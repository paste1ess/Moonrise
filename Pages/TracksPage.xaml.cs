using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonrise.Models;
using Moonrise.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Moonrise.Pages
{
    public sealed partial class TracksPage : Page
    {
        // Property for data binding
        public BulkObservableCollection<Track> Tracks { get; } = new();

        public TracksPage()
        {
            InitializeComponent();
            TrackListView.Tracks = Tracks;

            LibraryService.Instance.LibraryChanged += OnLibraryChanged;
            Unloaded += (s, e) =>
            {
                LibraryService.Instance.LibraryChanged -= OnLibraryChanged;
                Tracks.Clear();
            };
        }

        private void OnLibraryChanged() => Tracks.Clear();

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var trackList = await Task.Run(() =>
                LibraryService.Instance.GetAllTracks()
                    .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (Frame == null) return;

            Tracks.ReplaceRange(trackList);
            TrackListView.Tracks = Tracks;
        }
    }
}