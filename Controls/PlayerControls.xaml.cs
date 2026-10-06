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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Controls;

public sealed partial class PlayerControls : UserControl
{
    private readonly IPlaybackService playbackService = App.Services.GetRequiredService<IPlaybackService>();

    public PlayerControls()
    {
        InitializeComponent();
    }

    public bool Small
    {
        get { return (bool)GetValue(SmallProperty); }
        set { SetValue(SmallProperty, value); }
    }

    public static readonly DependencyProperty SmallProperty =
        DependencyProperty.Register(nameof(Small), typeof(bool), typeof(ProgressSlider), new PropertyMetadata(false));

    public string PlaybackStateToGlyph(PlaybackState state)
    => state == PlaybackState.Playing ? "\uE769" : "\uE768";
    public string RepeatStateToGlyph(RepeatState state) => state == RepeatState.RepeatOne ? "\uE8ED" : state == RepeatState.RepeatAll ? "\uE8EE" : "\uF5E7";
    public bool RepeatStateToActive(RepeatState state) => state != RepeatState.Off;

    public double BoolToButtonSize(bool value) => value ? 40.0 : 46.0;
    public double BoolToLargeButtonSize(bool value) => value ? 40.0 : 58.0;
    public double BoolToFontSize(bool value) => value ? 16.0 : 18.0;

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (playbackService.CurrentPlaybackState == PlaybackState.Paused)
            playbackService.Play();
        else if (playbackService.CurrentPlaybackState == PlaybackState.Playing)
            playbackService.Pause();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        playbackService.Next();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        playbackService.Back();
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        playbackService.ToggleShuffle();
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (playbackService.RepeatState == RepeatState.Off)
        {
            playbackService.RepeatState = RepeatState.RepeatAll;
        }
        else if (playbackService.RepeatState == RepeatState.RepeatAll)
        {
            playbackService.RepeatState = RepeatState.RepeatOne;
        }
        else
        {
            playbackService.RepeatState = RepeatState.Off;
        }
    }
}
