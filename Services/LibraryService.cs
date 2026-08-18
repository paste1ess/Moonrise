using Microsoft.Extensions.DependencyInjection;
using Moonrise.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Moonrise.Services
{
    public interface ILibraryService
    {
        event Action? LibraryChanging;
        event Action? LibraryChanged;
        void Initialize();
        string PathToAbsolute(string relativePath);
        Task HardScanLibrary(string path);
        Task OpenAndScanLibrary(string path);
        Task ScanFolder(string folderPath);
        Task<Track?> GetTrack(string id);
        Task<string?> GetLyrics(string id);
        IEnumerable<Track> GetAllTracks();
        IEnumerable<Track> GetTracksByIds(IEnumerable<string> ids);
        Task<Album?> GetAlbum(string id);
        IEnumerable<Album> GetAllAlbums();
    }

    public class LibraryService : ILibraryService
    {
        private readonly IArtService art;
        private readonly ITaskService task;
        private readonly ISettingsService settings;
        private readonly IToastService toast;
        private DbService dbService;
        private string libraryPath;

        public event Action? LibraryChanging;
        public event Action? LibraryChanged;

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
            PlaybackService.Instance.ResetForLibraryChange();
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
            PlaybackService.Instance.ResetForLibraryChange();

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

            var filesToParse = new ConcurrentBag<(string AbsolutePath, string RelativePath, string LastModified, long FileSize, string TrackId, bool IsFavorite, DateTime DateAdded)>();
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

                filesToParse.Add((absolutePath, relativePath, lastModifiedStr, fileSize, trackId, isFavorite, dateAdded));
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
                    var result = ParseTrackMetadata(file.AbsolutePath, file.RelativePath, file.LastModified, file.FileSize, file.TrackId, file.IsFavorite, file.DateAdded);
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

            var allPresentTracks = dbService.GetAllTracks().Where(t => t.IsPresent).ToList();

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

            toastHandle.Complete($"Finished scanning {scannedCount} songs.");
        }

        private (Track?, string?) ParseTrackMetadata(string absolutePath, string relativePath, string lastModified, long fileSize, string trackId, bool isFavorite, DateTime dateAdded)
        {
            using var file = TagLib.File.Create(absolutePath);
            if (!file.Properties.MediaTypes.HasFlag(TagLib.MediaTypes.Audio)) return (null, null);

            var artist = (file.Tag.Performers.FirstOrDefault() ?? "Unknown Artist").Trim();
            var album = (file.Tag.Album ?? "Unknown Album").Trim();
            var title = (file.Tag.Title ?? Path.GetFileNameWithoutExtension(absolutePath)).Trim();

            var track = new Track
            {
                Id = trackId,
                AlbumId = IdGenerator.GetAlbumId(album, artist),
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
                IsFavorite = isFavorite
            };

            return (track, file.Tag.Lyrics);
        }

        public async Task<Track?> GetTrack(string id)
        {
            return dbService.GetTrack(id);
        }

        public async Task<string?> GetLyrics(string id)
        {
            return dbService.GetLyrics(id);
        }

        public IEnumerable<Track> GetAllTracks()
        {
            return dbService.GetAllTracks();
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
    }
}
