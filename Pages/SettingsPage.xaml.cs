// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        private SettingsService _settings = SettingsService.Instance;
        public SettingsPage()
        {
            InitializeComponent();
            Unloaded += (s, e) =>
            {
                this.Bindings.StopTracking();
                _settings.PropertyChanged -= Settings_PropertyChanged;
            };
            UpdateScanButtonState();
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsService.MusicLibraryPath))
            {
                DispatcherQueue.TryEnqueue(UpdateScanButtonState);
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
                await LibraryService.Instance.OpenAndScanLibrary(folder.Path);
            }
        }

        private async void ScanNow_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settings.MusicLibraryPath))
            {
                await LibraryService.Instance.HardScanLibrary(_settings.MusicLibraryPath);
            }
        }

        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_settings.MusicLibraryPath))
            {
                TaskService.Instance.Enqueue(new RelayAppCommand(async (_) =>
                {
                    await LibraryService.Instance.ScanFolder(_settings.MusicLibraryPath);
                }));
            }
        }
    }
}
