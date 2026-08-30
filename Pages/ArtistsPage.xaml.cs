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
    public sealed partial class ArtistsPage : Page
    {
        private readonly ILibraryService library = App.Services.GetRequiredService<ILibraryService>();
        public BulkObservableCollection<Artist> Artists { get; } = new();

        public ArtistsPage()
        {
            InitializeComponent();
            library.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                library.LibraryChanged -= OnLibraryChanged;
                Artists.Clear();
            };
        }

        private void OnLibraryChanged() => Artists.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var artistList = await Task.Run(() => library.GetAllArtists()
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Artists.ReplaceRange(artistList);
            ArtistGridView.ItemsSource = Artists;
        }

        private void ArtistGridItem_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.ArtistGridItem item && item.Artist is Artist selectedArtist)
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("ForwardArtistCover", item.CoverControl);
                Frame.Navigate(typeof(ArtistItemPage), selectedArtist);
            }
        }
    }
}
