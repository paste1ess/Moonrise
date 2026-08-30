using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Pages;

[INotifyPropertyChanged]
public sealed partial class ArtistItemPage : Page
{
    private CancellationTokenSource? _artworkCts;
    private readonly IArtService art = App.Services.GetRequiredService<IArtService>();
    private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
    private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();

    [ObservableProperty]
    public partial ImageSource? ArtistArt { get; set; }

    public Artist DisplayedArtist
    {
        get { return (Artist)GetValue(DisplayedArtistProperty); }
        set { SetValue(DisplayedArtistProperty, value); }
    }

    public static readonly DependencyProperty DisplayedArtistProperty =
        DependencyProperty.Register(nameof(DisplayedArtist), typeof(Artist), typeof(ArtistItemPage), new PropertyMetadata(null));

    [ObservableProperty]
    public partial IEnumerable<Album>? Albums { get; set; }

    public ArtistItemPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Artist artist)
        {
            DisplayedArtist = artist;
            Albums = library.GetArtistsAlbums(artist.Id)
                .OrderBy(a => a.Year ?? int.MaxValue)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            LoadArtworkAsync(artist);

            var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("ForwardArtistCover");
            animation?.TryStart(ArtistCoverContainer);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _artworkCts?.Cancel();
        _artworkCts?.Dispose();
        _artworkCts = null;
    }

    private async void LoadArtworkAsync(Artist artist)
    {
        _artworkCts?.Cancel();
        _artworkCts?.Dispose();
        _artworkCts = new CancellationTokenSource();
        var token = _artworkCts.Token;

        var cached = art.GetCachedArtwork(artist.Id, 320) ?? art.GetCachedArtwork(artist.Id, 174);
        ArtistArt = cached;

        try
        {
            var artImage = await art.GetArtwork(artist, 320, token);
            if (!token.IsCancellationRequested)
                ArtistArt = artImage;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private void AlbumGridItem_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is Controls.AlbumGridItem item && item.Album is Album selectedAlbum)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardAlbumCover", item.CoverImageControl);
            Frame.Navigate(typeof(AlbumItemPage), selectedAlbum);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (Albums == null) return;
        var tracks = Albums.SelectMany(a => library.GetTracksByIds(a.TrackIds).OrderBy(t => t.TrackNumber ?? int.MaxValue)).ToList();
        if (tracks.Count == 0) return;

        var selectedTrack = tracks.First();

        playback.Queue.SetQueue(tracks);
        playback.Queue.PassQueue();
        playback.Queue.SkipAndTake(0);

        playback.ShuffleState = false;

        playback.PlayTrack(selectedTrack);
        MainWindow.Instance?.NavigateToPlayer();
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        if (Albums == null) return;
        var tracks = Albums.SelectMany(a => library.GetTracksByIds(a.TrackIds).OrderBy(t => t.TrackNumber ?? int.MaxValue)).ToList();
        if (tracks.Count == 0) return;

        playback.Queue.SetQueue(tracks);

        var shuffledQueueTracks = playback.Queue.GetShuffledList();
        if (shuffledQueueTracks.Count == 0) return;

        var firstQueueTrack = shuffledQueueTracks[0];
        var firstTrack = tracks.FirstOrDefault(t => t.Id == firstQueueTrack.Id);
        if (firstTrack == null) return;

        var remainingShuffled = shuffledQueueTracks.Skip(1).ToList();
        playback.Queue.ActiveQueue.ReplaceRange(remainingShuffled);
        playback.Queue.History.Clear();

        playback.ShuffleState = true;

        playback.PlayTrack(firstTrack);
        MainWindow.Instance?.NavigateToPlayer();
    }
}
