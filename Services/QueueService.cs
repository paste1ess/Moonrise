using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Services
{
    public class QueueTrack
    {
        required public string Id { get; init; }
        required public string Title { get; init; }
        required public string Artist { get; init; }
        public static QueueTrack FromTrack(Track track) { return new QueueTrack { Id = track.Id, Artist = track.Artist, Title = track.Title }; }
    }

    public class QueueService
    {
        public List<QueueTrack> QueueList { private set; get; } = new();

        public void AddToStart(Track track)
        {
            QueueList.Insert(0, QueueTrack.FromTrack(track));
        }

        public void AddToEnd(Track track)
        {
            QueueList.Add(QueueTrack.FromTrack(track));
        }

        public QueueTrack? TakeFromStart()
        {
            if (QueueList.Count == 0) return null;

            var track = QueueList[0];
            QueueList.RemoveAt(0);
            return track;
        }
    }
}
