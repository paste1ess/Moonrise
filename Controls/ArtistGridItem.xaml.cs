using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
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
    public sealed partial class ArtistGridItem : UserControl
    {
        private int _updateCount = 0;
        private ArtKey? _currentArtKey;
        private ImageSource? _currentArt;
        private CancellationTokenSource? _artworkCts;
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

        public static readonly DependencyProperty ArtistProperty =
            DependencyProperty.Register(nameof(Artist), typeof(Artist), typeof(ArtistGridItem), new PropertyMetadata(null, OnArtistChanged));

        public Artist Artist
        {
            get => (Artist)GetValue(ArtistProperty);
            set => SetValue(ArtistProperty, value);
        }

        private static void OnArtistChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArtistGridItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        [ObservableProperty]
        public partial ImageSource? DisplayedCoverArt { get; set; }

        public ArtistGridItem()
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

            var currentArtist = Artist;
            if (currentArtist == null)
            {
                ReleaseCurrentArt();
                DisplayedCoverArt = null;
                return;
            }

            var cached = art.GetCachedArtwork(currentArtist.Id, 174);
            if (cached != null)
            {
                ReleaseCurrentArt();
                _currentArtKey = new ArtKey(currentArtist.Id, 174);
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

                var artImage = await art.GetArtwork(currentArtist, 174, token);

                if (token.IsCancellationRequested) return;

                task.Dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || _updateCount != currentUpdate) return;

                    if (Artist == currentArtist)
                    {
                        ReleaseCurrentArt();
                        if (artImage != null)
                        {
                            _currentArtKey = new ArtKey(currentArtist.Id, 174);
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
