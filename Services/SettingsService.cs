using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonrise.Services
{
    public interface ISettingsService : INotifyPropertyChanged
    {
        string MusicLibraryPath { get; set; }
        ElementTheme Theme { get; set; }
        bool BackgroundShadersEnabled { get; set; }
        bool BackgroundShadersBoostFps { get; set; }
        bool DiscordRpcEnabled { get; set; }
        void Save();
        void Load();
    }

    public partial class SettingsService : ObservableObject, ISettingsService
    {
        private string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moonrise", "settings.json");
        private bool _isLoading = false;

        private readonly IToastService _toast;

        [ObservableProperty]
        public partial string MusicLibraryPath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial ElementTheme Theme { get; set; } = ElementTheme.Default;

        [ObservableProperty]
        public partial bool BackgroundShadersEnabled { get; set; } = false;
        [ObservableProperty]
        public partial bool BackgroundShadersBoostFps { get; set; } = false;
        [ObservableProperty]
        public partial bool DiscordRpcEnabled { get; set; } = false;

        partial void OnMusicLibraryPathChanged(string value) { if (!_isLoading) Save(); }
        partial void OnThemeChanged(ElementTheme value) { if (!_isLoading) Save(); }
        partial void OnBackgroundShadersEnabledChanged(bool value) { if (!_isLoading) Save(); }

        partial void OnBackgroundShadersBoostFpsChanged(bool value) { if (!_isLoading) Save(); }
        partial void OnDiscordRpcEnabledChanged(bool value) { if (!_isLoading) Save(); }
        public SettingsService(IToastService toastService)
        {
            _toast = toastService;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var settings = new AppSettings
                {
                    MusicLibraryPath = MusicLibraryPath,
                    Theme = Theme,
                    BackgroundShadersEnabled = BackgroundShadersEnabled,
                    BackgroundShadersBoostFps = BackgroundShadersBoostFps,
                    DiscordRpcEnabled = DiscordRpcEnabled,
                };
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings));
            } catch(Exception ex)
            {
                _toast.Show("Error", ex.Message, InfoBarSeverity.Error);
            }
            
        }

        public void Load()
        {
            if (!File.Exists(_settingsPath)) return;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), AppSettingsContext.Default.AppSettings) ?? new();
            _isLoading = true;
            try
            {
                MusicLibraryPath = s.MusicLibraryPath;
                Theme = s.Theme;
                BackgroundShadersEnabled = s.BackgroundShadersEnabled;
                BackgroundShadersBoostFps = s.BackgroundShadersBoostFps;
                DiscordRpcEnabled = s.DiscordRpcEnabled;
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
        public ElementTheme Theme { get; set; } = ElementTheme.Default;
        public bool BackgroundShadersEnabled { get; set; } = false;
        public bool BackgroundShadersBoostFps { get; set; } = false;
        public bool DiscordRpcEnabled { get; set; } = false;
    }
}