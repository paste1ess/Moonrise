using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Intrinsics.Arm;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Moonrise.Controls
{
    [INotifyPropertyChanged]
    public sealed partial class QueueListItem : UserControl
    {
        public QueueTrack Item
        {
            get { return (QueueTrack)GetValue(ItemProperty); }
            set { SetValue(ItemProperty, value); }
        }

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(QueueTrack), typeof(QueueListItem), new PropertyMetadata(null, OnItemChanged));
        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QueueListItem control)
            {
                control.UpdateArtworkAsync();
            }
        }

        public bool DisplayGrip
        {
            get { return (bool)GetValue(DisplayGripProperty); }
            set { SetValue(DisplayGripProperty, value); }
        }

        public static readonly DependencyProperty DisplayGripProperty =
            DependencyProperty.Register(nameof(DisplayGrip), typeof(bool), typeof(QueueListItem), new PropertyMetadata(true));

        [ObservableProperty]
        public partial BitmapImage? DisplayedCoverArt { get; set; }

        private async void UpdateArtworkAsync()
        {
            DisplayedCoverArt = null;

            var currentTrack = Item;
            if (currentTrack == null) return;

            var art = await ArtService.Instance.GetArtwork(currentTrack, 40);

            if (Item == currentTrack)
            {
                DisplayedCoverArt = art;
            }
        }

        public QueueListItem()
        {
            InitializeComponent();
        }
    }
}
