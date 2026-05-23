using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace Moonrise.Services
{
    public partial class SettingsService : ObservableObject
    {
        public static readonly SettingsService Instance = new();
        private string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moonrise", "settings.json");
        private bool _isLoading = false;

        [ObservableProperty]
        public partial string MusicLibraryPath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool BackgroundShadersEnabled { get; set; } = false;
        [ObservableProperty]
        public partial bool BackgroundShadersBoostFps { get; set; } = false;

        partial void OnMusicLibraryPathChanged(string value) { if (!_isLoading) Save(); }
        partial void OnBackgroundShadersEnabledChanged(bool value) { if (!_isLoading) Save(); }

        partial void OnBackgroundShadersBoostFpsChanged(bool value) { if (!_isLoading) Save(); }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var settings = new AppSettings
            {
                MusicLibraryPath = MusicLibraryPath,
                BackgroundShadersEnabled = BackgroundShadersEnabled,
                BackgroundShadersBoostFps = BackgroundShadersBoostFps
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings));
        }

        public void Load()
        {
            if (!File.Exists(_settingsPath)) return;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), AppSettingsContext.Default.AppSettings) ?? new();
            _isLoading = true;
            try
            {
                MusicLibraryPath = s.MusicLibraryPath;
                BackgroundShadersEnabled = s.BackgroundShadersEnabled;
                BackgroundShadersBoostFps = s.BackgroundShadersBoostFps;
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    public class AppSettings
    {
        public string MusicLibraryPath { get; set; } = string.Empty;
        public bool BackgroundShadersEnabled { get; set; } = false;
        public bool BackgroundShadersBoostFps { get; set; } = false;
    }
}