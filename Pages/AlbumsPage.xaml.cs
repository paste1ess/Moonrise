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
        public BulkObservableCollection<Album> Albums { get; } = new();

        public AlbumsPage()
        {
            InitializeComponent();
            LibraryService.Instance.LibraryChanging += OnLibraryChanging;
            Unloaded += (s, e) =>
            {
                LibraryService.Instance.LibraryChanging -= OnLibraryChanging;
                Albums.Clear();
            };
        }

        private void OnLibraryChanging() => Albums.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var albumList = await Task.Run(() => LibraryService.Instance.GetAllAlbums());

            if (Frame == null) return;

            Albums.ReplaceRange(albumList);
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
