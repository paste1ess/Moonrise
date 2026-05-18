using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Services
{
    public class QueueService
    {
        public List<Track> QueueList { private set; get; } = new();
    }
}
