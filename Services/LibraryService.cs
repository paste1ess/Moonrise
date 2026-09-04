using Microsoft.Extensions.DependencyInjection;
using Moonrise.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Moonrise.Services
{
    public record LyricsChangedEventArgs(string TrackId, string? Lyrics, bool IsSynced = false, bool SaveToDisk = false);

    public interface ILibraryService
    {
        event Action? LibraryChanging;
        event Action? LibraryChanged;
        void Initialize();
        string PathToAbsolute(string relativePath);
        Task HardScanLibrary(string path);
        Task OpenAndScanLibrary(string path);
        Task ScanFolder(string folderPath);
        Task<string?> GetLyrics(string id);
        Task<string?> GetSyncedLyrics(string id);
        Task SetLyrics(string trackId, string? lyrics, bool isSynced = false, bool saveToDisk = false);
        Task ClearLyrics(string trackId, bool isSynced = false, bool saveToDisk = true);
        event EventHandler<LyricsChangedEventArgs>? LyricsChanged;

        Task<Track?> GetTrack(string id);
        IEnumerable<Track> GetAllTracks();
        IEnumerable<Track> GetAllFavoriteTracks();
        IEnumerable<Track> GetTracksByIds(IEnumerable<string> ids);
        Task SetTrackFavorite(string trackId, bool isFavorite);
        Task IncrementPlayCount(string trackId);

        Task<Album?> GetAlbum(string id);
        IEnumerable<Album> GetAllAlbums();
        IEnumerable<Album> GetAlbumsByIds(IEnumerable<string> ids);

        Task<Artist?> GetArtist(string id);
        IEnumerable<Artist> GetAllArtists();
        IEnumerable<Album> GetArtistsAlbums(string artistId);

        IEnumerable<Playlist> GetAllPlaylists();
        Task<Playlist?> GetPlaylist(string id);
        //Task UpsertPlaylist(Playlist playlist);

    }

    public class LibraryService : ILibraryService
    {
        private readonly IArtService art;
        private readonly ITaskService task;
        private readonly ISettingsService settings;
        private readonly IToastService toast;
        private IPlaybackService playback => App.Services.GetRequiredService<IPlaybackService>();
        private DbService dbService;
        private string libraryPath;
        private readonly ConcurrentDictionary<string, string> _sessionLyrics = new();
        private readonly ConcurrentDictionary<string, string> _sessionSyncedLyrics = new();

        public event Action? LibraryChanging;
        public event Action? LibraryChanged;
        public event EventHandler<LyricsChangedEventArgs>? LyricsChanged;

        public LibraryService(ISettingsService settingsService, IArtService artService, ITaskService taskService, IToastService toastService)
        {
            settings = settingsService;
            art = artService;
            task = taskService;
            toast = toastService;

            var savedPath = settings.MusicLibraryPath;

            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                libraryPath = savedPath;
                dbService = new DbService(Path.Combine(savedPath, "moonrise.db"));
            }
            else
            {
                dbService = new DbService(":memory:");
            }
        }

        public void Initialize()
        {
            var savedPath = settings.MusicLibraryPath;

            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                if (libraryPath != savedPath || dbService == null)
                {
                    dbService?.Dispose();
                    libraryPath = savedPath;
                    dbService = new DbService(Path.Combine(savedPath, "moonrise.db"));
                }

                task.Enqueue(new RelayAppCommand(async (_) =>
                {
                    await ScanFolder(libraryPath);
                }));
            }
        }

        public string PathToAbsolute(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }
            return Path.Combine(libraryPath ?? string.Empty, relativePath);
        }

        public async Task HardScanLibrary(string path)
        {
            LibraryChanging?.Invoke();
            playback.ResetForLibraryChange();
            task.ClearAndReset();
            task.Enqueue(new RelayAppCommand(async (_) =>
            {
                dbService.Dispose();
                libraryPath = path;
                dbService = new DbService(Path.Combine(path, "moonrise.db"));
                dbService.ResetDb();
                art.ClearCache();
                toast.Show(string.Empty, "The database has been reset for " + path, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                await ScanFolder(libraryPath);
                LibraryChanged?.Invoke();
            }));
        }

        public async Task OpenAndScanLibrary(string path)
        {
            LibraryChanging?.Invoke();
            playback.ResetForLibraryChange();

            var dbPath = Path.Combine(path, "moonrise.db");
            bool dbExists = File.Exists(dbPath);

            dbService.Dispose();

            libraryPath = path;

            dbService = new DbService(dbPath);

            art.ClearCache();

            task.ClearAndReset();
            task.Enqueue(new RelayAppCommand(async (_) =>
            {
                await ScanFolder(libraryPath);
            }));
        }

        public async Task ScanFolder(string folderPath)
        {
            using var toastHandle = toast.ShowProgress(string.Empty, "Scanning 0 songs", isIndeterminate: true, isClosable: true);
            art.ClearCache();

            var dbTracks = dbService.GetAllTracks().ToDictionary(t => t.FilePath, StringComparer.OrdinalIgnoreCase);
            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".m4a", ".wav", ".wma", ".ogg"
            };

            var filesToParse = new ConcurrentBag<(string AbsolutePath, string RelativePath, string LastModified, long FileSize, string TrackId, bool IsFavorite, DateTime DateAdded, int PlayCount)>();
            var unchangedTracks = new List<Track>();
            var seenRelativePaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            var dirInfo = new DirectoryInfo(folderPath);
            var filesOnDisk = dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories)
                                     .Where(f => supportedExtensions.Contains(f.Extension));

            int scannedCount = 0;

            foreach (var fileInfo in filesOnDisk)
            {
                var absolutePath = fileInfo.FullName;
                var relativePath = Path.GetRelativePath(folderPath, absolutePath);
                seenRelativePaths.TryAdd(relativePath, 0);

                var lastModifiedStr = fileInfo.LastWriteTimeUtc.ToString("O");
                var fileSize = fileInfo.Length;

                if (dbTracks.TryGetValue(relativePath, out var cachedTrack))
                {
                    if (cachedTrack.LastModified == lastModifiedStr && cachedTrack.FileSize == fileSize)
                    {
                        if (!cachedTrack.IsPresent)
                        {
                            unchangedTracks.Add(cachedTrack with { IsPresent = true });
                        }
                        scannedCount++;
                        if (scannedCount % 50 == 0)
                        {
                            toastHandle.Update(message: $"Scanning {scannedCount} songs");
                        }
                        continue;
                    }
                }

                string trackId = cachedTrack?.Id ?? IdGenerator.NewTrackId();
                bool isFavorite = cachedTrack?.IsFavorite ?? false;
                DateTime dateAdded = cachedTrack?.DateAdded ?? DateTime.UtcNow;
                int playCount = cachedTrack?.PlayCount ?? 0;

                filesToParse.Add((absolutePath, relativePath, lastModifiedStr, fileSize, trackId, isFavorite, dateAdded, playCount));
            }

            toastHandle.Update(message: $"Scanning {scannedCount} songs");

            var tracksToSave = new ConcurrentBag<Track>();
            var lyricsToSave = new ConcurrentBag<(string TrackId, string Lyrics)>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8)
            };

            int totalFiles = filesToParse.Count + unchangedTracks.Count;

            await Parallel.ForEachAsync(filesToParse, parallelOptions, (file, cancellationToken) =>
            {
                try
                {
                    var result = ParseTrackMetadata(file.AbsolutePath, file.RelativePath, file.LastModified, file.FileSize, file.TrackId, file.IsFavorite, file.DateAdded, file.PlayCount);
                    if (result.Item1 != null)
                    {
                        tracksToSave.Add(result.Item1);
                        if (result.Item2 != null)
                        {
                            lyricsToSave.Add((result.Item1.Id, result.Item2));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
                finally
                {
                    int current = Interlocked.Increment(ref scannedCount);
                    if (current % 10 == 0 || current == totalFiles)
                    {
                        toastHandle.Update(message: $"Scanning {current} songs");
                    }
                }
                return ValueTask.CompletedTask;
            });

            var allTracksToSave = tracksToSave.Concat(unchangedTracks).ToList();
            if (allTracksToSave.Count > 0)
            {
                dbService.UpsertTracksBatch(allTracksToSave);
            }

            if (lyricsToSave.Count > 0)
            {
                dbService.UpsertLyricsBatch(lyricsToSave);
            }

            var missingTracks = dbTracks.Values
                .Where(t => t.IsPresent && !seenRelativePaths.ContainsKey(t.FilePath))
                .Select(t => t with { IsPresent = false })
                .ToList();

            if (missingTracks.Count > 0)
            {
                dbService.UpsertTracksBatch(missingTracks);
            }

            bool hasTrackChanges = allTracksToSave.Count > 0 || missingTracks.Count > 0;
            List<Track>? allPresentTracks = null;

            if (hasTrackChanges)
            {
                allPresentTracks = dbService.GetAllTracks().Where(t => t.IsPresent).ToList();

                var existingAlbums = dbService.GetAllAlbums().ToDictionary(a => a.Id);
                var existingArtists = dbService.GetAllArtists().ToDictionary(a => a.Id);

                var albumsToSave = allPresentTracks
                    .GroupBy(t => t.AlbumId)
                    .Select(g => {
                        var first = g.First();
                        var trackIds = g.Select(t => t.Id).ToArray();
                        return new Album
                        {
                            Id = g.Key,
                            ArtistId = first.ArtistId,
                            Title = first.Album,
                            Artist = first.Artist,
                            TrackIds = trackIds,
                            Year = g.Where(t => t.Year.HasValue).Select(t => t.Year!.Value).FirstOrDefault(),
                            Genre = g.Where(t => t.Genre != null).Select(t => t.Genre).FirstOrDefault(),
                            DateAdded = g.Min(t => t.DateAdded),
                            IsFavorite = existingAlbums.TryGetValue(g.Key, out var existingAlbum) ? existingAlbum.IsFavorite : false
                        };
                    }).ToList();

                var artistsToSave = allPresentTracks
                    .GroupBy(t => t.ArtistId)
                    .Select(g => {
                        var first = g.First();
                        var albumIds = g.Select(t => t.AlbumId).Distinct().ToArray();
                        return new Artist
                        {
                            Id = g.Key,
                            AlbumIds = albumIds,
                            Name = first.Artist,
                            DateAdded = g.Min(t => t.DateAdded),
                            IsFavorite = existingArtists.TryGetValue(g.Key, out var existingArtist) ? existingArtist.IsFavorite : false
                        };
                    }).ToList();

                if (albumsToSave.Count > 0)
                {
                    dbService.UpsertAlbumsBatch(albumsToSave);
                }

                if (artistsToSave.Count > 0)
                {
                    dbService.UpsertArtistsBatch(artistsToSave);
                }
            }

            var playlistExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".m3u8", ".m3u", ".m3a8", ".m3a"
            };

            var playlistFiles = dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories)
                                       .Where(f => playlistExtensions.Contains(f.Extension))
                                       .ToList();

            if (playlistFiles.Count > 0)
            {
                allPresentTracks ??= dbService.GetAllTracks().Where(t => t.IsPresent).ToList();

                var trackPathLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var track in allPresentTracks)
                {
                    trackPathLookup[track.FilePath] = track.Id;
                    trackPathLookup[track.FilePath.Replace('/', '\\')] = track.Id;
                    trackPathLookup[track.FilePath.Replace('\\', '/')] = track.Id;

                    var fullPath = Path.GetFullPath(Path.Combine(folderPath, track.FilePath));
                    trackPathLookup[fullPath] = track.Id;

                    var relPath = Path.GetRelativePath(folderPath, fullPath);
                    trackPathLookup[relPath] = track.Id;
                    trackPathLookup[relPath.Replace('/', '\\')] = track.Id;
                    trackPathLookup[relPath.Replace('\\', '/')] = track.Id;
                }

                var playlistsToSave = new List<Playlist>();
                foreach (var playlistFile in playlistFiles)
                {
                    try
                    {
                        var playlistRelPath = Path.GetRelativePath(folderPath, playlistFile.FullName);
                        var title = Path.GetFileNameWithoutExtension(playlistFile.Name);
                        var playlistId = IdGenerator.GetPlaylistId(title, playlistRelPath);
                        var trackIds = new List<string>();
                        foreach (var rawPath in ExtractPaths(playlistFile.FullName))
                        {
                            if (trackPathLookup.TryGetValue(rawPath, out var trackId))
                            {
                                trackIds.Add(trackId);
                            }
                            else
                            {
                                var rel = Path.GetRelativePath(folderPath, rawPath);
                                if (trackPathLookup.TryGetValue(rel, out trackId))
                                {
                                    trackIds.Add(trackId);
                                }
                            }
                        }
                        playlistsToSave.Add(new Playlist
                        {
                            Id = playlistId,
                            Title = title,
                            TrackIds = trackIds.ToArray()
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }

                if (playlistsToSave.Count > 0)
                {
                    dbService.UpsertPlaylistsBatch(playlistsToSave);
                }
            }

            toastHandle.Complete($"Finished scanning {scannedCount} songs.");
        }

        private (Track?, string?) ParseTrackMetadata(string absolutePath, string relativePath, string lastModified, long fileSize, string trackId, bool isFavorite, DateTime dateAdded, int playCount)
        {
            using var file = TagLib.File.Create(absolutePath);
            if (!file.Properties.MediaTypes.HasFlag(TagLib.MediaTypes.Audio)) return (null, null);

            var artist = (file.Tag.Performers.FirstOrDefault() ?? "Unknown Artist").Trim();
            var albumArtist = (file.Tag.FirstAlbumArtist ?? artist).Trim();
            var album = (file.Tag.Album ?? "Unknown Album").Trim();
            var title = (file.Tag.Title ?? Path.GetFileNameWithoutExtension(absolutePath)).Trim();

            var track = new Track
            {
                Id = trackId,
                AlbumId = IdGenerator.GetAlbumId(album, albumArtist),
                ArtistId = IdGenerator.GetArtistId(artist),
                Title = title,
                Album = album,
                Artist = artist,
                TrackNumber = file.Tag.Track != 0 ? (int?)file.Tag.Track : null,
                Year = (int)file.Tag.Year != 0 ? (int?)file.Tag.Year : null,
                Genre = file.Tag.Genres.FirstOrDefault(),
                Bpm = file.Tag.BeatsPerMinute != 0 ? (int?)file.Tag.BeatsPerMinute : null,
                FilePath = relativePath,
                FileSize = fileSize,
                Bitrate = file.Properties.AudioBitrate,
                Duration = file.Properties.Duration,
                DateAdded = dateAdded,
                LastModified = lastModified,
                IsPresent = true,
                IsFavorite = isFavorite,
                PlayCount = playCount
            };

            return (track, file.Tag.Lyrics);
        }

        public async Task<Track?> GetTrack(string id)
        {
            return dbService.GetTrack(id);
        }

        public async Task SetTrackFavorite(string trackId, bool isFavorite)
        {
            dbService.SetTrackFavorite(trackId, isFavorite);
        }

        public async Task IncrementPlayCount(string trackId)
        {
            dbService.IncrementPlayCount(trackId);
        }

        public async Task<string?> GetLyrics(string id)
        {
            if (_sessionLyrics.TryGetValue(id, out var sessionLyric))
            {
                return sessionLyric;
            }
            return dbService.GetLyrics(id);
        }

        public async Task<string?> GetSyncedLyrics(string id)
        {
            if (_sessionSyncedLyrics.TryGetValue(id, out var sessionSynced))
            {
                return sessionSynced;
            }
            return null;
        }

        public async Task SetLyrics(string trackId, string? lyrics, bool isSynced = false, bool saveToDisk = false)
        {
            if (!isSynced)
            {
                if (!saveToDisk)
                {
                    _sessionLyrics[trackId] = lyrics ?? string.Empty;
                }
                else
                {
                    _sessionLyrics.TryRemove(trackId, out _);
                }
            }
            else
            {
                if (!saveToDisk)
                {
                    _sessionSyncedLyrics[trackId] = lyrics ?? string.Empty;
                }
                else
                {
                    _sessionSyncedLyrics.TryRemove(trackId, out _);
                }
            }

            LyricsChanged?.Invoke(this, new LyricsChangedEventArgs(trackId, lyrics, isSynced, saveToDisk));

            if (saveToDisk)
            {
                await Task.Run(async () =>
                {
                    var track = dbService.GetTrack(trackId);
                    if (track != null)
                    {
                        var absPath = PathToAbsolute(track.FilePath);
                        if (File.Exists(absPath))
                        {
                            if (!isSynced)
                            {
                                try
                                {
                                    var abstraction = new SafeFileAbstraction(absPath);
                                    using var file = TagLib.File.Create(abstraction);
                                    file.Tag.Lyrics = lyrics;
                                    file.Save();

                                    var fileInfo = new FileInfo(absPath);
                                    var updatedTrack = track with
                                    {
                                        LastModified = fileInfo.LastWriteTimeUtc.ToString("O"),
                                        FileSize = fileInfo.Length
                                    };
                                    dbService.UpsertTrack(updatedTrack);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    task.Dispatcher.TryEnqueue(() =>
                                        toast.Show("Save error", $"Failed to update file tags: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error));
                                }

                                dbService.UpsertLyricsBatch([(trackId, lyrics ?? string.Empty)]);
                            }
                            else
                            {
                                try
                                {
                                    string lrcPath = Path.ChangeExtension(absPath, ".lrc");
                                    if (!string.IsNullOrEmpty(lyrics))
                                    {
                                        await File.WriteAllTextAsync(lrcPath, lyrics);
                                    }
                                    else if (File.Exists(lrcPath))
                                    {
                                        File.Delete(lrcPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    task.Dispatcher.TryEnqueue(() =>
                                        toast.Show("Save error", $"Failed to save .lrc file: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error));
                                }
                            }
                        }
                    }
                });
            }
        }

        public async Task ClearLyrics(string trackId, bool isSynced = false, bool saveToDisk = true)
        {
            if (!isSynced)
            {
                _sessionLyrics.TryRemove(trackId, out _);
            }
            else
            {
                _sessionSyncedLyrics.TryRemove(trackId, out _);
            }

            LyricsChanged?.Invoke(this, new LyricsChangedEventArgs(trackId, null, isSynced, saveToDisk));

            if (saveToDisk)
            {
                await Task.Run(() =>
                {
                    var track = dbService.GetTrack(trackId);
                    if (track != null)
                    {
                        var absPath = PathToAbsolute(track.FilePath);
                        if (File.Exists(absPath))
                        {
                            if (!isSynced)
                            {
                                try
                                {
                                    var abstraction = new SafeFileAbstraction(absPath);
                                    using var file = TagLib.File.Create(abstraction);
                                    file.Tag.Lyrics = null;
                                    file.Save();

                                    var fileInfo = new FileInfo(absPath);
                                    var updatedTrack = track with
                                    {
                                        LastModified = fileInfo.LastWriteTimeUtc.ToString("O"),
                                        FileSize = fileInfo.Length
                                    };
                                    dbService.UpsertTrack(updatedTrack);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    task.Dispatcher.TryEnqueue(() =>
                                        toast.Show("Delete error", $"Failed to clear file tags: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error));
                                }

                                dbService.UpsertLyricsBatch([(trackId, string.Empty)]);
                            }
                            else
                            {
                                try
                                {
                                    string lrcPath = Path.ChangeExtension(absPath, ".lrc");
                                    if (File.Exists(lrcPath))
                                    {
                                        File.Delete(lrcPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    task.Dispatcher.TryEnqueue(() =>
                                        toast.Show("Delete error", $"Failed to delete .lrc file: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error));
                                }
                            }
                        }
                    }
                });
            }
        }

        public IEnumerable<Track> GetAllTracks()
        {
            return dbService.GetAllTracks();
        }
        public IEnumerable<Track> GetAllFavoriteTracks()
        {
            return dbService.GetAllFavoriteTracks();
        }

        public IEnumerable<Track> GetTracksByIds(IEnumerable<string> ids)
        {
            return dbService.GetTracksByIds(ids);
        }

        public async Task<Album?> GetAlbum(string id)
        {
            return dbService.GetAlbum(id);
        }

        public IEnumerable<Album> GetAllAlbums()
        {
            return dbService.GetAllAlbums();
        }

        public IEnumerable<Album> GetAlbumsByIds(IEnumerable<string> ids)
        {
            return dbService.GetAlbumsByIds(ids);
        }

        public async Task<Artist?> GetArtist(string id)
        {
            return dbService.GetArtist(id);
        }
        public IEnumerable<Artist> GetAllArtists()
        {
            return dbService.GetAllArtists();
        }
        public IEnumerable<Album> GetArtistsAlbums(string artistId)
        {
            var artist = dbService.GetArtist(artistId);
            if (artist == null || artist.AlbumIds == null || artist.AlbumIds.Length == 0)
            {
                return Enumerable.Empty<Album>();
            }

            return dbService.GetAlbumsByIds(artist.AlbumIds);
        }

        public IEnumerable<Playlist> GetAllPlaylists()
        {
            return dbService.GetAllPlaylists();
        }

        public async Task<Playlist?> GetPlaylist(string id)
        {
            return dbService.GetPlaylistFromId(id);
        }

        static IEnumerable<string> ExtractPaths(string m3u8Path)
        {
            var playlistDir = Path.GetDirectoryName(m3u8Path) ?? string.Empty;

            foreach (var rawLine in File.ReadLines(m3u8Path, Encoding.UTF8))
            {
                var line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                string pathStr = line;
                if (pathStr.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(pathStr, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
                    {
                        pathStr = fileUri.LocalPath;
                    }
                    else
                    {
                        pathStr = pathStr.Substring(7).TrimStart('/');
                    }
                }

                try
                {
                    pathStr = Uri.UnescapeDataString(pathStr);
                }
                catch { }

                var normalized = pathStr.Replace('/', Path.DirectorySeparatorChar);

                if (!Path.IsPathRooted(normalized))
                {
                    normalized = Path.GetFullPath(Path.Combine(playlistDir, normalized));
                }
                else
                {
                    try
                    {
                        normalized = Path.GetFullPath(normalized);
                    }
                    catch { }
                }

                yield return normalized;
            }
        }
    }

    public class SafeFileAbstraction : TagLib.File.IFileAbstraction
    {
        private Stream? _readStream;
        private Stream? _writeStream;

        public string Name { get; }

        public SafeFileAbstraction(string name)
        {
            Name = name;
        }

        public Stream ReadStream => _readStream ??= new FileStream(Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        public Stream WriteStream => _writeStream ??= new FileStream(Name, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        public void CloseStream(Stream stream)
        {
            if (stream == _readStream)
            {
                _readStream?.Dispose();
                _readStream = null;
            }
            else if (stream == _writeStream)
            {
                _writeStream?.Dispose();
                _writeStream = null;
            }
            else
            {
                stream?.Dispose();
            }
        }
    }
}
