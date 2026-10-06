using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MiniplayerWindow : Window
    {
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();

        public MiniplayerWindow()
        {
            InitializeComponent();
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            presenter?.IsMaximizable = false;
            presenter?.IsMinimizable = false;
            presenter?.IsResizable = false;

            ExtendsContentIntoTitleBar = true;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(320, 320 - 7)); // genuinely no idea why but it happens to be like exactly 7 pixels too tall
        }
    }
}
