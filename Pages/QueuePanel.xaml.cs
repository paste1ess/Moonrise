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
    public sealed partial class QueuePanel : Page
    {
        public IPlaybackService Playback { get; } = App.Services.GetRequiredService<IPlaybackService>();
        public QueueService Queue => Playback.Queue;
        
        public QueuePanel()
        {
            InitializeComponent();
            Unloaded += (s, e) =>
            {
                Bindings.StopTracking();
                Playback.PropertyChanged -= Playback_PropertyChanged;
            };
            Playback.PropertyChanged += Playback_PropertyChanged;
        }

        private void Playback_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ShuffleState")
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (QueueListView.Items.Count > 0)
                    {
                        QueueListView.ScrollIntoView(QueueListView.Items[0]);
                    }
                });
            }
        }
    }
}
