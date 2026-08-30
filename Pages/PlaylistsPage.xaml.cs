using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Media.Animation;

namespace Moonrise.Pages
{
    public sealed partial class PlaylistsPage : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        public BulkObservableCollection<Playlist> Playlists { get; } = new();

        public PlaylistsPage()
        {
            InitializeComponent();
            library.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                library.LibraryChanged -= OnLibraryChanged;
                Playlists.Clear();
            };
        }

        private void OnLibraryChanged() => Playlists.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var playlistList = await Task.Run(() => library.GetAllPlaylists()
                    .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Playlists.ReplaceRange(playlistList);
            PlaylistGridView.ItemsSource = Playlists;
        }

        private void PlaylistGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.PlaylistGridItem item && item.Playlist is Playlist selectedPlaylist)
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardPlaylistCover", item.CoverControl);
                Frame.Navigate(typeof(PlaylistItemPage), selectedPlaylist);
            }
        }
    }
}
