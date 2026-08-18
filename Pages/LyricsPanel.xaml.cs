using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Moonrise.Services;
using System;

namespace Moonrise.Pages
{
    public sealed partial class LyricsPanel : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly PlaybackService playback = PlaybackService.Instance;

        public LyricsPanel()
        {
            InitializeComponent();
            UpdateLyricsSelection(false);

            playback.PropertyChanged += Playback_PropertyChanged;
            Unloaded += (s, e) => playback.PropertyChanged -= Playback_PropertyChanged;
        }

        private void Playback_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackService.CurrentTrack))
            {
                DispatcherQueue.TryEnqueue(() => UpdateLyricsSelection(true));
            }
        }

        private void UpdateLyricsSelection(bool animate)
        {
            if (SyncedLyricsPanel.CheckIsInstrumental(playback.CurrentTrack, library))
            {
                SelectLyricsType("Static", animate);
            }
            else if (SyncedLyricsPanel.CheckHasSyncedLyrics(playback.CurrentTrack, library))
            {
                SelectLyricsType("Synced", animate);
            }
            else
            {
                SelectLyricsType("Static", animate);
            }
        }

        private void SelectLyricsType(string type, bool animate = true)
        {
            Type pageType = type switch
            {
                "Static" => typeof(StaticLyricsPanel),
                "Synced" => typeof(SyncedLyricsPanel),
                _ => typeof(SyncedLyricsPanel)
            };

            DropDown.Content = type ?? "Static";

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                if (animate)
                {
                    ContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromBottom });
                }
                else
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }

        private void Static_Click(object sender, RoutedEventArgs e)
        {
            SelectLyricsType("Static", true);
        }

        private void Synced_Click(object sender, RoutedEventArgs e)
        {
            SelectLyricsType("Synced", true);
        }
    }
}
