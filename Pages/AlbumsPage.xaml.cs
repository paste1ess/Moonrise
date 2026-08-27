using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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

namespace Moonrise.Pages
{
    public sealed partial class AlbumsPage : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        public BulkObservableCollection<Album> Albums { get; } = new();

        public AlbumsPage()
        {
            InitializeComponent();
            library.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                library.LibraryChanged -= OnLibraryChanged;
                Albums.Clear();
            };
        }

        private void OnLibraryChanged() => Albums.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var albumList = await Task.Run(() => library.GetAllAlbums()
                    .OrderBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Albums.ReplaceRange(albumList);
            AlbumGridView.ItemsSource = Albums;
        }

        private void AlbumGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.AlbumGridItem item && item.Album is Album selectedAlbum)
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardAlbumCover", item.CoverImageControl);
                Frame.Navigate(typeof(AlbumItemPage), selectedAlbum);
            }
        }
    }
}
