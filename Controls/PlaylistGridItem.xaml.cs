using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class PlaylistGridItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IArtService art = App.Services.GetRequiredService<IArtService>();
        private readonly ITaskService task = App.Services.GetRequiredService<ITaskService>();

        public UIElement CoverControl => CoverContainer;
        public Image CoverImageControl => CoverImage;

        public event RoutedEventHandler? Click;
        private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);

        private void ReleaseCurrentArt()
        {
            if (_currentArtKey.HasValue && _currentArt != null)
            {
                art.ReleaseArtwork(_currentArtKey.Value, _currentArt);
                _currentArtKey = null;
                _currentArt = null;
            }
            DisplayedCoverArt = null;
        }

        public static readonly DependencyProperty PlaylistProperty =
            DependencyProperty.Register(nameof(Playlist), typeof(Playlist), typeof(PlaylistGridItem), new PropertyMetadata(null, OnPlaylistChanged));

        public Playlist Playlist
        {
            get => (Playlist)GetValue(PlaylistProperty);
            set => SetValue(PlaylistProperty, value);
        }

        private static void OnPlaylistChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PlaylistGridItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        public PlaylistGridItem()
        {
            InitializeComponent();
            Unloaded += (s, e) =>
            {
                _updateCount++;
                _artworkCts?.Cancel();
                _artworkCts?.Dispose();
                _artworkCts = null;
                ReleaseCurrentArt();
            };
        }

        private async void UpdateArtworkAsync()
        {
            int currentUpdate = ++_updateCount;
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = new CancellationTokenSource();
            var token = _artworkCts.Token;

            var currentPlaylist = Playlist;
            if (currentPlaylist == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }

            var cached = art.GetCachedArtwork(currentPlaylist.Id, 174);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentPlaylist.Id, 174);
                _currentArt = cached;
                art.AcquireArtwork(_currentArtKey.Value, cached);
                DisplayedCoverArt = cached;
                return;
            }

            ReleaseCurrentArt();
            DisplayedCoverArt = null;

            try
            {
                await Task.Delay(150);

                if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                var artImage = await art.GetArtwork(currentPlaylist, 174, token);

                if (token.IsCancellationRequested) return;

                task.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                    if (Playlist == currentPlaylist)
                    {
                        ReleaseCurrentArt();
                        if (artImage != null)
                        {
                            _currentArtKey = new ArtKey(currentPlaylist.Id, 174);
                            _currentArt = artImage;
                            art.AcquireArtwork(_currentArtKey.Value, artImage);
                        }
                        DisplayedCoverArt = artImage;
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }
    }
}
