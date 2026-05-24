using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

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
        private TaskService task => TaskService.Instance;
        public readonly QueueService Queue = new();

        [ObservableProperty]
        public partial PlaybackState CurrentPlaybackState { get; set; }
        [ObservableProperty]
        public partial Track? CurrentTrack { get; set; }
        [ObservableProperty]
        public partial ImageSource? CurrentTrackArtwork { get; set; }
        [ObservableProperty]
        public partial ImageSource? CurrentTrackBackgroundArtwork { get; set; }
        


        private CancellationTokenSource? _artworkCts;

        partial void OnCurrentTrackChanged(Track? value)
        {
            if (_artworkCts != null)
            {
                _artworkCts.Cancel();
                _artworkCts.Dispose();
            }
            if (value == null)
            {
                CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                CurrentTrackBackgroundArtwork = null;

                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.MusicProperties.Title = "No track currently playing.";
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.MusicProperties.Artist = "";
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Thumbnail = null;
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Update();

                return;
            }

            _artworkCts = new CancellationTokenSource();
            _ = loadArtworkAsync(value, _artworkCts.Token);
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
            CurrentTrackBackgroundArtwork = null;

            var alwaysEnabled = Enum.ToObject(
                mediaPlayer.CommandManager.NextBehavior.EnablingRule.GetType(), 1);
            mediaPlayer.CommandManager.NextBehavior.EnablingRule = (dynamic)alwaysEnabled;
            mediaPlayer.CommandManager.PreviousBehavior.EnablingRule = (dynamic)alwaysEnabled;

            var smtc = mediaPlayer.SystemMediaTransportControls;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.ButtonPressed += SmtcButtonPressed;

            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            Next();
        }

        private void SmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Back();
                    break;
            }
        }

        public void Next() 
        {
            var command = new RelayAppCommand(async (token) =>
            {
                await stopBase();

                QueueTrack? queueTrack = null;
                var dequeueTcs = new TaskCompletionSource(); // fyi this is so it can await for it to finish instead of instantly returning and continuing on broken
                task.Dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        queueTrack = Queue.TakeFromStart();
                        if (queueTrack != null && CurrentTrack != null)
                        {
                            Queue.AddToHistory(QueueTrack.FromTrack(CurrentTrack));
                        }
                    }
                    finally
                    {
                        dequeueTcs.SetResult();
                    }
                });
                await dequeueTcs.Task;
                if (queueTrack == null) return;

                var nextTrack = await LibraryService.Instance.GetTrack(queueTrack.Id);
                if (nextTrack == null) return;

                var sourceTcs = new TaskCompletionSource();
                task.Dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        CurrentTrack = nextTrack;
                        if (mediaPlayer.Source is IDisposable oldSource)
                        {
                            mediaPlayer.Source = null;
                            oldSource.Dispose();
                        }

                        mediaPlayer.Source = CreatePlaybackItem(nextTrack);
                    }
                    finally
                    {
                        sourceTcs.SetResult();
                    }
                });
                await sourceTcs.Task;

                await playBase();

            }, async (ex) => await errorActionBase());

            task.Enqueue(command);
        }

        public void Back()
        {
            var command = new RelayAppCommand(async (token) =>
            {

                if (mediaPlayer.PlaybackSession != null && mediaPlayer.Position.TotalSeconds > 3)
                {
                    mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                    return;
                }

                await stopBase();

                QueueTrack? queueTrack = null;
                var dequeueTcs = new TaskCompletionSource();
                task.Dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        queueTrack = Queue.TakeFromHistory();

                        if (queueTrack != null && CurrentTrack != null)
                        {
                            Queue.ActiveQueue.Insert(0, QueueTrack.FromTrack(CurrentTrack));
                        }
                    }
                    finally
                    {
                        dequeueTcs.SetResult();
                    }
                });
                await dequeueTcs.Task;
                if (queueTrack == null) return;

                var prevTrack = await LibraryService.Instance.GetTrack(queueTrack.Id);
                if (prevTrack == null) return;

                var sourceTcs = new TaskCompletionSource();
                task.Dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        CurrentTrack = prevTrack;
                        if (mediaPlayer.Source is IDisposable oldSource)
                        {
                            mediaPlayer.Source = null;
                            oldSource.Dispose();
                        }

                        mediaPlayer.Source = CreatePlaybackItem(prevTrack);
                    }
                    finally
                    {
                        sourceTcs.SetResult();
                    }
                });
                await sourceTcs.Task;

                await playBase();

            }, async (ex) => await errorActionBase());

            task.Enqueue(command);
        }


        private async Task playBase()
        {
            mediaPlayer.Play();
            task.Dispatcher.TryEnqueue(() => {
                CurrentPlaybackState = PlaybackState.Playing;
                _positionTimer.Start();
            });
        }

        private async Task pauseBase()
        {
            mediaPlayer.Pause();
            task.Dispatcher.TryEnqueue(() => {
                CurrentPlaybackState = PlaybackState.Paused;
                _positionTimer.Stop();
            });
        }

        private async Task stopBase()
        {
            mediaPlayer.Pause();
            task.Dispatcher.TryEnqueue(() => {
                if (mediaPlayer.PlaybackSession != null)
                {
                    mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                }
                CurrentPlaybackState = PlaybackState.Stopped;
                _positionTimer.Stop();
            });
        }

        private async Task errorActionBase()
        {
            task.Dispatcher.TryEnqueue(() => {
                CurrentPlaybackState = PlaybackState.Error;
                _positionTimer.Stop();
            });
        }

        public void Play()
        {
            var command = new RelayAppCommand(async (token) => await playBase(), async (ex) => await errorActionBase());

            task.Enqueue(command);
        }
        public void Pause()
        {
            var command = new RelayAppCommand(async (token) => await pauseBase(), async (ex) => await errorActionBase());

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

            mediaPlayer.Source = CreatePlaybackItem(track);

            Play();
        }

        private async Task loadArtworkAsync(Track track, CancellationToken token)
        {
            task.Dispatcher.TryEnqueue(() =>
            {
                CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                CurrentTrackBackgroundArtwork = null;
            });
            try
            {
                var art = await ArtService.Instance.GetArtwork(track, 320, token);
                if (token.IsCancellationRequested) return;

                var bgArt = await ArtService.Instance.GetArtwork(track, 6, token);
                if (token.IsCancellationRequested) return;

                var smtcRef = await ArtService.Instance.GetArtworkStreamReference(track, token);
                if (token.IsCancellationRequested) return;

                task.Dispatcher.TryEnqueue(() =>
                {
                    if (CurrentTrack == track)
                    {
                        CurrentTrackArtwork = art ?? new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                        CurrentTrackBackgroundArtwork = bgArt;

                        if (mediaPlayer.Source is MediaPlaybackItem playbackItem)
                        {
                            var props = playbackItem.GetDisplayProperties();
                            props.Thumbnail = smtcRef ?? RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/Placeholder.png"));
                            playbackItem.ApplyDisplayProperties(props);
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load playback artwork: {ex.Message}");
            }
        }

        private MediaPlaybackItem CreatePlaybackItem(Track track)
        {
            var path = LibraryService.Instance.PathToAbsolute(track.FilePath);
            var mediaSource = MediaSource.CreateFromUri(new Uri(path));
            var playbackItem = new MediaPlaybackItem(mediaSource);
            var props = playbackItem.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = track.Title;
            props.MusicProperties.Artist = track.Artist;
            props.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/Placeholder.png"));
            playbackItem.ApplyDisplayProperties(props);
            return playbackItem;
        }
    }
}
