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

namespace Moonrise.Pages
{
    public sealed partial class HistoryPanel : Page
    {
        private readonly IPlaybackService playback = App.Services.GetRequiredService<IPlaybackService>();
        public QueueService Queue => playback.Queue;
        public HistoryPanel()
        {
            InitializeComponent();
            Unloaded += (s, e) => this.Bindings.StopTracking();
        }
    }
}
