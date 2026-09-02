using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages
{
    public class TrackColumn
    {
        public List<Track> Tracks { get; set; } = new();
    }

    [INotifyPropertyChanged]
    public sealed partial class HomePage : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();

        public BulkObservableCollection<Album> DiscoverAlbums { get; } = new();
        public BulkObservableCollection<Artist> TopArtists { get; } = new();
        public BulkObservableCollection<TrackColumn> TopTrackColumns { get; } = new();

        private List<Track> _topTracksList = new();

        [ObservableProperty]
        public partial Visibility DiscoverSectionVisibility { get; set; } = Visibility.Collapsed;

        [ObservableProperty]
        public partial Visibility TopArtistsSectionVisibility { get; set; } = Visibility.Collapsed;

        [ObservableProperty]
        public partial Visibility TopTracksSectionVisibility { get; set; } = Visibility.Collapsed;

        public HomePage()
        {
            InitializeComponent();

            library.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                library.LibraryChanged -= OnLibraryChanged;
                DiscoverAlbums.Clear();
                TopArtists.Clear();
                TopTrackColumns.Clear();
                _topTracksList.Clear();
            };
        }

        private void OnLibraryChanged()
        {
            DiscoverAlbums.Clear();
            TopArtists.Clear();
            TopTrackColumns.Clear();
            _topTracksList.Clear();
            _ = LoadDataAsync();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            var (discover, artists, topTracks, columns) = await Task.Run(() =>
            {
                var allAlbums = library.GetAllAlbums().ToList();
                var allTracks = library.GetAllTracks().ToList();
                var allArtists = library.GetAllArtists().ToList();

                var rng = new Random(DateTime.Today.Year * 1000 + DateTime.Today.DayOfYear);
                var discoverAlbums = allAlbums.OrderBy(_ => rng.Next()).Take(10).ToList();

                var artistPlays = allTracks
                    .GroupBy(t => t.ArtistId)
                    .Select(g => new { ArtistId = g.Key, TotalPlays = g.Sum(t => t.PlayCount) })
                    .Where(x => x.TotalPlays > 0)
                    .OrderByDescending(x => x.TotalPlays)
                    .Take(5)
                    .ToList();

                List<Artist> topArtistsList;
                if (artistPlays.Count > 0)
                {
                    var artistMap = allArtists.ToDictionary(a => a.Id);
                    topArtistsList = artistPlays
                        .Where(x => artistMap.ContainsKey(x.ArtistId))
                        .Select(x => artistMap[x.ArtistId])
                        .ToList();
                }
                else
                {
                    topArtistsList = allArtists.Take(5).ToList();
                }

                var topTracksList = allTracks
                    .Where(t => t.PlayCount > 0)
                    .OrderByDescending(t => t.PlayCount)
                    .ThenByDescending(t => t.DateAdded)
                    .Take(20)
                    .ToList();

                if (topTracksList.Count == 0)
                {
                    topTracksList = allTracks
                        .OrderByDescending(t => t.DateAdded)
                        .Take(20)
                        .ToList();
                }

                var trackColumns = new List<TrackColumn>();
                for (int i = 0; i < topTracksList.Count; i += 4)
                {
                    trackColumns.Add(new TrackColumn
                    {
                        Tracks = topTracksList.Skip(i).Take(4).ToList()
                    });
                }

                return (discoverAlbums, topArtistsList, topTracksList, trackColumns);
            });

            if (Frame == null) return;

            _topTracksList = topTracks;

            DiscoverAlbums.ReplaceRange(discover);
            DiscoverSectionVisibility = discover.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            TopArtists.ReplaceRange(artists);
            TopArtistsSectionVisibility = artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            TopTrackColumns.ReplaceRange(columns);
            TopTracksSectionVisibility = columns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AlbumGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.AlbumGridItem item && item.Album is Album selectedAlbum)
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardAlbumCover", item.CoverImageControl);
                Frame.Navigate(typeof(AlbumItemPage), selectedAlbum);
            }
        }

        private void ArtistGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.ArtistGridItem item && item.Artist is Artist selectedArtist)
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardArtistCover", item.CoverControl);
                Frame.Navigate(typeof(ArtistItemPage), selectedArtist);
            }
        }

        private async void TrackListItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.TrackListItem item && item.Song is Track selectedTrack)
            {
                var list = _topTracksList;
                int index = list.IndexOf(selectedTrack);
                if (index < 0) return;

                var queue = playback.Queue;
                queue.SetQueue(list);

                QueueTrack? qt;
                if (playback.ShuffleState)
                {
                    var selectedQueueTrack = QueueTrack.FromTrack(selectedTrack);
                    queue.ActiveQueue.ReplaceRange(queue.GetShuffledList(selectedQueueTrack));
                    queue.History.Clear();
                    qt = selectedQueueTrack;
                }
                else
                {
                    queue.PassQueue();
                    qt = queue.SkipAndTake(index);
                }

                if (qt == null) return;

                var track = await Task.Run(() => library.GetTrack(qt.Id));
                if (track == null) return;

                playback.PlayTrack(track);
                MainWindow.Instance?.NavigateToPlayer();
            }
        }
    }
}
