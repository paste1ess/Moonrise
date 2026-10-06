using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using NAudio.CoreAudioApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MiniplayerWindow : Window
    {
        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetDpiForWindow(IntPtr hwnd);

        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();
        public OverlappedPresenter? presenter;

        private double lastKnownScale;


        public MiniplayerWindow()
        {
            InitializeComponent();
            presenter = AppWindow.Presenter as OverlappedPresenter;
            presenter?.IsMaximizable = false;
            presenter?.IsMinimizable = false;
            presenter?.IsResizable = false;

            ExtendsContentIntoTitleBar = true;

            SetTitleBar(DragArea);

            RootGrid.Loaded += RootGrid_Loaded;
            RootGrid.PointerEntered += RootGrid_PointerEntered;
            RootGrid.PointerExited += RootGrid_PointerExited;
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Titlebar.Opacity = 0;
            InfoPanel.Opacity = 0;
        }

        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Titlebar.Opacity = 1;
            InfoPanel.Opacity = 1;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            lastKnownScale = Content.XamlRoot?.RasterizationScale ?? 1;
            AdjustWindowSize(lastKnownScale);
            //Content.XamlRoot?.Changed += XamlRoot_Changed;
        }

        private void AdjustWindowSize(double scale)
        {
            AppWindow.ResizeClient(new SizeInt32((int)Math.Round(320 * scale), (int)Math.Round((320 - 30) * scale)));
        }

        private void TogglePinned()
        {
            presenter?.IsAlwaysOnTop = !presenter.IsAlwaysOnTop;
            if (presenter is not null) PinnedIcon.Glyph = presenter.IsAlwaysOnTop ? "\uE77A" : "\uE718";
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePinned();
        }
    }
}
