using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Models
{
    public record Artist
    {
        // relational info
        public string Id { get; init; }
        public string[] AlbumIds { get; init; }

        // metadata
        public string Name { get; init; }
        public bool IsFavorite { get; init; }

        // file info
        public DateTime DateAdded { get; init; }
    }
}
