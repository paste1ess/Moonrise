using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class AlbumGridItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;

        public event RoutedEventHandler? Click;
        private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);

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

        public static readonly DependencyProperty AlbumProperty =
            DependencyProperty.Register(nameof(Album), typeof(Album), typeof(AlbumGridItem), new PropertyMetadata(null, OnAlbumChanged));

        public Album Album
        {
            get => (Album)GetValue(AlbumProperty);
            set => SetValue(AlbumProperty, value);
        }

        private static void OnAlbumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AlbumGridItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        public AlbumGridItem()
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

            var currentAlbum = Album;
            if (currentAlbum == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }

            var cached = ArtService.Instance.GetCachedArtwork(currentAlbum.Id, 174);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentAlbum.Id, 174);
                _currentArt = cached;
                ArtService.Instance.AcquireArtwork(_currentArtKey.Value, cached);
                DisplayedCoverArt = cached;
                return;
            }

            ReleaseCurrentArt();
            DisplayedCoverArt = null;

            try
            {
                await Task.Delay(150);

                if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                var art = await ArtService.Instance.GetArtwork(currentAlbum, 174, token);

                if (token.IsCancellationRequested) return;

                TaskService.Instance.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                    if (Album == currentAlbum)
                    {
                        ReleaseCurrentArt();
                        if (art != null)
                        {
                            _currentArtKey = new ArtKey(currentAlbum.Id, 174);
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
    }
}
