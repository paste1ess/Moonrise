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

        public LyricsPanel()
        {
            InitializeComponent();

            if (SyncedLyricsPanel.CheckHasSyncedLyrics(PlaybackService.Instance.CurrentTrack, library))
            {
                DropDown.Content = "Synced";
                ContentFrame.Navigate(typeof(SyncedLyricsPanel));
            }
            else
            {
                DropDown.Content = "Static";
                ContentFrame.Navigate(typeof(StaticLyricsPanel));
            }
        }

        private void SelectLyricsType(string type)
        {
            Type pageType = type switch
            {
                "Static" => typeof(StaticLyricsPanel),
                "Synced" => typeof(SyncedLyricsPanel),
                _ => typeof(SyncedLyricsPanel)
            };

            DropDown.Content = type ?? "Static";

            ContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromBottom });
        }

        private void Static_Click(object sender, RoutedEventArgs e)
        {
            SelectLyricsType("Static");
        }

        private void Synced_Click(object sender, RoutedEventArgs e)
        {
            SelectLyricsType("Synced");
        }
    }
}
