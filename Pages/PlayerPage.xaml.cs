using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PlayerPage : Page
    {
        private PlaybackService playbackService = PlaybackService.Instance;
        private Track exampleTrack;
        private Track exampleTrack2;
        private Track exampleTrack3;
        private Track exampleTrack4;
        public PlayerPage()
        {
            InitializeComponent();
            Unloaded += (s, e) => this.Bindings.StopTracking();
        }
        private int previousSelectedIndex;

        private void SecondPanelSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            SelectorBarItem selectedItem = sender.SelectedItem;
            int currentSelectedIndex = sender.Items.IndexOf(selectedItem);
            System.Type pageType;

            switch (currentSelectedIndex)
            {
                case 0:
                    pageType = typeof(QueuePanel);
                    break;
                case 1:
                    pageType = typeof(HistoryPanel);
                    break;
                default:
                    pageType = typeof(LyricsPanel);
                    break;
            }

            var slideNavigationTransitionEffect = currentSelectedIndex - previousSelectedIndex > 0
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;

            ContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo() { Effect = slideNavigationTransitionEffect });

            previousSelectedIndex = currentSelectedIndex;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (exampleTrack != null) return;

            var allTracks = await Task.Run(() => LibraryService.Instance.GetAllTracks());
            if (allTracks.Count > 0) exampleTrack = allTracks[0];
            if (allTracks.Count > 1) exampleTrack2 = allTracks[1];
            if (allTracks.Count > 2) exampleTrack3 = allTracks[2];
            if (allTracks.Count > 3) exampleTrack4 = allTracks[3];
        }

        public string PlaybackStateToGlyph(PlaybackState state)
    => state == PlaybackState.Playing ? "\uE769" : "\uE768";
        public string FormatDuration(TimeSpan duration)
    => $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";

        private bool _isUserDragging = false;

        private void ProgressSlider_Loaded(object sender, RoutedEventArgs e)
        {
            ProgressSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
        }

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (playbackService.CurrentTrack == null) return;
            _isUserDragging = true;
            playbackService.Pause();
        }

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_isUserDragging) return;
            if (playbackService.CurrentTrack == null)
            {
                _isUserDragging = false;
                return;
            }
            playbackService.Scrub(TimeSpan.FromSeconds(e.NewValue));
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isUserDragging) return;
            _isUserDragging = false;

            if (playbackService.CurrentTrack == null) return;
            playbackService.Scrub(TimeSpan.FromSeconds(ProgressSlider.Value));
            playbackService.Play();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (playbackService.CurrentPlaybackState == PlaybackState.Paused)
                playbackService.Play();
            else if (playbackService.CurrentPlaybackState == PlaybackState.Playing)
                playbackService.Pause();
            else
            { 
                playbackService.PlayTrack(exampleTrack);
                playbackService.Queue.AddToStart(exampleTrack2);
                playbackService.Queue.AddToStart(exampleTrack3);
                playbackService.Queue.AddToStart(exampleTrack4);
                playbackService.Queue.AddToStart(exampleTrack2);
                playbackService.Queue.AddToStart(exampleTrack3);
                playbackService.Queue.AddToStart(exampleTrack4);
                playbackService.Queue.AddToStart(exampleTrack2);
                playbackService.Queue.AddToStart(exampleTrack3);
                playbackService.Queue.AddToStart(exampleTrack4);
                playbackService.Queue.AddToStart(exampleTrack2);
                playbackService.Queue.AddToStart(exampleTrack3);
                playbackService.Queue.AddToStart(exampleTrack4);
                playbackService.Queue.AddToStart(exampleTrack2);
                playbackService.Queue.AddToStart(exampleTrack3);
                playbackService.Queue.AddToStart(exampleTrack4);
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            playbackService.Next();
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            playbackService.Back();
        }
    }
}
