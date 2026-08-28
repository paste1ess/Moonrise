// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Moonrise.Services;
using System;
using System.ComponentModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ISettingsService _settings = App.Services.GetRequiredService<ISettingsService>();
        private readonly ITaskService task = App.Services.GetRequiredService<ITaskService>();
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        public SettingsPage()
        {
            InitializeComponent();
            Unloaded += (s, e) =>
            {
                this.Bindings.StopTracking();
                _settings.PropertyChanged -= Settings_PropertyChanged;
            };
            UpdateScanButtonState();
            ThemeComboBox.SelectedIndex = _settings.Theme switch
            {
                ElementTheme.Default => 0,
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettingsService.MusicLibraryPath))
            {
                DispatcherQueue.TryEnqueue(UpdateScanButtonState);
            }
            else if (e.PropertyName == nameof(ISettingsService.Theme))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ThemeComboBox.SelectedIndex = _settings.Theme switch
                    {
                        ElementTheme.Default => 0,
                        ElementTheme.Light => 1,
                        ElementTheme.Dark => 2,
                        _ => 0
                    };
                });
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<ElementTheme>(item.Tag?.ToString(), out var theme))
            {
                _settings.Theme = theme;
            }
        }

        private void UpdateScanButtonState()
        {
            ScanButton.IsEnabled = !string.IsNullOrEmpty(_settings.MusicLibraryPath);
        }

        private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            var mainWindow = MainWindow.Instance;
            if (mainWindow == null) return;

            var hwnd = WindowNative.GetWindowHandle(mainWindow);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _settings.MusicLibraryPath = folder.Path;
                await library.OpenAndScanLibrary(folder.Path);
            }
        }

        private async void ScanNow_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settings.MusicLibraryPath))
            {
                await library.HardScanLibrary(_settings.MusicLibraryPath);
            }
        }

        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settings.MusicLibraryPath))
            {
                task.Enqueue(new RelayAppCommand(async (_) =>
                {
                    await library.ScanFolder(_settings.MusicLibraryPath);
                }));
            }
        }
    }
}
