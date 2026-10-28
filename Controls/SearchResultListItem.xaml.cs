using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class SearchResultListItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;
        private readonly IArtService art = App.Services.GetRequiredService<IArtService>();
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly ITaskService task = App.Services.GetRequiredService<ITaskService>();

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

        public SearchResult Item
        {
            get => (SearchResult)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(SearchResult), typeof(SearchResultListItem), new PropertyMetadata(null, OnItemChanged));

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SearchResultListItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        public SearchResultListItem()
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

            var currentItem = Item;
            if (currentItem == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }

            var cached = art.GetCachedArtwork(currentItem.Id, 40);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentItem.Id, 40);
                _currentArt = cached;
                art.AcquireArtwork(_currentArtKey.Value, cached);
                DisplayedCoverArt = cached;
                return;
            }

            ReleaseCurrentArt();
            DisplayedCoverArt = null;

            try
            {
                await Task.Delay(75, token);

                if (_updateCount != currentUpdate) return;

                var artImage = await Task.Run(async () =>
                {
                    if (token.IsCancellationRequested) return null;

                    return currentItem.ResultType switch
                    {
                        ResultType.Track => await library.GetTrack(currentItem.Id) is Track track
                            ? await art.GetArtwork(track, 40, token)
                            : null,
                        ResultType.Album => await library.GetAlbum(currentItem.Id) is Album album
                            ? await art.GetArtwork(album, 40, token)
                            : null,
                        ResultType.Artist => await library.GetArtist(currentItem.Id) is Artist artist
                            ? await art.GetArtwork(artist, 40, token)
                            : null,
                        ResultType.Playlist => await library.GetPlaylist(currentItem.Id) is Playlist playlist
                            ? await art.GetArtwork(playlist, 40, token)
                            : null,
                        _ => null
                    };
                }, token);

                if (token.IsCancellationRequested) return;

                task.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                    if (Item == currentItem)
                    {
                        ReleaseCurrentArt();
                        if (artImage != null)
                        {
                            _currentArtKey = new ArtKey(currentItem.Id, 40);
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

        public Visibility GetCaptionVisibility(string? caption) =>
            string.IsNullOrWhiteSpace(caption) ? Visibility.Collapsed : Visibility.Visible;

        public CornerRadius GetRadius(ResultType res) => res == ResultType.Artist ? new(40) : new(4);
    }
}
