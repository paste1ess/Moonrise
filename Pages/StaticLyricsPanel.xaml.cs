using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Moonrise.Pages
{
    public sealed partial class StaticLyricsPanel : Page, INotifyPropertyChanged
    {
        private readonly IPlaybackService playbackService = App.Services.GetRequiredService<IPlaybackService>();
        private readonly ILibraryService libService = App.Services.GetRequiredService<ILibraryService>();
        private readonly IWebLyricService webLyrics = App.Services.GetRequiredService<IWebLyricService>();
        private readonly IToastService toast = App.Services.GetRequiredService<IToastService>();

        private string? _lyrics;
        public string? Lyrics
        {
            get => _lyrics;
            private set
            {
                _lyrics = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lyrics)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoLyricsVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyricsVisibility)));
            }
        }

        public Visibility NoLyricsVisibility => Lyrics == "No lyrics found" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LyricsVisibility => Lyrics != "No lyrics found" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private CancellationTokenSource? _lyricsCts;

        public StaticLyricsPanel()
        {
            InitializeComponent();
            playbackService.PropertyChanged += OnPlaybackPropertyChanged;
            libService.LyricsChanged += OnLyricsChanged;
            Unloaded += (s, e) =>
            {
                playbackService.PropertyChanged -= OnPlaybackPropertyChanged;
                libService.LyricsChanged -= OnLyricsChanged;
                _lyricsCts?.Cancel();
                _lyricsCts?.Dispose();
            };
            _ = UpdateLyrics(playbackService.CurrentTrack?.Id);
        }

        private void OnLyricsChanged(object? sender, LyricsChangedEventArgs e)
        {
            if (!e.IsSynced && e.TrackId == playbackService.CurrentTrack?.Id)
            {
                DispatcherQueue.TryEnqueue(() => Lyrics = ParseLyrics(e.Lyrics));
            }
        }

        private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPlaybackService.CurrentTrack))
                _ = UpdateLyrics(playbackService.CurrentTrack?.Id);
        }

        private async Task UpdateLyrics(string? trackId)
        {
            _lyricsCts?.Cancel();
            _lyricsCts?.Dispose();
            _lyricsCts = new CancellationTokenSource();
            var token = _lyricsCts.Token;

            var result = trackId != null ? await libService.GetLyrics(trackId) : null;

            if (!token.IsCancellationRequested)
                Lyrics = ParseLyrics(result);
        }

        private string ParseLyrics(string? lyrics)
        {
            if (string.IsNullOrEmpty(lyrics)) return "No lyrics found";
            if (lyrics == "[INSTRUMENTAL]") return "This is an instrumental track, no lyrics here";
            return lyrics;
        }

        private async void AddLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playbackService.CurrentTrack;
            if (currentTrack == null) return;

            var root = this.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            await LyricsDialogHelper.ShowEditLyricsDialogAsync(currentTrack, isInitiallySynced: false, root, libService, webLyrics, toast);
        }

        private async void MarkInstrumental_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = playbackService.CurrentTrack;
            if (currentTrack == null) return;

            var root = this.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            await LyricsDialogHelper.ShowMarkInstrumentalDialogAsync(currentTrack, root, libService, toast);
        }
    }
}
