using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;

namespace Moonrise.Pages
{
    public sealed partial class PlayerPage : Page
    {
        private readonly IPlaybackService playbackService = App.Services.GetRequiredService<IPlaybackService>();
        private readonly ILibraryService libraryService = App.Services.GetRequiredService<ILibraryService>();
        public PlayerPage()
        {
            InitializeComponent();
            //Unloaded += (s, e) => this.Bindings.StopTracking();
            Loaded += (s, e) =>
            {
                playbackService.PropertyChanged += PlaybackService_PropertyChanged;
                UpdateFavoriteButtonVisuals();
            };
            Unloaded += (s, e) =>
            {
                playbackService.PropertyChanged -= PlaybackService_PropertyChanged;
            };
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

        public string PlaybackStateToGlyph(PlaybackState state)
    => state == PlaybackState.Playing ? "\uE769" : "\uE768";
        public string RepeatStateToGlyph(RepeatState state) => state == RepeatState.RepeatOne ? "\uE8ED" : state == RepeatState.RepeatAll ? "\uE8EE" : "\uF5E7";
        public bool RepeatStateToActive(RepeatState state) => state != RepeatState.Off;

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (playbackService.CurrentPlaybackState == PlaybackState.Paused)
                playbackService.Play();
            else if (playbackService.CurrentPlaybackState == PlaybackState.Playing)
                playbackService.Pause();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            playbackService.Next();
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            playbackService.Back();
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            playbackService.ToggleShuffle();
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            if (playbackService.RepeatState == RepeatState.Off)
            {
                playbackService.RepeatState = RepeatState.RepeatAll;
            } else if (playbackService.RepeatState == RepeatState.RepeatAll)
            {
                playbackService.RepeatState = RepeatState.RepeatOne;
            } else
            {
                playbackService.RepeatState = RepeatState.Off;
            }
        }

        private void PlaybackService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPlaybackService.CurrentTrack))
            {
                UpdateFavoriteButtonVisuals();
            }
        }

        private void UpdateFavoriteButtonVisuals()
        {
            bool isFav = playbackService.CurrentTrack?.IsFavorite ?? false;
            FavoriteButton.Glyph = isFav ? "\uEB52" : "\uEB51";
            FavoriteButton.Active = isFav;
        }

        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            var track = playbackService.CurrentTrack;
            if (track == null) return;

            track.IsFavorite = !track.IsFavorite;
            UpdateFavoriteButtonVisuals();

            await libraryService.SetTrackFavorite(track.Id, track.IsFavorite);
        }
    }
}
