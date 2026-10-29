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
                Content = Loc.Get("LyricsDialog_SaveToDisk"),
                IsChecked = false
            };

            var syncedLyricsCheckBox = new CheckBox
            {
                Content = Loc.Get("LyricsDialog_SyncedLyrics"),
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
                        ? Loc.Get("LyricsDialog_EditSyncedSaveDescription")
                        : Loc.Get("LyricsDialog_EditSyncedSessionDescription");
                }
                else
                {
                    descriptionText.Text = isSave
                        ? Loc.Get("LyricsDialog_EditStaticSaveDescription")
                        : Loc.Get("LyricsDialog_EditStaticSessionDescription");
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
                Title = Loc.Get("LyricsDialog_EditTitle"),
                Content = contentPanel,
                PrimaryButtonText = Loc.Get("LyricsDialog_OnlineButton"),
                SecondaryButtonText = Loc.Get("LyricsDialog_ClipboardButton"),
                CloseButtonText = Loc.Get("LyricsDialog_CancelButton"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            bool saveToDisk = saveToDiskCheckBox.IsChecked == true;
            bool targetSynced = syncedLyricsCheckBox.IsChecked == true;

            if (result == ContentDialogResult.Primary)
            {
                using var toastHandle = toast.ShowProgress(string.Empty, Loc.Format("LyricsDialog_SearchingLyrics", track.Title), isIndeterminate: true, isClosable: true);

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
                        Title = Loc.Get("LyricsDialog_NetworkErrorTitle"),
                        Content = Loc.Format("LyricsDialog_NetworkErrorMessage", networkError.Message),
                        CloseButtonText = Loc.Get("LyricsDialog_OkButton"),
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
                        Title = Loc.Get("LyricsDialog_NotFoundTitle"),
                        Content = Loc.Format("LyricsDialog_NotFoundMessage", track.Title, track.Artist),
                        CloseButtonText = Loc.Get("LyricsDialog_OkButton"),
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = xamlRoot
                    };
                    await notFoundDialog.ShowAsync();
                    return;
                }

                if (webResult.IsInstrumental)
                {
                    var promptText = saveToDisk
                        ? Loc.Format("LyricsDialog_InstrumentalPromptSave", track.Title)
                        : Loc.Format("LyricsDialog_InstrumentalPromptSession", track.Title);

                    var instrumentalDialog = new ContentDialog
                    {
                        Title = Loc.Get("LyricsDialog_InstrumentalTitle"),
                        Content = promptText,
                        PrimaryButtonText = Loc.Get("LyricsDialog_InstrumentalConfirmButton"),
                        CloseButtonText = Loc.Get("LyricsDialog_CancelButton"),
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
                            var message = saveToDisk
                                ? Loc.Format("LyricsDialog_MarkedInstrumentalSaveToast", track.Title)
                                : Loc.Get("LyricsDialog_MarkedInstrumentalSessionToast");
                            toast.Show(string.Empty, message, InfoBarSeverity.Informational);
                        }
                        catch (Exception ex)
                        {
                            toast.Show(Loc.Get("LyricsDialog_ErrorTitle"), ex.Message, InfoBarSeverity.Error);
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
                            var message = saveToDisk
                                ? Loc.Format("LyricsDialog_SavedLyricsToast", track.Title)
                                : Loc.Get("LyricsDialog_UsingLyricsSessionToast");
                            toast.Show(string.Empty, message, InfoBarSeverity.Success);
                        }
                        else
                        {
                            toast.Show(string.Empty, Loc.Get("LyricsDialog_ClipboardEmptyToast"), InfoBarSeverity.Warning);
                        }
                    }
                    else
                    {
                        toast.Show(string.Empty, Loc.Get("LyricsDialog_ClipboardEmptyToast"), InfoBarSeverity.Warning);
                    }
                }
                catch (Exception ex)
                {
                    toast.Show(Loc.Get("LyricsDialog_ClipboardErrorTitle"), ex.Message, InfoBarSeverity.Error);
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
                Title = Loc.Get("LyricsDialog_ConfirmTitle"),
                Content = scroller,
                PrimaryButtonText = Loc.Get("LyricsDialog_ConfirmPrimaryButton"),
                CloseButtonText = Loc.Get("LyricsDialog_ConfirmCloseButton"),
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
                    var message = saveToDisk
                        ? Loc.Format("LyricsDialog_SavedLyricsToast", track.Title)
                        : Loc.Get("LyricsDialog_UsingLyricsSessionToast");
                    toast.Show(string.Empty, message, InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                    toast.Show(Loc.Get("LyricsDialog_ErrorTitle"), ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        public static async Task ShowMarkInstrumentalDialogAsync(Track track, XamlRoot xamlRoot, ILibraryService library, IToastService toast, Action<string>? onSelectType = null)
        {
            if (xamlRoot == null) return;

            var saveToDiskCheckBox = new CheckBox
            {
                Content = Loc.Get("LyricsDialog_SaveToDisk"),
                IsChecked = false
            };

            var messageText = new TextBlock
            {
                Text = Loc.Format("LyricsDialog_MarkInstrumentalMessage", track.Title),
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
                Title = Loc.Get("LyricsDialog_MarkInstrumentalTitle"),
                Content = contentPanel,
                PrimaryButtonText = Loc.Get("LyricsDialog_YesButton"),
                CloseButtonText = Loc.Get("LyricsDialog_CancelButton"),
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
                    var message = saveToDisk
                        ? Loc.Format("LyricsDialog_MarkedInstrumentalSaveToast", track.Title)
                        : Loc.Get("LyricsDialog_MarkedInstrumentalSessionToast");
                    toast.Show(string.Empty, message, InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show(Loc.Get("LyricsDialog_ErrorTitle"), ex.Message, InfoBarSeverity.Error);
                }
            }
        }

        public static async Task ShowClearLyricsDialogAsync(Track track, bool isSynced, XamlRoot xamlRoot, ILibraryService library, IToastService toast)
        {
            if (xamlRoot == null) return;

            var messageText = new TextBlock
            {
                Text = isSynced
                    ? Loc.Get("LyricsDialog_ClearSyncedMessage")
                    : Loc.Get("LyricsDialog_ClearStaticMessage"),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            var dialog = new ContentDialog
            {
                Title = Loc.Get("LyricsDialog_ClearTitle"),
                Content = messageText,
                PrimaryButtonText = Loc.Get("LyricsDialog_ClearConfirmButton"),
                CloseButtonText = Loc.Get("LyricsDialog_CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    await library.ClearLyrics(track.Id, isSynced: isSynced, saveToDisk: true);
                    toast.Show(string.Empty, Loc.Format("LyricsDialog_DeletedLyricsToast", track.Title), InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    toast.Show(Loc.Get("LyricsDialog_ErrorTitle"), ex.Message, InfoBarSeverity.Error);
                }
            }
        }
    }
}
