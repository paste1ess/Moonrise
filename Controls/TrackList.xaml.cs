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

namespace Moonrise.Controls;

public sealed partial class TrackList : UserControl
{
    public IEnumerable<Track> Tracks
    {
        get { return (IEnumerable<Track>)GetValue(TracksProperty); }
        set { SetValue(TracksProperty, value); }
    }

    public static readonly DependencyProperty TracksProperty =
        DependencyProperty.Register(nameof(Tracks), typeof(IEnumerable<Track>), typeof(TrackList), new PropertyMetadata(null));



    public bool ArtView
    {
        get { return (bool)GetValue(ArtViewProperty); }
        set { SetValue(ArtViewProperty, value); }
    }

    public static readonly DependencyProperty ArtViewProperty =
        DependencyProperty.Register(nameof(ArtView), typeof(bool), typeof(TrackList), new PropertyMetadata(true));



    public bool TrackView
    {
        get { return (bool)GetValue(TrackViewProperty); }
        set { SetValue(TrackViewProperty, value); }
    }

    public static readonly DependencyProperty TrackViewProperty =
        DependencyProperty.Register(nameof(TrackView), typeof(bool), typeof(TrackList), new PropertyMetadata(false));

    public TrackList()
    {
        InitializeComponent();
    }

    private async void TrackListItem_Clicked(object sender, RoutedEventArgs e) // might make this customizable in the future but i don't need that atm so
    {
        if (sender is Controls.TrackListItem item && item.Song is Track selectedTrack)
        {
            var list = Tracks.ToList();
            int index = list.IndexOf(selectedTrack);
            if (index < 0) return;

            var queue = PlaybackService.Instance.Queue;
            queue.SetQueue(list);

            QueueTrack? qt;
            if (PlaybackService.Instance.ShuffleState)
            {
                var selectedQueueTrack = QueueTrack.FromTrack(selectedTrack);
                queue.ActiveQueue.ReplaceRange(queue.GetShuffledList(selectedQueueTrack));
                queue.History.Clear();
                qt = selectedQueueTrack;
            }
            else
            {
                queue.PassQueue();
                qt = queue.SkipAndTake(index);
            }

            if (qt == null) return;

            var track = await Task.Run(() => LibraryService.Instance.GetTrack(qt.Id));
            if (track == null) return;

            PlaybackService.Instance.PlayTrack(track);
            MainWindow.Instance?.NavigateToPlayer();
        }
    }
}
