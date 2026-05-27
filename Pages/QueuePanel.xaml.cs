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

namespace Moonrise.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class QueuePanel : Page
    {
        public PlaybackService Playback => PlaybackService.Instance;
        public QueueService Queue => Playback.Queue;
        
        public QueuePanel()
        {
            InitializeComponent();
            Unloaded += (s, e) => Bindings.StopTracking();
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
