using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    public sealed partial class LyricsPanel : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();
        private readonly IWebLyricService webLyrics = App.Services.GetRequiredService<IWebLyricService>();
        private readonly IToastService toast = App.Services.GetRequiredService<IToastService>();

        public LyricsPanel()
        {
            InitializeComponent();
            UpdateLyricsSelection(false);

            playback.PropertyChanged += Playback_PropertyChanged;
            Unloaded += (s, e) => playback.PropertyChanged -= Playback_PropertyChanged;
        }

        private void Playback_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPlaybackService.CurrentTrack))
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
                    ContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo());
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

        private async void EditLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            bool isInitiallySynced = (DropDown.Content as string) == "Synced" || ContentFrame.CurrentSourcePageType == typeof(SyncedLyricsPanel);
            await LyricsDialogHelper.ShowEditLyricsDialogAsync(currentTrack, isInitiallySynced, root, library, webLyrics, toast, type => SelectLyricsType(type, true));
        }

        private async void MarkInstrumental_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            await LyricsDialogHelper.ShowMarkInstrumentalDialogAsync(currentTrack, root, library, toast, type => SelectLyricsType(type, true));
        }

        private async void ClearLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            bool isSynced = (DropDown.Content as string) == "Synced" || ContentFrame.CurrentSourcePageType == typeof(SyncedLyricsPanel);
            await LyricsDialogHelper.ShowClearLyricsDialogAsync(currentTrack, isSynced, root, library, toast);
        }
    }
}

