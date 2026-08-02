using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class TrackListItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;
        private IArtService art => App.Services.GetRequiredService<IArtService>();

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

        public static readonly DependencyProperty SongProperty =
            DependencyProperty.Register(nameof(Song), typeof(Track), typeof(TrackListItem), new PropertyMetadata(null, OnSongChanged));

        public Track Song
        {
            get => (Track)GetValue(SongProperty);
            set => SetValue(SongProperty, value);
        }

        private static void OnSongChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        public static readonly DependencyProperty TrackViewProperty =
            DependencyProperty.Register(nameof(TrackView), typeof(bool), typeof(TrackListItem),
                new PropertyMetadata(true, (d, _) => ((TrackListItem)d).OnPropertyChanged(nameof(TrackViewVisibility))));

        public bool TrackView
        {
            get => (bool)GetValue(TrackViewProperty);
            set => SetValue(TrackViewProperty, value);
        }

        public Visibility TrackViewVisibility => TrackView ? Visibility.Visible : Visibility.Collapsed;

        public static readonly DependencyProperty ArtViewProperty =
            DependencyProperty.Register(nameof(ArtView), typeof(bool), typeof(TrackListItem),
                new PropertyMetadata(true, (d, _) => ((TrackListItem)d).OnPropertyChanged(nameof(ArtViewVisibility))));

        public bool ArtView
        {
            get => (bool)GetValue(ArtViewProperty);
            set => SetValue(ArtViewProperty, value);
        }

        public Visibility ArtViewVisibility => ArtView ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        public event RoutedEventHandler? Clicked;

        public TrackListItem()
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

            var currentTrack = Song;
            if (currentTrack == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }

            var cached = art.GetCachedArtwork(currentTrack.Id, 40);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentTrack.Id, 40);
                _currentArt = cached;
                art.AcquireArtwork(_currentArtKey.Value, cached);
                DisplayedCoverArt = cached;
                return;
            }

            ReleaseCurrentArt();
            DisplayedCoverArt = null;

            try
            {
                await Task.Delay(150, token);

                if (_updateCount != currentUpdate) return;

                var artImage = await art.GetArtwork(currentTrack, 40, token);

                if (token.IsCancellationRequested) return;

                TaskService.Instance.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                    if (Song == currentTrack)
                    {
                        ReleaseCurrentArt();
                        if (artImage != null)
                        {
                            _currentArtKey = new ArtKey(currentTrack.Id, 40);
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

        public string FormatTrackNumber(int? trackNumber) => trackNumber.HasValue ? trackNumber.Value.ToString() : string.Empty;

        public string FormatDuration(TimeSpan duration)
        {
            return $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Clicked?.Invoke(this, e);
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.Instance.Queue.AddToStart(Song);
        }

        private void PlayLast_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.Instance.Queue.AddToEnd(Song);
        }
    }
}
