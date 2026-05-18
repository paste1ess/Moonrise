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
    public sealed partial class PlayerPage : Page
    {
        private PlaybackService playbackService = PlaybackService.Instance;
        private Track exampleTrack = new Track
        {
            Id = "1",
            Title = "Monday",
            FilePath = "C:/Users/jamied/Documents/devmusic/K.Shiraki - Monday/Monday.ogg",
            AlbumId = "2",
            ArtistId = "3",
            Album = "Monday - Single",
            Artist = "K.Shiraki",
            Duration = TimeSpan.FromSeconds(155)
        };
        public PlayerPage()
        {
            InitializeComponent();
        }
        public string PlaybackStateToGlyph(PlaybackState state)
    => state == PlaybackState.Playing ? "\uE769" : "\uE768";
        public string FormatDuration(TimeSpan duration)
    => $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (playbackService.CurrentPlaybackState == PlaybackState.Paused)
                playbackService.Play();
            else if (playbackService.CurrentPlaybackState == PlaybackState.Playing)
                playbackService.Pause();
            else
                playbackService.PlayTrack(exampleTrack);
        }
    }
}
