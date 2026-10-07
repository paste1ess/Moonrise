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

public sealed partial class ProgressSlider : UserControl
{
    private readonly IPlaybackService playbackService = App.Services.GetRequiredService<IPlaybackService>();

    public ProgressSlider()
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


    public string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";

    public Style BoolToTextStyle(bool value)
    {
        return value ? (Style)Application.Current.Resources["CaptionTextBlockStyle"] : (Style)Application.Current.Resources["BodyTextBlockStyle"];
    }

    private bool _isUserDragging = false;

    private void ProgressSlider_Loaded(object sender, RoutedEventArgs e)
    {
        ProgressSliderElement.AddHandler(PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
        ProgressSliderElement.AddHandler(PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
    }

    private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (playbackService.CurrentTrack == null) return;
        _isUserDragging = true;
        playbackService.Pause();
    }

    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isUserDragging) return;
        if (playbackService.CurrentTrack == null)
        {
            _isUserDragging = false;
            return;
        }
        playbackService.Scrub(TimeSpan.FromSeconds(e.NewValue));
    }

    private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isUserDragging) return;
        _isUserDragging = false;

        if (playbackService.CurrentTrack == null) return;
        playbackService.Scrub(TimeSpan.FromSeconds(ProgressSliderElement.Value));
        playbackService.Play();
    }
}
