using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Models
{
    public record Track
    {
        // relational info
        required public string Id { get; init; }
        required public string AlbumId { get; init; }
        required public string ArtistId { get; init; }

        // metadata
        required public string Title { get; init; }
        required public string Album { get; init; } // album name
        required public string Artist { get; init; } // artist name
        public int? Year { get; init; }
        public string? Genre { get; init; }
        public bool IsFavorite { get; init; }

        // file info
        required public string FilePath { get; init; }
        public int Bitrate { get; init; }
        public TimeSpan Duration { get; init; }
        public DateTime DateAdded { get; init; }
    }
}
