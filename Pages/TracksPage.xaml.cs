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
        public ObservableCollection<Track> Tracks { get; } = new();

        public TracksPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var trackList = await Task.Run(() => LibraryService.Instance.GetAllTracks());

            if (Frame == null) return;

            TrackListView.ItemsSource = null;
            Tracks.Clear();
            foreach (var track in trackList)
            {
                Tracks.Add(track);
            }
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