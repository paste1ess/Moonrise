using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class QueueListItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;

        private void ReleaseCurrentArt()
        {
            if (_currentArtKey.HasValue && _currentArt != null)
            {
                ArtService.Instance.ReleaseArtwork(_currentArtKey.Value, _currentArt);
                _currentArtKey = null;
                _currentArt = null;
            }
            DisplayedCoverArt = null;
        }

        public QueueTrack Item
        {
            get { return (QueueTrack)GetValue(ItemProperty); }
            set { SetValue(ItemProperty, value); }
        }

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(QueueTrack), typeof(QueueListItem), new PropertyMetadata(null, OnItemChanged));
        
        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QueueListItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        public bool DisplayGrip
        {
            get { return (bool)GetValue(DisplayGripProperty); }
            set { SetValue(DisplayGripProperty, value); }
        }

        public static readonly DependencyProperty DisplayGripProperty =
            DependencyProperty.Register(nameof(DisplayGrip), typeof(bool), typeof(QueueListItem), new PropertyMetadata(true));

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        private async void UpdateArtworkAsync()
        {
            int currentUpdate = ++_updateCount;
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = new CancellationTokenSource();
            var token = _artworkCts.Token;
 
            var currentTrack = Item;
            if (currentTrack == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }
 
            var cached = ArtService.Instance.GetCachedArtwork(currentTrack.Id, 40);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentTrack.Id, 40);
                _currentArt = cached;
                ArtService.Instance.AcquireArtwork(_currentArtKey.Value, cached);
                DisplayedCoverArt = cached;
                return;
            }
 
            ReleaseCurrentArt();
            DisplayedCoverArt = null;
 
            try
            {
                await Task.Delay(150, token);
 
                if (_updateCount != currentUpdate) return;
 
                var art = await ArtService.Instance.GetArtwork(currentTrack, 40, token);
 
                if (token.IsCancellationRequested) return;
 
                TaskService.Instance.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;
 
                    if (Item == currentTrack)
                    {
                        ReleaseCurrentArt();
                        if (art != null)
                        {
                            _currentArtKey = new ArtKey(currentTrack.Id, 40);
                            _currentArt = art;
                            ArtService.Instance.AcquireArtwork(_currentArtKey.Value, art);
                        }
                        DisplayedCoverArt = art;
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
 
        public QueueListItem()
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

        private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.Instance.Queue.ActiveQueue.Remove(Item);
        }
    }
}
