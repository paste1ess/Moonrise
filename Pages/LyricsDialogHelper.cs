using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    public static class LyricsDialogHelper
    {
        public static async Task ShowEditLyricsDialogAsync(Track track, bool isInitiallySynced, XamlRoot xamlRoot, ILibraryService library, IWebLyricService webLyrics, IToastService toast, Action<string>? onSelectType = null)
        {
            if (xamlRoot == null) return;

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

            var dialog = new ContentDialog
            {
                Title = "Edit",
                Content = contentPanel,
                PrimaryButtonText = "Online",
                SecondaryButtonText = "Clipboard",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
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
                        XamlRoot = xamlRoot
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
                        XamlRoot = xamlRoot
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
                        XamlRoot = xamlRoot
                    };

                    var instrumentalResult = await instrumentalDialog.ShowAsync();
                    if (instrumentalResult == ContentDialogResult.Primary)
                    {
                        try
                        {
                            await library.SetLyrics(track.Id, "[INSTRUMENTAL]", isSynced: false, saveToDisk: saveToDisk);
                            onSelectType?.Invoke("Static");
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
                    await ShowConfirmLyricsDialogAsync(track, candidateLyrics, targetSynced, saveToDisk, xamlRoot, library, toast, onSelectType);
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
                            onSelectType?.Invoke(targetSynced ? "Synced" : "Static");
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

        public static async Task ShowConfirmLyricsDialogAsync(Track track, string lyrics, bool isSynced, bool saveToDisk, XamlRoot xamlRoot, ILibraryService library, IToastService toast, Action<string>? onSelectType = null)
        {
            if (xamlRoot == null) return;

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

            var confirmDialog = new ContentDialog
            {
                Title = "Are these right?",
                Content = scroller,
                PrimaryButtonText = "Yes, use these",
                CloseButtonText = "No, these are wrong",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult == ContentDialogResult.Primary)
            {
                try
                {
                    await library.SetLyrics(track.Id, lyrics, isSynced: isSynced, saveToDisk: saveToDisk);
                    onSelectType?.Invoke(isSynced ? "Synced" : "Static");
                    var message = saveToDisk ? $"Saved lyrics for {track.Title}" : "Using lyrics for this session";
                    toast.Show(string.Empty, message, InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        public static async Task ShowMarkInstrumentalDialogAsync(Track track, XamlRoot xamlRoot, ILibraryService library, IToastService toast, Action<string>? onSelectType = null)
        {
            if (xamlRoot == null) return;

            var saveToDiskCheckBox = new CheckBox
            {
                Content = "Save to disk",
                IsChecked = false
            };

            var messageText = new TextBlock
            {
                Text = $"Are you sure you want to mark \"{track.Title}\" as instrumental?",
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

            var dialog = new ContentDialog
            {
                Title = "Mark as Instrumental",
                Content = contentPanel,
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                bool saveToDisk = saveToDiskCheckBox.IsChecked == true;
                try
                {
                    await library.SetLyrics(track.Id, "[INSTRUMENTAL]", isSynced: false, saveToDisk: saveToDisk);
                    onSelectType?.Invoke("Static");
                    var message = saveToDisk ? $"{track.Title} marked as instrumental" : "Marked as instrumental for this session";
                    toast.Show(string.Empty, message, InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        public static async Task ShowClearLyricsDialogAsync(Track track, bool isSynced, XamlRoot xamlRoot, ILibraryService library, IToastService toast)
        {
            if (xamlRoot == null) return;

            var messageText = new TextBlock
            {
                Text = isSynced
                    ? "These lyrics will be permanently deleted.\nThis will delete the associated .lrc file from your system."
                    : "These lyrics will be permanently deleted.\nThis will remove the embedded lyrics from the file.",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            var dialog = new ContentDialog
            {
                Title = "Are you sure?",
                Content = messageText,
                PrimaryButtonText = "Yes, delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    await library.ClearLyrics(track.Id, isSynced: isSynced, saveToDisk: true);
                    toast.Show(string.Empty, $"Deleted lyrics for {track.Title}", InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show("Error", ex.Message, InfoBarSeverity.Error);
                }
            }
        }
    }
}
