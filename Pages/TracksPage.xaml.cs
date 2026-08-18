using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    public sealed partial class TracksPage : Page
    {
        public BulkObservableCollection<Track> Tracks { get; } = new();

        public TracksPage()
        {
            InitializeComponent();
            TrackListView.Tracks = Tracks;

            LibraryService.Instance.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                LibraryService.Instance.LibraryChanged -= OnLibraryChanged;
                Tracks.Clear();
            };
        }

        private void OnLibraryChanged() => Tracks.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var trackList = await Task.Run(() =>
                LibraryService.Instance.GetAllTracks()
                    .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Tracks.ReplaceRange(trackList);
            TrackListView.Tracks = Tracks;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            var selectedTrack = list.First();

            PlaybackService.Instance.Queue.SetQueue(list);
            PlaybackService.Instance.Queue.PassQueue();
            PlaybackService.Instance.Queue.SkipAndTake(0);

            PlaybackService.Instance.ShuffleState = false;

            PlaybackService.Instance.PlayTrack(selectedTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            PlaybackService.Instance.Queue.SetQueue(list);

            var shuffledQueueTracks = PlaybackService.Instance.Queue.GetShuffledList();
            if (shuffledQueueTracks.Count == 0) return;

            var firstQueueTrack = shuffledQueueTracks[0];
            var firstTrack = list.FirstOrDefault(t => t.Id == firstQueueTrack.Id);
            if (firstTrack == null) return;

            var remainingShuffled = shuffledQueueTracks.Skip(1).ToList();
            PlaybackService.Instance.Queue.ActiveQueue.ReplaceRange(remainingShuffled);
            PlaybackService.Instance.Queue.History.Clear();

            PlaybackService.Instance.ShuffleState = true;

            PlaybackService.Instance.PlayTrack(firstTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }
    }
}