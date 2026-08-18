using CommunityToolkit.Mvvm.ComponentModel;
using DiscordRPC;
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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Moonrise.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>

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


    public sealed partial class SyncedLyricsPanel : Page
    {
        private static readonly Regex LrcTimestampRegex = new(@"\[(\d{2}):(\d{2}\.\d{2,3})\]", RegexOptions.Compiled);
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        public PlaybackService Playback => PlaybackService.Instance;

        //List<Lyric> data = [];
        //    new Lyric("What, what, what, what, what", TimeSpan.FromSeconds(0)),
        //    new Lyric("Ummmmm", TimeSpan.FromSeconds(4.5)),
        //    new Lyric("This is the 'first' line of the song", TimeSpan.FromSeconds(8.2)),
        //    new Lyric("And now", TimeSpan.FromSeconds(12.0)),
        //    new Lyric("    Um", TimeSpan.FromSeconds(15.5)),
        //    new Lyric("", TimeSpan.FromSeconds(20.0)),
        //    new Lyric("Family guy funny moments", TimeSpan.FromSeconds(27.1)),
        //    new Lyric("Idk i forgot", TimeSpan.FromSeconds(32.0))
        //];


        public ObservableCollection<EvaluatedLyric> DisplayLyrics { get; } = new();

        private bool _isUpdatingMargin = false;

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

            DisplayLyrics.Clear();
            _ = GetSyncedLyrics(library.PathToAbsolute(Playback.CurrentTrack?.FilePath ?? ""));
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Playback.PropertyChanged -= Playback_PropertyChanged;
        }

        private void Playback_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackService.CurrentTrackTime))
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
            else if (e.PropertyName == nameof(PlaybackService.CurrentTrack))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DisplayLyrics.Clear();
                    _ = GetSyncedLyrics(library.PathToAbsolute(Playback.CurrentTrack?.FilePath ?? ""));
                });
                
            }
        }
        private async Task GetSyncedLyrics(string? path)
        {
            if (path == null) return;

            string lrcPath = Path.ChangeExtension(path, ".lrc");
            if (!File.Exists(lrcPath)) return;

            List<Lyric> lyrics = new();

            try
            {
                using var reader = new StreamReader(lrcPath);
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
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                DisplayLyrics.Clear();
                foreach (var l in lyrics)
                    DisplayLyrics.Add(new EvaluatedLyric(l.Text, l.Timestamp));
            });
        }
    }
}
