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
        // Property for data binding
        public BulkObservableCollection<Track> Tracks { get; } = new();

        public TracksPage()
        {
            InitializeComponent();
            LibraryService.Instance.LibraryChanging += OnLibraryChanging;
            Unloaded += (s, e) =>
            {
                LibraryService.Instance.LibraryChanging -= OnLibraryChanging;
                Tracks.Clear();
            };
        }

        private void OnLibraryChanging() => Tracks.Clear();

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
            TrackListView.ItemsSource = Tracks;
        }

        private async void TrackListItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.TrackListItem item && item.Song is Track selectedTrack)
            {
                var list = Tracks.ToList();
                int index = list.IndexOf(selectedTrack);
                if (index < 0) return;

                PlaybackService.Instance.Queue.SetQueue(list);
                var qt = PlaybackService.Instance.Queue.SkipAndTake(index);

                var track = await Task.Run(() => LibraryService.Instance.GetTrack(qt.Id));
                if (track == null) return;

                PlaybackService.Instance.PlayTrack(track);
                MainWindow.Instance?.NavigateToPlayer();
            }
        }
    }
}