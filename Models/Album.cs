using System;

namespace Moonrise.Models
{
    public record Album
    {
        public string Id { get; set; } = string.Empty;
        public string[] TrackIds { get; set; } = Array.Empty<string>();
        public string ArtistId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string? Genre { get; set; }
        public bool IsFavorite { get; set; }

        public DateTime DateAdded { get; set; }
    }
}
