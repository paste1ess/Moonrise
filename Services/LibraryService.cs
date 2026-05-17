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
        async public Task HardScanLibrary(string path)
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

        async public Task ScanFolder(string folderPath)
        {

        }

        async public Task GetTrack(string id)
        {
            
        }
    }
}
