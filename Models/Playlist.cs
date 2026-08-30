using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Models
{
    public record Playlist
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string[] TrackIds { get; set; } = Array.Empty<string>();
    }
}
