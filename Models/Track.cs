using System;

namespace Moonrise.Models
{
    public record Track
    {
        public string Id { get; set; } = string.Empty;
        public string AlbumId { get; set; } = string.Empty;
        public string ArtistId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string? Genre { get; set; }
        public int? Bpm { get; set; }
        public bool IsFavorite { get; set; }

        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Bitrate { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime DateAdded { get; set; }
        public string LastModified { get; set; } = string.Empty;
        public bool IsPresent { get; set; }
    }
}
