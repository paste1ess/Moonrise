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
    public class LibraryService
    {
        public static LibraryService Instance = new LibraryService();
        private DbService dbService;
        private string libraryPath;

        private LibraryService()
        {
            var savedPath = SettingsService.Instance.MusicLibraryPath;

            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                libraryPath = savedPath;
                dbService = new DbService(Path.Combine(savedPath, "library.db"));
            }
            else
            {
                dbService = new DbService(":memory:");
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
            var oldPath = dbService.DbPath;
            dbService.Dispose();

            libraryPath = path;

            dbService = new DbService(Path.Combine(path, "library.db"));

            ArtService.Instance.ClearCache();

            TaskService.Instance.ClearAndReset();
            TaskService.Instance.Enqueue(new RelayAppCommand(async (_) =>
            {
                await ScanFolder(libraryPath);
            }));
        }

        public async Task OpenAndScanLibrary(string path)
        {
            var dbPath = Path.Combine(path, "library.db");
            bool dbExists = File.Exists(dbPath);

            dbService.Dispose();

            libraryPath = path;

            dbService = new DbService(dbPath);

            if (!dbExists)
            {
                ArtService.Instance.ClearCache();
            }

            TaskService.Instance.ClearAndReset();
            TaskService.Instance.Enqueue(new RelayAppCommand(async (_) =>
            {
                await ScanFolder(libraryPath);
            }));
        }

        public async Task ScanFolder(string folderPath)
        {
            var dbTracks = dbService.GetAllTracks().ToDictionary(t => t.FilePath, StringComparer.OrdinalIgnoreCase);
            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".m4a", ".wav", ".wma", ".ogg"
            };

            var filesToParse = new ConcurrentBag<(string AbsolutePath, string RelativePath, string LastModified, long FileSize, string TrackId, bool IsFavorite)>();
            var unchangedTracks = new List<Track>();
            var seenRelativePaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            var dirInfo = new DirectoryInfo(folderPath);
            var filesOnDisk = dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories)
                                     .Where(f => supportedExtensions.Contains(f.Extension));

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
                        continue;
                    }
                }

                string trackId = cachedTrack?.Id ?? IdGenerator.NewTrackId();
                bool isFavorite = cachedTrack?.IsFavorite ?? false;

                filesToParse.Add((absolutePath, relativePath, lastModifiedStr, fileSize, trackId, isFavorite));
            }

            var tracksToSave = new ConcurrentBag<Track>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8)
            };

            await Parallel.ForEachAsync(filesToParse, parallelOptions, (file, cancellationToken) =>
            {
                try
                {
                    var track = ParseTrackMetadata(file.AbsolutePath, file.RelativePath, file.LastModified, file.FileSize, file.TrackId, file.IsFavorite);
                    if (track != null)
                    {
                        tracksToSave.Add(track);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
                return ValueTask.CompletedTask;
            });

            var allTracksToSave = tracksToSave.Concat(unchangedTracks).ToList();
            if (allTracksToSave.Count > 0)
            {
                dbService.UpsertTracksBatch(allTracksToSave);
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
                        IsFavorite = false
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
                        IsFavorite = false
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

        private Track? ParseTrackMetadata(string absolutePath, string relativePath, string lastModified, long fileSize, string trackId, bool isFavorite)
        {
            using var file = TagLib.File.Create(absolutePath);
            if (!file.Properties.MediaTypes.HasFlag(TagLib.MediaTypes.Audio)) return null;

            var artist = (file.Tag.Performers.FirstOrDefault() ?? "Unknown Artist").Trim();
            var album = (file.Tag.Album ?? "Unknown Album").Trim();
            var title = (file.Tag.Title ?? Path.GetFileNameWithoutExtension(absolutePath)).Trim();

            return new Track
            {
                Id = trackId,
                AlbumId = IdGenerator.GetAlbumId(album, artist),
                ArtistId = IdGenerator.GetArtistId(artist),
                Title = title,
                Album = album,
                Artist = artist,
                Year = (int)file.Tag.Year != 0 ? (int?)file.Tag.Year : null,
                Genre = file.Tag.Genres.FirstOrDefault(),
                Bpm = (int)file.Tag.BeatsPerMinute,
                FilePath = relativePath,
                FileSize = fileSize,
                Bitrate = file.Properties.AudioBitrate,
                Duration = file.Properties.Duration,
                DateAdded = DateTime.UtcNow,
                LastModified = lastModified,
                IsPresent = true,
                IsFavorite = isFavorite
            };
        }

        public async Task<Track?> GetTrack(string id)
        {
            return dbService.GetTrack(id);
        }

        public List<Track> GetAllTracks()
        {
            return dbService.GetAllTracks();
        }
    }
}
