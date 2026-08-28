using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
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
    public sealed partial class FavoritesPage : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();
        public BulkObservableCollection<Track> Tracks { get; } = new();

        public FavoritesPage()
        {
            InitializeComponent();
            TrackListView.Tracks = Tracks;

            library.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                library.LibraryChanged -= OnLibraryChanged;
                Tracks.Clear();
            };
        }

        private void OnLibraryChanged() => Tracks.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var trackList = await Task.Run(() =>
                library.GetAllFavoriteTracks()
                    .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Tracks.ReplaceRange(trackList);
            TrackListView.Tracks = Tracks;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            var selectedTrack = list.First();

            playback.Queue.SetQueue(list);
            playback.Queue.PassQueue();
            playback.Queue.SkipAndTake(0);

            playback.ShuffleState = false;

            playback.PlayTrack(selectedTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks == null) return;
            var list = Tracks.ToList();
            if (list.Count == 0) return;

            playback.Queue.SetQueue(list);

            var shuffledQueueTracks = playback.Queue.GetShuffledList();
            if (shuffledQueueTracks.Count == 0) return;

            var firstQueueTrack = shuffledQueueTracks[0];
            var firstTrack = list.FirstOrDefault(t => t.Id == firstQueueTrack.Id);
            if (firstTrack == null) return;

            var remainingShuffled = shuffledQueueTracks.Skip(1).ToList();
            playback.Queue.ActiveQueue.ReplaceRange(remainingShuffled);
            playback.Queue.History.Clear();

            playback.ShuffleState = true;

            playback.PlayTrack(firstTrack);
            MainWindow.Instance?.NavigateToPlayer();
        }
    }
}
