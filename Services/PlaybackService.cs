using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Moonrise.Services
{
    public enum PlaybackState
    {
        Playing,
        Paused,
        Stopped,
        None,
        Error
    }
    public partial class PlaybackService : ObservableObject
    {
        public static readonly PlaybackService Instance = new();
        private TaskService task = TaskService.Instance;

        [ObservableProperty]
        public partial PlaybackState CurrentPlaybackState { get; set; }
        [ObservableProperty]
        public partial Track CurrentTrack { get; set; }
        [ObservableProperty]
        public partial BitmapImage? CurrentTrackArtwork { get; set; }
        partial void OnCurrentTrackChanged(Track value)
        {
            if (value == null)
            {
                CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                return;
            }
            _ = loadArtworkAsync(value);
        }

        private TimeSpan _currentTrackTime;
        public TimeSpan CurrentTrackTime
        {
            get => mediaPlayer.PlaybackSession.Position;
            set => SetProperty(ref _currentTrackTime, value);
        }



        private readonly DispatcherTimer _positionTimer = new();

        private MediaPlayer mediaPlayer;

        PlaybackService()
        {
            CurrentPlaybackState = PlaybackState.None;
            mediaPlayer = new();

            _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
            _positionTimer.Tick += (_, _) => OnPropertyChanged(nameof(CurrentTrackTime));

            CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
        }

        public void Play()
        {
            var command = new RelayAppCommand(async (token) =>
            {
                mediaPlayer.Play();
                task.Dispatcher.TryEnqueue(() => { 
                    CurrentPlaybackState = PlaybackState.Playing;
                    _positionTimer.Start();
                });
                
            }, async (ex) =>
            {
                task.Dispatcher.TryEnqueue(() => {
                    CurrentPlaybackState = PlaybackState.Error;
                    _positionTimer.Stop();
                });
            });

            task.Enqueue(command);
        }
        public void Pause()
        {
            var command = new RelayAppCommand(async (token) =>
            {
                mediaPlayer.Pause();
                task.Dispatcher.TryEnqueue(() => {
                    CurrentPlaybackState = PlaybackState.Paused;
                    _positionTimer.Stop();
                });
            }, async (ex) =>
            {
                task.Dispatcher.TryEnqueue(() => {
                    CurrentPlaybackState = PlaybackState.Error;
                    _positionTimer.Stop();
                });
            });

            task.Enqueue(command);
        }
        public void Scrub(TimeSpan position)
        {
            mediaPlayer.PlaybackSession.Position = position;
            OnPropertyChanged(nameof(CurrentTrackTime));
        }

        public void PlayTrack(Track track)
        {
            Pause();

            CurrentTrack = track;

            mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;

            if (mediaPlayer.Source is IDisposable oldSource)
            {
                mediaPlayer.Source = null;
                oldSource.Dispose();
            }

            IMediaPlaybackSource mediaSource = MediaSource.CreateFromUri(new Uri(track.FilePath));
            mediaPlayer.Source = mediaSource;

            Play();
        }

        private async Task loadArtworkAsync(Track track)
        {
            CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
            var art = await ArtService.Instance.GetArtwork(track, 320);
            if (CurrentTrack == track)
            {
                CurrentTrackArtwork = art ?? new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
            }
        }
    }
}
