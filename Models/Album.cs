using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Models
{
    public record Album
    {
        // relational info
        public string Id { get; init; }
        public string[] TrackIds { get; init; }
        public string ArtistId { get; init; }

        // metadata
        public string Title { get; init; }
        public string Artist { get; init; } // album artist name
        public int? Year { get; init; }
        public string? Genre { get; init; }
        public bool IsFavorite { get; init; }

        // file info
        public DateTime DateAdded { get; init; }

    }
}
