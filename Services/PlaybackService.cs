using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.Graphics.Imaging;
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
    public enum RepeatState
    {
        Off,
        RepeatAll,
        RepeatOne
    }
    public partial class PlaybackService : ObservableObject
    {
        public static readonly PlaybackService Instance = new();
        private ITaskService task => App.Services.GetRequiredService<ITaskService>();
        private IDiscordRpcService rpc => App.Services.GetRequiredService<IDiscordRpcService>();
        private IArtService art => App.Services.GetRequiredService<IArtService>();
        private ILibraryService library => App.Services.GetRequiredService<ILibraryService>();
        public readonly QueueService Queue = new();

        [ObservableProperty]
        public partial PlaybackState CurrentPlaybackState { get; set; }
        [ObservableProperty]
        public partial bool ShuffleState { get; set; }
        [ObservableProperty]
        public partial RepeatState RepeatState { get; set; }
        [ObservableProperty]
        public partial Track? CurrentTrack { get; set; }
        [ObservableProperty]
        public partial ImageSource? CurrentTrackArtwork { get; set; }
        [ObservableProperty]
        public partial SoftwareBitmap? CurrentTrackBackgroundBitmap { get; set; }
        


        private CancellationTokenSource? _artworkCts;
        private SoftwareBitmap? _previousBackgroundBitmap;

        partial void OnCurrentTrackBackgroundBitmapChanging(SoftwareBitmap? value)
        {
            _previousBackgroundBitmap = CurrentTrackBackgroundBitmap;
        }

        partial void OnCurrentTrackBackgroundBitmapChanged(SoftwareBitmap? value)
        {
            if (_previousBackgroundBitmap != value)
            {
                _previousBackgroundBitmap?.Dispose();
            }
            _previousBackgroundBitmap = null;
        }

        partial void OnCurrentTrackChanged(Track? value)
        {
            if (_artworkCts != null)
            {
                _artworkCts.Cancel();
                _artworkCts.Dispose();
                _artworkCts = null;
            }
            if (value == null)
            {
                CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                CurrentTrackBackgroundBitmap = null;

                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.MusicProperties.Title = "No track currently playing.";
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.MusicProperties.Artist = "";
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Thumbnail = null;
                mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Update();

                rpc.ClearPresence();

                return;
            }

            rpc.SetPresence(value.Title, value.Artist);

            _artworkCts = new CancellationTokenSource();
            _ = loadArtworkAsync(value, _artworkCts.Token);
        }

        public TimeSpan CurrentTrackTime => mediaPlayer.PlaybackSession.Position;

        private readonly DispatcherTimer _positionTimer = new();

        private MediaPlayer mediaPlayer;

        PlaybackService()
        {
            CurrentPlaybackState = PlaybackState.None;
            mediaPlayer = new();

            _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
            _positionTimer.Tick += (_, _) => { 
                OnPropertyChanged(nameof(CurrentTrackTime));
                //if (CurrentTrack != null) rpc.SetPresence(CurrentTrack.Title, CurrentTrack.Artist);
            };

            CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
            CurrentTrackBackgroundBitmap = null;

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
                if (RepeatState == RepeatState.RepeatOne)
                {
                    var repeatTcs = new TaskCompletionSource();
                    task.Dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                            _ = playBase();
                        }
                        finally
                        {
                            repeatTcs.SetResult();
                        }
                    });
                    await repeatTcs.Task;
                    return;
                }

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
                if (queueTrack == null)
                {
                    if (RepeatState == RepeatState.RepeatAll)
                    {
                        var repeatTcs = new TaskCompletionSource();
                        task.Dispatcher.TryEnqueue(() =>
                        {
                            try
                            {
                                if (Queue.OriginalQueue.Count > 0)
                                {
                                    if (ShuffleState)
                                        Queue.ShuffleQueue();
                                    else
                                        Queue.PassQueue();
                                    queueTrack = Queue.TakeFromStart();
                                }
                            }
                            finally
                            {
                                repeatTcs.SetResult();
                            }
                        });
                        await repeatTcs.Task;
                    }
                    if (queueTrack == null) return;
                }

                var nextTrack = await library.GetTrack(queueTrack.Id);
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

                var prevTrack = await library.GetTrack(queueTrack.Id);
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
            if (CurrentTrack != null) rpc.SetPresence(CurrentTrack.Title, CurrentTrack.Artist);
            task.Dispatcher.TryEnqueue(() => {
                CurrentPlaybackState = PlaybackState.Playing;
                _positionTimer.Start();
            });
        }

        private async Task pauseBase()
        {
            mediaPlayer.Pause();
            rpc.ClearPresence();
            task.Dispatcher.TryEnqueue(() => {
                CurrentPlaybackState = PlaybackState.Paused;
                _positionTimer.Stop();
            });
        }

        private async Task stopBase()
        {
            mediaPlayer.Pause();
            rpc.ClearPresence();
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
        public void ToggleShuffle()
        {
            var oldShuffleState = ShuffleState;
            var command = new RelayAppCommand(async (token) =>
            {
                if (CurrentTrack == null) return;
                var queueTrack = QueueTrack.FromTrack(CurrentTrack);
                var targetShuffleState = !ShuffleState;
                BulkObservableCollection<QueueTrack>? newCollection = null;

                if (targetShuffleState)
                {
                    var newQueueList = Queue.GetShuffledList(queueTrack);
                    newCollection = new BulkObservableCollection<QueueTrack>();
                    if (newQueueList != null) newCollection.ReplaceRange(newQueueList);
                }
                else
                {
                    newCollection = new BulkObservableCollection<QueueTrack>();
                    var index = Queue.OriginalQueue.FindIndex(q => q.Id == queueTrack.Id);
                    if (index >= 0)
                    {
                        var remaining = Queue.OriginalQueue.GetRange(index + 1, Queue.OriginalQueue.Count - index - 1);
                        newCollection.ReplaceRange(remaining);
                    }
                    else
                    {
                        newCollection.ReplaceRange(Queue.OriginalQueue);
                    }
                }

                task.Dispatcher.TryEnqueue(() =>
                {
                    ShuffleState = targetShuffleState;
                    if (newCollection != null) Queue.ActiveQueue = newCollection;
                });
            }, async (ex) => ShuffleState = oldShuffleState);

            task.Enqueue(command);
        }
        public void Scrub(TimeSpan position)
        {
            mediaPlayer.PlaybackSession.Position = position;
            OnPropertyChanged(nameof(CurrentTrackTime));
        }

        public void ResetForLibraryChange()
        {
            mediaPlayer.Pause();

            task.Dispatcher.TryEnqueue(() =>
            {
                _positionTimer.Stop();
                CurrentPlaybackState = PlaybackState.Stopped;
                CurrentTrack = null;
                Queue.ClearAll();
            });
        }

        public void PlayTrack(Track track)
        {
            var command = new RelayAppCommand(async (token) =>
            {
                await stopBase();

                var sourceTcs = new TaskCompletionSource();
                task.Dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        if (CurrentTrack != null)
                            Queue.AddToHistory(QueueTrack.FromTrack(CurrentTrack));
                        CurrentTrack = track;
                        if (mediaPlayer.Source is IDisposable oldSource)
                        {
                            mediaPlayer.Source = null;
                            oldSource.Dispose();
                        }
                        mediaPlayer.Source = CreatePlaybackItem(track);
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

        private async Task loadArtworkAsync(Track track, CancellationToken token)
        {
            task.Dispatcher.TryEnqueue(() =>
            {
                CurrentTrackArtwork = new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                CurrentTrackBackgroundBitmap = null;
            });
            try
            {
                var artwork = await art.GetArtwork(track, 320, token);
                if (token.IsCancellationRequested) return;

                var bgBitmap = await art.GetArtworkBitmap(track, 8, token);
                if (token.IsCancellationRequested)
                {
                    bgBitmap?.Dispose();
                    return;
                }

                var smtcRef = await art.GetArtworkStreamReference(track, token);
                if (token.IsCancellationRequested)
                {
                    bgBitmap?.Dispose();
                    return;
                }

                task.Dispatcher.TryEnqueue(() =>
                {
                    if (CurrentTrack == track)
                    {
                        CurrentTrackArtwork = artwork ?? new BitmapImage(new Uri("ms-appx:///Assets/Placeholder.png"));
                        CurrentTrackBackgroundBitmap = bgBitmap;

                        if (mediaPlayer.Source is MediaPlaybackItem playbackItem)
                        {
                            var props = playbackItem.GetDisplayProperties();
                            props.Thumbnail = smtcRef ?? RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/Placeholder.png"));
                            playbackItem.ApplyDisplayProperties(props);
                        }
                    }
                    else
                    {
                        bgBitmap?.Dispose();
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
            var path = library.PathToAbsolute(track.FilePath);
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
