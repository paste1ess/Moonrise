using System;

namespace Moonrise.Models
{
    public record Artist
    {
        public string Id { get; set; } = string.Empty;
        public string[] AlbumIds { get; set; } = Array.Empty<string>();

        public string Name { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        public DateTime DateAdded { get; set; }
    }
}
