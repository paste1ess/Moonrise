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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class StaticLyricsPanel : Page, INotifyPropertyChanged
    {
        private PlaybackService playbackService = PlaybackService.Instance;
        private LibraryService libService = LibraryService.Instance;

        private string? _lyrics;
        public string? Lyrics
        {
            get => _lyrics;
            private set { _lyrics = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lyrics))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private CancellationTokenSource? _lyricsCts;

        public StaticLyricsPanel()
        {
            InitializeComponent();
            playbackService.PropertyChanged += OnPlaybackPropertyChanged;
            Unloaded += (s, e) =>
            {
                playbackService.PropertyChanged -= OnPlaybackPropertyChanged;
                _lyricsCts?.Cancel();
                _lyricsCts?.Dispose();
            };
            _ = UpdateLyrics(playbackService.CurrentTrack?.Id);
        }

        private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackService.CurrentTrack))
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
    }
}
