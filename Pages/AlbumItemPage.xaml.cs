using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Pages;

[INotifyPropertyChanged]
public sealed partial class AlbumItemPage : Page
{
    private CancellationTokenSource? _artworkCts;
    private readonly IArtService art = App.Services.GetRequiredService<IArtService>();
    private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
    private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();

    [ObservableProperty]
    public partial ImageSource? AlbumArt { get; set; }


    public Album DisplayedAlbum
    {
        get { return (Album)GetValue(DisplayedAlbumProperty); }
        set { SetValue(DisplayedAlbumProperty, value); }
    }

    public static readonly DependencyProperty DisplayedAlbumProperty =
        DependencyProperty.Register(nameof(DisplayedAlbum), typeof(Album), typeof(AlbumItemPage), new PropertyMetadata(null));

    [ObservableProperty]
    public partial IEnumerable<Track>? Tracks { get; set; }

    public AlbumItemPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Album album)
        {
            DisplayedAlbum = album;
            Tracks = library.GetTracksByIds(album.TrackIds).OrderBy(t => t.TrackNumber ?? int.MaxValue);
            LoadArtworkAsync(album);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _artworkCts?.Cancel();
        _artworkCts?.Dispose();
        _artworkCts = null;
    }

    private async void LoadArtworkAsync(Album album)
    {
        _artworkCts?.Cancel();
        _artworkCts?.Dispose();
        _artworkCts = new CancellationTokenSource();
        var token = _artworkCts.Token;

        AlbumArt = null;

        try
        {
            var artImage = await art.GetArtwork(album, 320, token);
            if (!token.IsCancellationRequested)
                AlbumArt = artImage;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
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
