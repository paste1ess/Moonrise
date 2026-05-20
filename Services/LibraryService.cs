using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Services
{
    public class LibraryService
    {
        public static LibraryService Instance = new LibraryService();
        private DbService dbService;
        private string libraryPath;

        private LibraryService()
        {
            dbService = new DbService("./library.db");
        }

        /// <summary>
        /// Replaces the previous library and rescans a directory for library data. Should be a last resort or if a new library is being scanned
        /// </summary>
        /// <param name="path"></param>
        public async Task HardScanLibrary(string path)
        {
            var oldPath = dbService.DbPath;
            dbService.Dispose();

            //File.Delete(oldPath);

            libraryPath = path;

            dbService = new DbService(Path.Combine(path, "library.db"));

            TaskService.Instance.ClearAndReset();
            TaskService.Instance.Enqueue(new RelayAppCommand(async (_) =>
            {
                await ScanFolder(libraryPath);
            }));


        }

        public async Task ScanFolder(string folderPath)
        {
            foreach (var file in Directory.GetFiles(folderPath))
            {
                // do something with file
            }

            foreach (var subDir in Directory.GetDirectories(folderPath))
            {
                await ScanFolder(subDir);
            }
        }

        public async Task<Track?> GetTrack(string id)
        {
            return dbService.GetTrack(id);
        }

        public async Task<Track?> ScanTrackFromFile(string path)
        {
            using (var file = TagLib.File.Create(path))
            {
                if (!file.Properties.MediaTypes.HasFlag(TagLib.MediaTypes.Audio)) return null;
                Track track = new Track
                {
                    Id = IdGenerator.NewTrackId(),
                    AlbumId = IdGenerator.NewAlbumId(), // fix this!!
                    ArtistId = IdGenerator.NewArtistId(), // fix this too!!!

                    Title = file.Tag.Title,
                    Album = file.Tag.Album,
                    Artist = file.Tag.Performers.First(),

                    FilePath = path,
                    Duration = file.Properties.Duration
                };

                return track;
            }
        }
    }
}
