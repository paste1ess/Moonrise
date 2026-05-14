using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Services
{
    public class LibraryService
    {
        public static LibraryService Instance { get; } = new LibraryService();
        private readonly DbService dbService;

        private LibraryService()
        {
            dbService = new DbService("./library.db");
        }
        async public void GetTrack(string id)
        {
            TaskService.Instance.Enqueue(new RelayAppCommand(async () =>
            {

            }));
        }
    }
}
