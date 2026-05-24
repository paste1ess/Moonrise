using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public sealed partial class AlbumsPage : Page
    {
        public ObservableCollection<Album> Albums { get; } = new();

        public AlbumsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var albumList = await Task.Run(() => LibraryService.Instance.GetAllAlbums());

            if (Frame == null) return;

            AlbumGridView.ItemsSource = null;
            Albums.Clear();
            foreach (var album in albumList)
            {
                Albums.Add(album);
            }
            AlbumGridView.ItemsSource = Albums;
        }

        private void AlbumGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.AlbumGridItem item && item.Album is Album selectedAlbum)
            {
                // navigate to AlbumItemPage
            }
        }
    }
}
