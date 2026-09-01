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

        private async void EditLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                await ShowEditLyricsDialog(currentTrack);
            }
            catch (Exception ex)
            {
                toast.Show("Error", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task ShowEditLyricsDialog(Track track)
        {
            bool isInitiallySynced = (DropDown.Content as string) == "Synced" || ContentFrame.CurrentSourcePageType == typeof(SyncedLyricsPanel);

            var saveToDiskCheckBox = new CheckBox
            {
                Content = "Save to disk",
                IsChecked = false
            };

            var syncedLyricsCheckBox = new CheckBox
            {
                Content = "Synced lyrics",
                IsChecked = isInitiallySynced
            };

            var descriptionText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            void UpdateDescription()
            {
                bool isSave = saveToDiskCheckBox.IsChecked == true;
                bool isSynced = syncedLyricsCheckBox.IsChecked == true;

                if (isSynced)
                {
                    descriptionText.Text = isSave
                        ? "How would you like to edit these synced lyrics?\nThis will modify the .lrc file on your system."
                        : "How would you like to edit these synced lyrics?\nChanges will only apply temporarily for this session.";
                }
                else
                {
                    descriptionText.Text = isSave
                        ? "How would you like to edit these static lyrics?\nThis will modify embedded tags in the audio file."
                        : "How would you like to edit these static lyrics?\nChanges will only apply temporarily for this session.";
                }
            }

            saveToDiskCheckBox.Checked += (s, e) => UpdateDescription();
            saveToDiskCheckBox.Unchecked += (s, e) => UpdateDescription();
            syncedLyricsCheckBox.Checked += (s, e) => UpdateDescription();
            syncedLyricsCheckBox.Unchecked += (s, e) => UpdateDescription();
            UpdateDescription();

            var checkBoxesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    saveToDiskCheckBox,
                    syncedLyricsCheckBox
                }
            };

            var contentPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    descriptionText,
                    checkBoxesPanel
                }
            };

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            var dialog = new ContentDialog
            {
                Title = "Edit",
                Content = contentPanel,
                PrimaryButtonText = "Online",
                SecondaryButtonText = "Clipboard",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };

            var result = await dialog.ShowAsync();
            bool saveToDisk = saveToDiskCheckBox.IsChecked == true;
            bool targetSynced = syncedLyricsCheckBox.IsChecked == true;

            if (result == ContentDialogResult.Primary)
            {
                using var toastHandle = toast.ShowProgress(string.Empty, $"Searching lyrics for {track.Title}...", isIndeterminate: true, isClosable: true);

                WebLyricResult? webResult = null;
                Exception? networkError = null;
                try
                {
                    webResult = await webLyrics.FetchLyricsAsync(track.Title, track.Artist, track.Album, track.Duration);
                }
                catch (Exception ex)
                {
                    networkError = ex;
                }

                toastHandle.Dismiss();

                if (networkError != null)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Network Error",
                        Content = $"Failed to connect to the online lyrics service:\n{networkError.Message}",
                        CloseButtonText = "OK",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = root
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                if (webResult == null || (string.IsNullOrWhiteSpace(targetSynced ? webResult.SyncedLyrics : webResult.PlainLyrics) && !webResult.IsInstrumental))
                {
                    var notFoundDialog = new ContentDialog
                    {
                        Title = "Not Found",
                        Content = $"No lyrics could be found online for \"{track.Title}\" by {track.Artist}.",
                        CloseButtonText = "OK",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = root
                    };
                    await notFoundDialog.ShowAsync();
                    return;
                }

                if (webResult.IsInstrumental)
                {
                    var promptText = saveToDisk
                        ? $"\"{track.Title}\" was identified as an instrumental track.\nWould you like to mark it as instrumental and save to file?"
                        : $"\"{track.Title}\" was identified as an instrumental track.\nWould you like to mark it as instrumental for this session?";

                    var instrumentalDialog = new ContentDialog
                    {
                        Title = "Instrumental Track",
                        Content = promptText,
                        PrimaryButtonText = "Yes, mark as instrumental",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = root
                    };

                    var instrumentalResult = await instrumentalDialog.ShowAsync();
                    if (instrumentalResult == ContentDialogResult.Primary)
                    {
                        try
                        {
                            await library.SetLyrics(track.Id, "[INSTRUMENTAL]", isSynced: false, saveToDisk: saveToDisk);
                            SelectLyricsType("Static", true);
                            var message = saveToDisk ? $"{track.Title} marked as instrumental" : "Marked as instrumental for this session";
                            toast.Show(string.Empty, message, InfoBarSeverity.Informational);
                        }
                        catch (Exception ex)
                        {
                            toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                        }
                    }
                    return;
                }

                var candidateLyrics = targetSynced ? webResult.SyncedLyrics : webResult.PlainLyrics;
                if (!string.IsNullOrWhiteSpace(candidateLyrics))
                {
                    await ShowConfirmLyricsDialog(track, candidateLyrics, targetSynced, saveToDisk);
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                try
                {
                    var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (dataPackageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    {
                        var text = await dataPackageView.GetTextAsync();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            await library.SetLyrics(track.Id, text.Trim(), isSynced: targetSynced, saveToDisk: saveToDisk);
                            SelectLyricsType(targetSynced ? "Synced" : "Static", true);
                            var message = saveToDisk ? $"Saved lyrics for {track.Title}" : "Using lyrics for this session";
                            toast.Show(string.Empty, message, InfoBarSeverity.Success);
                        }
                        else
                        {
                            toast.Show(string.Empty, "Clipboard contains no text", InfoBarSeverity.Warning);
                        }
                    }
                    else
                    {
                        toast.Show(string.Empty, "Clipboard contains no text", InfoBarSeverity.Warning);
                    }
                }
                catch (Exception ex)
                {
                    toast.Show("Clipboard error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        private async Task ShowConfirmLyricsDialog(Track track, string lyrics, bool isSynced, bool saveToDisk)
        {
            var previewTextBlock = new TextBlock
            {
                Text = lyrics,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            var scroller = new ScrollViewer
            {
                MaxHeight = 260,
                Content = previewTextBlock
            };

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = "Are these right?",
                Content = scroller,
                PrimaryButtonText = "Yes, use these",
                CloseButtonText = "No, these are wrong",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult == ContentDialogResult.Primary)
            {
                try
                {
                    await library.SetLyrics(track.Id, lyrics, isSynced: isSynced, saveToDisk: saveToDisk);
                    SelectLyricsType(isSynced ? "Synced" : "Static", true);
                    var message = saveToDisk ? $"Saved lyrics for {track.Title}" : "Using lyrics for this session";
                    toast.Show(string.Empty, message, InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        private async void MarkInstrumental_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            var saveToDiskCheckBox = new CheckBox
            {
                Content = "Apply to disk",
                IsChecked = false
            };

            var messageText = new TextBlock
            {
                Text = $"Are you sure you want to mark \"{currentTrack.Title}\" as instrumental?",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            var contentPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    messageText,
                    saveToDiskCheckBox
                }
            };

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            var dialog = new ContentDialog
            {
                Title = "Mark as Instrumental",
                Content = contentPanel,
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                bool saveToDisk = saveToDiskCheckBox.IsChecked == true;
                try
                {
                    await library.SetLyrics(currentTrack.Id, "[INSTRUMENTAL]", isSynced: false, saveToDisk: saveToDisk);
                    SelectLyricsType("Static", true);
                    var message = saveToDisk ? $"{currentTrack.Title} marked as instrumental" : "Marked as instrumental for this session";
                    toast.Show(string.Empty, message, InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        private async void ClearLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playback.CurrentTrack;
            if (currentTrack == null)
            {
                toast.Show(string.Empty, "No track currently playing", InfoBarSeverity.Warning);
                return;
            }

            bool isSynced = (DropDown.Content as string) == "Synced" || ContentFrame.CurrentSourcePageType == typeof(SyncedLyricsPanel);

            var messageText = new TextBlock
            {
                Text = isSynced
                    ? "These lyrics will be permanently deleted.\nThis will delete the associated .lrc file from your system."
                    : "These lyrics will be permanently deleted.\nThis will remove the embedded lyrics from the file.",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            var root = this.XamlRoot ?? ContentFrame.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            var dialog = new ContentDialog
            {
                Title = "Are you sure?",
                Content = messageText,
                PrimaryButtonText = "Yes, delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    await library.ClearLyrics(currentTrack.Id, isSynced: isSynced, saveToDisk: true);
                    toast.Show(string.Empty, $"Deleted lyrics for {currentTrack.Title}", InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }
    }
}

