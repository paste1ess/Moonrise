using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    [INotifyPropertyChanged]
    public sealed partial class PlaylistItemPage : Page
    {
        private CancellationTokenSource? _artworkCts;
        private readonly IArtService art = App.Services.GetRequiredService<IArtService>();
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();

        [ObservableProperty]
        public partial ImageSource? PlaylistArt { get; set; }

        public Playlist DisplayedPlaylist
        {
            get => (Playlist)GetValue(DisplayedPlaylistProperty);
            set => SetValue(DisplayedPlaylistProperty, value);
        }

        public static readonly DependencyProperty DisplayedPlaylistProperty =
            DependencyProperty.Register(nameof(DisplayedPlaylist), typeof(Playlist), typeof(PlaylistItemPage), new PropertyMetadata(null));

        [ObservableProperty]
        public partial IEnumerable<Track>? Tracks { get; set; }

        public PlaylistItemPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is Playlist playlist)
            {
                DisplayedPlaylist = playlist;
                Tracks = library.GetTracksByIds(playlist.TrackIds).ToList();
                LoadArtworkAsync(playlist);

                var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("ForwardPlaylistCover");
                animation?.TryStart(PlaylistCoverContainer);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = null;
        }

        private async void LoadArtworkAsync(Playlist playlist)
        {
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = new CancellationTokenSource();
            var token = _artworkCts.Token;

            var cached = art.GetCachedArtwork(playlist.Id, 320) ?? art.GetCachedArtwork(playlist.Id, 174);
            PlaylistArt = cached;

            try
            {
                var artImage = await art.GetArtwork(playlist, 320, token);
                if (!token.IsCancellationRequested)
                    PlaylistArt = artImage;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            var selectedTrack = list.First();

            playback.Queue.SetQueue(list);
            playback.Queue.PassQueue();
            playback.Queue.SkipAndTake(0);

            playback.ShuffleState = false;

            playback.PlayTrack(selectedTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            playback.Queue.SetQueue(list);

            var shuffledQueueTracks = playback.Queue.GetShuffledList();
            if (shuffledQueueTracks.Count == 0) return;

            var firstQueueTrack = shuffledQueueTracks[0];
            var firstTrack = list.FirstOrDefault(t => t.Id == firstQueueTrack.Id);
            if (firstTrack == null) return;

            var remainingShuffled = shuffledQueueTracks.Skip(1).ToList();
            playback.Queue.ActiveQueue.ReplaceRange(remainingShuffled);
            playback.Queue.History.Clear();

            playback.ShuffleState = true;

            playback.PlayTrack(firstTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }
    }
}
