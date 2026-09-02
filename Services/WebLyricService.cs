using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Services
{
    public record WebLyricResult(string? PlainLyrics, string? SyncedLyrics, bool IsInstrumental);

    public interface IWebLyricService
    {
        Task<WebLyricResult?> FetchLyricsAsync(string title, string artist, string? album, TimeSpan? duration, CancellationToken token = default);
    }

    public class WebLyricService : IWebLyricService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static WebLyricService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Moonrise/1.0 (https://github.com/paste1ess/Moonrise)");
        }

        public async Task<WebLyricResult?> FetchLyricsAsync(string title, string artist, string? album, TimeSpan? duration, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                return null;

            try
            {
                var queryParams = $"track_name={Uri.EscapeDataString(title.Trim())}&artist_name={Uri.EscapeDataString(artist.Trim())}";
                if (!string.IsNullOrWhiteSpace(album) && !album.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase))
                {
                    queryParams += $"&album_name={Uri.EscapeDataString(album.Trim())}";
                }
                if (duration.HasValue && duration.Value.TotalSeconds > 0)
                {
                    queryParams += $"&duration={(int)duration.Value.TotalSeconds}";
                }

                var url = $"https://lrclib.net/api/get?{queryParams}";
                using var response = await _httpClient.GetAsync(url, token);
                if (response.IsSuccessStatusCode)
                {
                    var dto = await response.Content.ReadFromJsonAsync<LrcLibResponse>(cancellationToken: token);
                    if (dto != null)
                    {
                        return new WebLyricResult(dto.PlainLyrics, dto.SyncedLyrics, dto.Instrumental);
                    }
                }

                var searchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title.Trim())}&artist_name={Uri.EscapeDataString(artist.Trim())}";
                using var searchResponse = await _httpClient.GetAsync(searchUrl, token);
                if (searchResponse.IsSuccessStatusCode)
                {
                    var list = await searchResponse.Content.ReadFromJsonAsync<LrcLibResponse[]>(cancellationToken: token);
                    if (list != null && list.Length > 0)
                    {
                        var best = list[0];
                        return new WebLyricResult(best.PlainLyrics, best.SyncedLyrics, best.Instrumental);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private class LrcLibResponse
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("trackName")]
            public string? TrackName { get; set; }

            [JsonPropertyName("artistName")]
            public string? ArtistName { get; set; }

            [JsonPropertyName("albumName")]
            public string? AlbumName { get; set; }

            [JsonPropertyName("duration")]
            public double Duration { get; set; }

            [JsonPropertyName("instrumental")]
            public bool Instrumental { get; set; }

            [JsonPropertyName("plainLyrics")]
            public string? PlainLyrics { get; set; }

            [JsonPropertyName("syncedLyrics")]
            public string? SyncedLyrics { get; set; }
        }
    }
}
