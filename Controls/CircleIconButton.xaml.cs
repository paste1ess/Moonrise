using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Controls
{
    public sealed partial class CircleIconButton : UserControl
    {
        public event RoutedEventHandler Click;
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(CircleIconButton), new PropertyMetadata(string.Empty));

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register(nameof(Size), typeof(double), typeof(CircleIconButton), new PropertyMetadata(32.0));

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(CircleIconButton), new PropertyMetadata(18.0));

        public bool Filled
        {
            get => (bool)GetValue(FilledProperty);
            set => SetValue(FilledProperty, value);
        }

        public static readonly DependencyProperty FilledProperty =
            DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(CircleIconButton), new PropertyMetadata(true, OnFilledChanged));
        private static void OnFilledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CircleIconButton)d;
            control.CircleIconButtonObject.Style = (bool)e.NewValue
                ? (Style)control.Resources["FilledIconButton"]
                : (Style)control.Resources["TransparentIconButton"];
        }

        public bool Active
        {
            get => (bool)GetValue(ActiveProperty);
            set => SetValue(ActiveProperty, value);
        }

        public static readonly DependencyProperty ActiveProperty =
            DependencyProperty.Register(nameof(Active), typeof(bool), typeof(CircleIconButton), new PropertyMetadata(false, OnActiveChanged));
        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CircleIconButton)d;
            if (Application.Current.Resources.TryGetValue((bool)e.NewValue ? "AccentFillColorDefaultBrush" : "TextFillColorPrimaryBrush", out var brushObj) && brushObj is SolidColorBrush targetBrush)
            {
                var anim = new Microsoft.UI.Xaml.Media.Animation.ColorAnimation
                {
                    To = targetBrush.Color,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };
                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, control.GlyphBrush);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Color");
                sb.Children.Add(anim);
                sb.Begin();
            }
        }

        public CircleIconButton()
        {
            this.InitializeComponent();
            CircleIconButtonObject.Click += (s, e) => Click?.Invoke(this, e);
        }
    }
}
