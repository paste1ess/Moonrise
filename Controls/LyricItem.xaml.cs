using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Controls
{
    public sealed partial class LyricItem : UserControl
    {
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(LyricItem), new PropertyMetadata(""));

        public bool Active
        {
            get => (bool)GetValue(ActiveProperty);
            set => SetValue(ActiveProperty, value);
        }

        public static readonly DependencyProperty ActiveProperty =
            DependencyProperty.Register(nameof(Active), typeof(bool), typeof(LyricItem), new PropertyMetadata(false, OnActiveChanged));
        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LyricItem)d;
            var isActive = (bool)e.NewValue;
            if (isActive)
            {
                control.ActiveStoryboard.Begin();
            }
            else
            {
                control.InactiveStoryboard.Begin();
            }
        }

        public double BaseFontSize
        {
            get => (double)GetValue(BaseFontSizeProperty);
            set => SetValue(BaseFontSizeProperty, value);
        }
        public static readonly DependencyProperty BaseFontSizeProperty =
            DependencyProperty.Register(nameof(BaseFontSize), typeof(double), typeof(LyricItem), new PropertyMetadata(40.0, OnFontSizeFactorsChanged));

        public double AnimationMultiplier
        {
            get => (double)GetValue(AnimationMultiplierProperty);
            set => SetValue(AnimationMultiplierProperty, value);
        }
        public static readonly DependencyProperty AnimationMultiplierProperty =
            DependencyProperty.Register(nameof(AnimationMultiplier), typeof(double), typeof(LyricItem), new PropertyMetadata(1.0, OnFontSizeFactorsChanged));

        private static void OnFontSizeFactorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LyricItem)d;
            control.TextObject.FontSize = control.BaseFontSize * control.AnimationMultiplier;
        }

        public LyricItem()
        {
            InitializeComponent();
        }

        private DispatcherTimer? _resizeTimer;

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width == e.PreviousSize.Width) return;

            _resizeTimer?.Stop();
            _resizeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _resizeTimer.Tick += (s, _) =>
            {
                _resizeTimer.Stop();
                var scale = Math.Clamp(e.NewSize.Width / 700.0, 0.6, 1.0);
                BaseFontSize = 40.0 * scale;
            };
            _resizeTimer.Start();
        }
    }
}
