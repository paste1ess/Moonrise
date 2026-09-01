using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    public record Lyric(string Text, TimeSpan Timestamp);

    public partial class EvaluatedLyric : ObservableObject
    {
        public string Text { get; }
        public TimeSpan Timestamp { get; }

        [ObservableProperty]
        public partial bool Active { get; set; }

        public EvaluatedLyric(string text, TimeSpan timestamp, bool active = false)
        {
            Text = text;
            Timestamp = timestamp;
            Active = active;
        }
    }

    public sealed partial class SyncedLyricsPanel : Page, INotifyPropertyChanged
    {
        private static readonly Regex LrcTimestampRegex = new(@"\[(\d{2}):(\d{2}\.\d{2,3})\]", RegexOptions.Compiled);
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IWebLyricService webLyrics = App.Services.GetRequiredService<IWebLyricService>();
        private readonly IToastService toast = App.Services.GetRequiredService<IToastService>();
        public IPlaybackService Playback { get; } = App.Services.GetRequiredService<IPlaybackService>();

        public ObservableCollection<EvaluatedLyric> DisplayLyrics { get; } = new();

        private bool _hasSyncedLyrics;
        public bool HasSyncedLyrics
        {
            get => _hasSyncedLyrics;
            private set
            {
                if (_hasSyncedLyrics != value)
                {
                    _hasSyncedLyrics = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSyncedLyrics)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyricsVisibility)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoLyricsVisibility)));
                }
            }
        }

        public Visibility LyricsVisibility => HasSyncedLyrics ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoLyricsVisibility => HasSyncedLyrics ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isUpdatingMargin = false;

        public static bool CheckIsInstrumental(Track? track, ILibraryService library)
        {
            if (track == null) return false;

            var lyrics = library.GetLyrics(track.Id).GetAwaiter().GetResult();
            return lyrics != null && lyrics.Trim().Equals("[INSTRUMENTAL]", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CheckHasSyncedLyrics(Track? track, ILibraryService library)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return false;
            if (CheckIsInstrumental(track, library)) return false;

            var sessionLyrics = library.GetSyncedLyrics(track.Id).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(sessionLyrics)) return true;

            string path = library.PathToAbsolute(track.FilePath);
            if (string.IsNullOrEmpty(path)) return false;

            string lrcPath = Path.ChangeExtension(path, ".lrc");
            if (!File.Exists(lrcPath)) return false;

            try
            {
                using var fs = new FileStream(lrcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (LrcTimestampRegex.IsMatch(line))
                        return true;
                }
            }
            catch (IOException)
            {
                return false;
            }

            return false;
        }

        private void LyricScroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isUpdatingMargin) return;
            _isUpdatingMargin = true;
            var halfHeight = e.NewSize.Height / 2;
            LyricRepeater.Margin = new Thickness(32, halfHeight, 32, halfHeight);
            _isUpdatingMargin = false;
        }

        public SyncedLyricsPanel()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Playback.PropertyChanged += Playback_PropertyChanged;
            library.LyricsChanged += Library_LyricsChanged;

            DisplayLyrics.Clear();
            HasSyncedLyrics = CheckHasSyncedLyrics(Playback.CurrentTrack, library);
            _ = GetSyncedLyrics(library.PathToAbsolute(Playback.CurrentTrack?.FilePath ?? ""));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Playback.PropertyChanged -= Playback_PropertyChanged;
            library.LyricsChanged -= Library_LyricsChanged;
        }

        private void Library_LyricsChanged(object? sender, LyricsChangedEventArgs e)
        {
            if (e.TrackId == Playback.CurrentTrack?.Id)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (e.IsSynced)
                    {
                        if (!string.IsNullOrEmpty(e.Lyrics))
                        {
                            ParseAndSetLyrics(e.Lyrics);
                        }
                        else
                        {
                            DisplayLyrics.Clear();
                            HasSyncedLyrics = false;
                        }
                    }
                    else
                    {
                        DisplayLyrics.Clear();
                        HasSyncedLyrics = CheckHasSyncedLyrics(Playback.CurrentTrack, library);
                        _ = GetSyncedLyrics(library.PathToAbsolute(Playback.CurrentTrack?.FilePath ?? ""));
                    }
                });
            }
        }

        private void Playback_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPlaybackService.CurrentTrackTime))
            {
                var currentTime = Playback.CurrentTrackTime + TimeSpan.FromSeconds(0.35);
                var activeChanged = false;
                int activeIndex = -1;

                for (int i = 0; i < DisplayLyrics.Count; i++)
                {
                    var lyric = DisplayLyrics[i];
                    var nextLyric = i + 1 < DisplayLyrics.Count ? DisplayLyrics[i + 1] : null;

                    bool isActive = false;
                    if (lyric.Timestamp <= currentTime)
                    {
                        if (nextLyric == null) isActive = true;
                        else if (nextLyric.Timestamp > currentTime) isActive = true;
                    }

                    if (lyric.Active != isActive)
                    {
                        lyric.Active = isActive;
                        activeChanged = true;
                    }

                    if (isActive)
                    {
                        activeIndex = i;
                    }
                }

                if (activeChanged && activeIndex != -1)
                {
                    var capturedIndex = activeIndex;
                    _ = Task.Delay(50).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
                    {
                        var element = LyricRepeater.TryGetElement(capturedIndex);
                        if (element != null)
                        {
                            element.StartBringIntoView(new BringIntoViewOptions
                            {
                                VerticalAlignmentRatio = 0.5,
                                AnimationDesired = true
                            });
                        }
                        else
                        {
                            LyricScroller.ChangeView(null, capturedIndex * 96, null);
                        }
                    }));
                }
            }
            else if (e.PropertyName == nameof(IPlaybackService.CurrentTrack))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DisplayLyrics.Clear();
                    HasSyncedLyrics = CheckHasSyncedLyrics(Playback.CurrentTrack, library);
                    _ = GetSyncedLyrics(library.PathToAbsolute(Playback.CurrentTrack?.FilePath ?? ""));
                });
            }
        }

        private async Task GetSyncedLyrics(string? path)
        {
            var currentTrack = Playback.CurrentTrack;
            if (currentTrack != null)
            {
                var sessionLyrics = await library.GetSyncedLyrics(currentTrack.Id);
                if (!string.IsNullOrEmpty(sessionLyrics))
                {
                    DispatcherQueue.TryEnqueue(() => ParseAndSetLyrics(sessionLyrics));
                    return;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DisplayLyrics.Clear();
                    HasSyncedLyrics = false;
                });
                return;
            }

            string lrcPath = Path.ChangeExtension(path, ".lrc");
            if (!File.Exists(lrcPath))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DisplayLyrics.Clear();
                    HasSyncedLyrics = false;
                });
                return;
            }

            List<Lyric> lyrics = new();

            try
            {
                using var fs = new FileStream(lrcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var match = LrcTimestampRegex.Match(line);
                    if (match.Success)
                    {
                        int minutes = int.Parse(match.Groups[1].Value);
                        double seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        var timestamp = TimeSpan.FromSeconds(minutes * 60 + seconds);
                        string lyric = line.Substring(match.Index + match.Length).Trim();
                        lyrics.Add(new(lyric, timestamp));
                    }
                }
            }
            catch (IOException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DisplayLyrics.Clear();
                    HasSyncedLyrics = false;
                });
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                DisplayLyrics.Clear();
                foreach (var l in lyrics)
                    DisplayLyrics.Add(new EvaluatedLyric(l.Text, l.Timestamp));
                HasSyncedLyrics = lyrics.Count > 0;
            });
        }

        private void ParseAndSetLyrics(string lrcContent)
        {
            List<Lyric> lyrics = new();
            using var reader = new StringReader(lrcContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var match = LrcTimestampRegex.Match(line);
                if (match.Success)
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    double seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    var timestamp = TimeSpan.FromSeconds(minutes * 60 + seconds);
                    string lyric = line.Substring(match.Index + match.Length).Trim();
                    lyrics.Add(new(lyric, timestamp));
                }
            }

            DisplayLyrics.Clear();
            foreach (var l in lyrics)
                DisplayLyrics.Add(new EvaluatedLyric(l.Text, l.Timestamp));
            HasSyncedLyrics = lyrics.Count > 0;
        }

        private async void AddLyrics_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = Playback.CurrentTrack;
            if (currentTrack == null) return;

            var root = this.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            await LyricsDialogHelper.ShowEditLyricsDialogAsync(currentTrack, isInitiallySynced: true, root, library, webLyrics, toast);
        }

        private async void MarkInstrumental_Click(object sender, RoutedEventArgs e)
        {
            var currentTrack = Playback.CurrentTrack;
            if (currentTrack == null) return;

            var root = this.XamlRoot ?? this.Content?.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (root == null) return;

            await LyricsDialogHelper.ShowMarkInstrumentalDialogAsync(currentTrack, root, library, toast);
        }
    }
}
