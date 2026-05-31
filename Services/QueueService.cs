using CommunityToolkit.Mvvm.ComponentModel;
using Moonrise.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace Moonrise.Services
{
    public record QueueTrack
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;

        public static QueueTrack FromTrack(Track track)
        {
            return new QueueTrack
            {
                Id = track.Id,
                Artist = track.Artist,
                Title = track.Title,
                FilePath = track.FilePath
            };
        }
    }
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification = false;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnPropertyChanged(e);
        }
        public IEnumerable<T> GetRange(int index, int count)
        {
            if (Items is List<T> list)
            {
                return list.GetRange(index, count);
            }
            return this.Skip(index).Take(count).ToList();
        }

        public void ReplaceRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            _suppressNotification = true;
            try
            {
                if (Items is List<T> list)
                {
                    list.Clear();
                    list.AddRange(collection);
                }
                else
                {
                    Items.Clear();
                    foreach (var item in collection) Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
    public partial class QueueService : ObservableObject
    {
        [ObservableProperty]
        public partial BulkObservableCollection<QueueTrack> ActiveQueue { set; get; } = new();
        public List<QueueTrack> OriginalQueue { private set; get; } = new();
        [ObservableProperty]
        public partial BulkObservableCollection<QueueTrack> History { get; set; } = new();

        public void AddToStart(Track track)
        {
            ActiveQueue.Insert(0, QueueTrack.FromTrack(track));
        }

        public void AddToEnd(Track track)
        {
            ActiveQueue.Add(QueueTrack.FromTrack(track));
        }

        public QueueTrack? TakeFromStart()
        {
            if (ActiveQueue.Count == 0) return null;

            var track = ActiveQueue[0];
            ActiveQueue.RemoveAt(0);
            return track;
        }

        public void AddToHistory(QueueTrack track)
        {
            History.Add(track);
        }

        public QueueTrack? TakeFromHistory()
        {
            if (History.Count == 0) return null;

            int lastIndex = History.Count - 1;
            var track = History[lastIndex];
            History.RemoveAt(lastIndex);
            return track;
        }

        public void SetQueue(List<Track> tracks)
        {
            var list = new List<QueueTrack>(tracks.Count);
            for (int i = 0; i < tracks.Count; i++)
            {
                list.Add(QueueTrack.FromTrack(tracks[i]));
            }
            OriginalQueue = list;
        }

        public void PassQueue()
        {
            History.Clear();
            ActiveQueue.ReplaceRange(OriginalQueue);
        }

        public void ShuffleQueue()
        {
            ActiveQueue.ReplaceRange(GetShuffledList());
        }

        public List<QueueTrack> GetShuffledList(QueueTrack? excludeTrack = null)
        {
            var list = new List<QueueTrack>(OriginalQueue.Count);
            bool excluded = false;
            foreach (var track in OriginalQueue)
            {
                if (!excluded && excludeTrack != null && track.Id == excludeTrack.Id)
                {
                    excluded = true;
                    continue;
                }
                list.Add(track);
            }

            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Random.Shared.Next(n + 1);
                QueueTrack value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
            return list;
        }

        public QueueTrack? SkipAndTake(int index)
        {
            if (index < 0 || index >= ActiveQueue.Count) return null;
            QueueTrack selectedTrack = ActiveQueue[index];
            var remainingTracks = ActiveQueue.GetRange(index + 1, ActiveQueue.Count - index - 1);

            ActiveQueue.ReplaceRange(remainingTracks);
            return selectedTrack;
        }

        public void ClearAll()
        {
            ActiveQueue.Clear();
            OriginalQueue.Clear();
            History.Clear();
        }
    }
}
