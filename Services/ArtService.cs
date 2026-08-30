using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Moonrise.Services
{
    public readonly record struct ArtKey(string Id, int Size);
    public record ArtItem(string Id, int Size, ImageSource Data)
    {
        public int ByteSize => Size * Size * 4;
    }

    public interface IArtService
    {
        Task<ImageSource?> GetArtwork(Track track, int size, CancellationToken token = default);
        Task<ImageSource?> GetArtwork(QueueTrack track, int size, CancellationToken token = default);
        Task<ImageSource?> GetArtwork(Album album, int size, CancellationToken token = default);
        Task<ImageSource?> GetArtwork(Artist artist, int size, CancellationToken token = default);
        Task<ImageSource?> GetArtwork(Playlist playlist, int size, CancellationToken token = default);
        Task<RandomAccessStreamReference?> GetArtworkStreamReference(Track track, CancellationToken token = default);
        Task<SoftwareBitmap?> GetArtworkBitmap(Track track, int size, CancellationToken token = default);
        ImageSource? GetCachedArtwork(string id, int size);
        void AcquireArtwork(ArtKey key, ImageSource data);
        void ReleaseArtwork(ArtKey key, ImageSource data);
        void ClearCache();
    }

    public class ArtService : IArtService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static ArtService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Moonrise/1.0");
        }

        public static readonly int CacheMemoryLimit = 50 * 1024 * 1024; // 50 mb
        public static readonly int CacheItemLimit = 2000; // 2000 items

        private readonly Dictionary<ArtKey, ArtItem> cache = new();
        private readonly LinkedList<ArtKey> lruList = new();
        private readonly HashSet<ArtKey> placeholderItems = new();
        private readonly Dictionary<ArtKey, int> refCounts = new();
        private int currentCacheBytes = 0;

        private readonly SemaphoreSlim _ioSemaphore = new(2, 2);
        private readonly ITaskService _taskService;
        private ILibraryService library => App.Services.GetRequiredService<ILibraryService>();

        public ArtService(ITaskService taskService)
        {
            _taskService = taskService;
        }

        public Task<ImageSource?> GetArtwork(Track track, int size, CancellationToken token = default) => 
            GetArtworkInternal(track.Id, track.FilePath, size, token);
            
        public Task<ImageSource?> GetArtwork(QueueTrack track, int size, CancellationToken token = default) => 
            GetArtworkInternal(track.Id, track.FilePath, size, token);

        public async Task<ImageSource?> GetArtwork(Album album, int size, CancellationToken token = default)
        {
            ArtKey key = new(album.Id, size);
            lock (cache)
            {
                if (placeholderItems.Contains(key)) return null;

                if (cache.TryGetValue(key, out var cachedItem))
                {
                    lruList.Remove(key);
                    lruList.AddLast(key);
                    return cachedItem.Data;
                }
            }

            try
            {
                await _ioSemaphore.WaitAsync();
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                if (token.IsCancellationRequested) return null;

                Track? firstTrack = null;
                foreach (var trackId in album.TrackIds)
                {
                    if (token.IsCancellationRequested) return null;
                    firstTrack = await library.GetTrack(trackId);
                    if (firstTrack != null) break;
                }

                if (firstTrack == null || token.IsCancellationRequested) return null;

                var absolutePath = library.PathToAbsolute(firstTrack.FilePath);
                var dir = Path.GetDirectoryName(absolutePath);

                if (!string.IsNullOrEmpty(dir))
                {
                    string? foundCoverPath = null;
                    await Task.Run(() =>
                    {
                        foreach (var name in new[] { "cover.avif", "cover.png", "cover.jpg", "cover.jpeg" })
                        {
                            if (token.IsCancellationRequested) return;
                            var path = Path.Combine(dir, name);
                            if (File.Exists(path))
                            {
                                foundCoverPath = path;
                                return;
                            }
                        }
                    });

                    if (token.IsCancellationRequested) return null;

                    if (foundCoverPath != null)
                    {
                        var image = await loadAndDecodeFile(foundCoverPath, size, token);
                        if (image != null)
                        {
                            addToCache(key, new(album.Id, size, image));
                            return image;
                        }
                    }
                }

                foreach (var trackId in album.TrackIds)
                {
                    if (token.IsCancellationRequested) return null;
                    var track = await library.GetTrack(trackId);
                    if (track == null) continue;
                    var path = library.PathToAbsolute(track.FilePath);
                    var embeddedImage = await getEmbeddedArtwork(path, size, token);
                    if (embeddedImage != null)
                    {
                        addToCache(key, new(album.Id, size, embeddedImage));
                        return embeddedImage;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
            }
            finally
            {
                _ioSemaphore.Release();
            }

            lock (cache)
            {
                placeholderItems.Add(key);
            }
            return null;
        }

        public async Task<ImageSource?> GetArtwork(Artist artist, int size, CancellationToken token = default)
        {
            if (artist == null) return null;

            ArtKey key = new(artist.Id, size);
            lock (cache)
            {
                if (placeholderItems.Contains(key)) return null;

                if (cache.TryGetValue(key, out var cachedItem))
                {
                    lruList.Remove(key);
                    lruList.AddLast(key);
                    return cachedItem.Data;
                }
            }

            try
            {
                await _ioSemaphore.WaitAsync(token);
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                if (token.IsCancellationRequested) return null;

                var artistCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moonrise", "Cache", "Artists");
                var diskPath = Path.Combine(artistCacheDir, $"{artist.Id}.jpg");

                if (File.Exists(diskPath))
                {
                    var cachedImage = await loadAndDecodeFile(diskPath, size, token);
                    if (cachedImage != null)
                    {
                        addToCache(key, new(artist.Id, size, cachedImage));
                        return cachedImage;
                    }
                }

                if (!string.IsNullOrWhiteSpace(artist.Name) && !artist.Name.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                {
                    var artworkUrl = await fetchArtistImageUrl(artist.Name, token);

                    if (!string.IsNullOrEmpty(artworkUrl))
                    {
                        int targetDim = Math.Max(size, 600);
                        artworkUrl = Regex.Replace(artworkUrl, @"/\d+x\d+[^/?#]*", $"/{targetDim}x{targetDim}bb.jpg");

                        var imageBytes = await _httpClient.GetByteArrayAsync(artworkUrl, token);
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            try
                            {
                                Directory.CreateDirectory(artistCacheDir);
                                await File.WriteAllBytesAsync(diskPath, imageBytes, token);
                            }
                            catch { }

                            if (token.IsCancellationRequested) return null;

                            var image = await decodeToSoftwareBitmapSource(imageBytes, size, token);
                            if (image != null)
                            {
                                addToCache(key, new(artist.Id, size, image));
                                return image;
                            }
                        }
                    }
                }

                var localImage = await getLocalArtistArtwork(artist, size, token);
                if (localImage != null)
                {
                    addToCache(key, new(artist.Id, size, localImage));
                    return localImage;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
            }
            finally
            {
                _ioSemaphore.Release();
            }

            lock (cache)
            {
                placeholderItems.Add(key);
            }
            return null;
        }

        private async Task<ImageSource?> getLocalArtistArtwork(Artist artist, int size, CancellationToken token)
        {
            if (artist.AlbumIds == null || artist.AlbumIds.Length == 0) return null;

            foreach (var albumId in artist.AlbumIds)
            {
                if (token.IsCancellationRequested) return null;
                var album = await library.GetAlbum(albumId);
                if (album == null || album.TrackIds == null || album.TrackIds.Length == 0) continue;

                Track? firstTrack = null;
                foreach (var trackId in album.TrackIds)
                {
                    if (token.IsCancellationRequested) return null;
                    firstTrack = await library.GetTrack(trackId);
                    if (firstTrack != null) break;
                }

                if (firstTrack != null)
                {
                    var absolutePath = library.PathToAbsolute(firstTrack.FilePath);
                    var dir = Path.GetDirectoryName(absolutePath);

                    if (!string.IsNullOrEmpty(dir))
                    {
                        string? foundCoverPath = null;
                        await Task.Run(() =>
                        {
                            foreach (var name in new[] { "cover.avif", "cover.png", "cover.jpg", "cover.jpeg" })
                            {
                                if (token.IsCancellationRequested) return;
                                var path = Path.Combine(dir, name);
                                if (File.Exists(path))
                                {
                                    foundCoverPath = path;
                                    return;
                                }
                            }
                        });

                        if (token.IsCancellationRequested) return null;

                        if (foundCoverPath != null)
                        {
                            var image = await loadAndDecodeFile(foundCoverPath, size, token);
                            if (image != null) return image;
                        }
                    }

                    foreach (var trackId in album.TrackIds)
                    {
                        if (token.IsCancellationRequested) return null;
                        var track = await library.GetTrack(trackId);
                        if (track == null) continue;
                        var path = library.PathToAbsolute(track.FilePath);
                        var embeddedImage = await getEmbeddedArtwork(path, size, token);
                        if (embeddedImage != null) return embeddedImage;
                    }
                }
            }

            return null;
        }

        private async Task<string?> fetchArtistImageUrl(string artistName, CancellationToken token)
        {
            try
            {
                var artistSearchUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(artistName)}&entity=musicArtist&limit=1";
                using var response = await _httpClient.GetAsync(artistSearchUrl, token);
                string? artistPageUrl = null;

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        if (first.TryGetProperty("artistLinkUrl", out var linkUrl))
                        {
                            artistPageUrl = linkUrl.GetString();
                        }
                        else if (first.TryGetProperty("artistViewUrl", out var viewUrl))
                        {
                            artistPageUrl = viewUrl.GetString();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(artistPageUrl))
                {
                    try
                    {
                        using var pageResponse = await _httpClient.GetAsync(artistPageUrl, token);
                        if (pageResponse.IsSuccessStatusCode)
                        {
                            var html = await pageResponse.Content.ReadAsStringAsync(token);
                            var match = Regex.Match(html, @"<meta\s+[^>]*property=[""']og:image[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                            if (!match.Success)
                            {
                                match = Regex.Match(html, @"<meta\s+[^>]*content=[""']([^""']+)[""'][^>]*property=[""']og:image[""']", RegexOptions.IgnoreCase);
                            }

                            if (match.Success)
                            {
                                var imgUrl = WebUtility.HtmlDecode(match.Groups[1].Value);
                                if (!string.IsNullOrEmpty(imgUrl))
                                {
                                    return imgUrl;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch artist image URL: {ex.Message}");
            }

            return null;
        }

        public async Task<ImageSource?> GetArtwork(Playlist playlist, int size, CancellationToken token = default)
        {
            ArtKey key = new(playlist.Id, size);
            lock (cache)
            {
                if (placeholderItems.Contains(key)) return null;

                if (cache.TryGetValue(key, out var cachedItem))
                {
                    lruList.Remove(key);
                    lruList.AddLast(key);
                    return cachedItem.Data;
                }
            }

            if (playlist.TrackIds == null || playlist.TrackIds.Length == 0)
                return null;

            if (playlist.TrackIds.Length == 1)
            {
                var track = await library.GetTrack(playlist.TrackIds[0]);
                if (track == null || token.IsCancellationRequested) return null;
                var singleArt = await GetArtwork(track, size, token);
                if (singleArt != null)
                {
                    addToCache(key, new(playlist.Id, size, singleArt));
                }
                return singleArt;
            }

            try
            {
                if (token.IsCancellationRequested) return null;

                var trackIdsToLoad = playlist.TrackIds.Take(4).ToList();
                var tracks = new List<Track>();
                foreach (var tid in trackIdsToLoad)
                {
                    if (token.IsCancellationRequested) return null;
                    var t = await library.GetTrack(tid);
                    if (t != null) tracks.Add(t);
                }

                if (tracks.Count == 0 || token.IsCancellationRequested) return null;

                if (tracks.Count == 1)
                {
                    var singleArt = await GetArtwork(tracks[0], size, token);
                    if (singleArt != null)
                    {
                        addToCache(key, new(playlist.Id, size, singleArt));
                    }
                    return singleArt;
                }

                int halfSize = size / 2;
                var bitmaps = new List<SoftwareBitmap?>();

                for (int i = 0; i < 4; i++)
                {
                    if (i < tracks.Count)
                    {
                        if (token.IsCancellationRequested) break;
                        var bmp = await GetArtworkBitmap(tracks[i], halfSize, token);
                        bitmaps.Add(bmp);
                    }
                    else
                    {
                        bitmaps.Add(null);
                    }
                }

                if (token.IsCancellationRequested)
                {
                    foreach (var b in bitmaps) b?.Dispose();
                    return null;
                }

                bool hasAnyArt = bitmaps.Any(b => b != null);
                if (!hasAnyArt)
                {
                    foreach (var b in bitmaps) b?.Dispose();
                    lock (cache)
                    {
                        placeholderItems.Add(key);
                    }
                    return null;
                }

                byte[] compositePixels = new byte[size * size * 4];
                int halfW = size / 2;
                int halfH = size / 2;

                (int startX, int startY, int w, int h)[] quadrants = new[]
                {
                    (0, 0, halfW, halfH),
                    (halfW, 0, size - halfW, halfH),
                    (0, halfH, halfW, size - halfH),
                    (halfW, halfH, size - halfW, size - halfH)
                };

                for (int i = 0; i < 4; i++)
                {
                    var bmp = bitmaps[i];
                    if (bmp != null)
                    {
                        try
                        {
                            int bmpW = bmp.PixelWidth;
                            int bmpH = bmp.PixelHeight;
                            byte[] srcPixels = new byte[bmpW * bmpH * 4];
                            bmp.CopyToBuffer(srcPixels.AsBuffer());

                            var (startX, startY, qW, qH) = quadrants[i];
                            int copyH = Math.Min(qH, bmpH);
                            int copyW = Math.Min(qW, bmpW);

                            for (int y = 0; y < copyH; y++)
                            {
                                int srcRowOffset = y * bmpW * 4;
                                int dstRowOffset = ((startY + y) * size + startX) * 4;
                                System.Buffer.BlockCopy(srcPixels, srcRowOffset, compositePixels, dstRowOffset, copyW * 4);
                            }
                        }
                        finally
                        {
                            bmp.Dispose();
                        }
                    }
                }

                if (token.IsCancellationRequested) return null;

                var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);
                softwareBitmap.CopyFromBuffer(compositePixels.AsBuffer());

                var tcs = new TaskCompletionSource<ImageSource?>();
                _taskService.Dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            softwareBitmap.Dispose();
                            tcs.SetResult(null);
                            return;
                        }
                        var source = new SoftwareBitmapSource();
                        await source.SetBitmapAsync(softwareBitmap);
                        tcs.SetResult(source);
                    }
                    catch (Exception ex)
                    {
                        softwareBitmap.Dispose();
                        tcs.SetException(ex);
                    }
                });

                var image = await tcs.Task;
                if (image != null)
                {
                    addToCache(key, new(playlist.Id, size, image));
                    return image;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
            }

            lock (cache)
            {
                placeholderItems.Add(key);
            }
            return null;
        }

        public async Task<RandomAccessStreamReference?> GetArtworkStreamReference(Track track, CancellationToken token = default)
        {
            var absolutePath = library.PathToAbsolute(track.FilePath);
            try
            {
                var pictureData = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return null;
                    using var file = TagLib.File.Create(absolutePath);
                    var pic = file.Tag.Pictures?.FirstOrDefault();
                    return pic?.Data.Data;
                });

                if (pictureData != null && !token.IsCancellationRequested)
                {
                    var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(pictureData.AsBuffer());
                    stream.Seek(0);
                    return RandomAccessStreamReference.CreateFromStream(stream);
                }

                foreach (var name in new[] { "cover.avif", "cover.png", "cover.jpg", "cover.jpeg" })
                {
                    var companionPath = checkCompanionFilePath(absolutePath, name);
                    if (companionPath != null && File.Exists(companionPath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(companionPath);
                        return RandomAccessStreamReference.CreateFromFile(file);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private async Task<ImageSource?> GetArtworkInternal(string id, string filePath, int size, CancellationToken token)
        {
            ArtKey key = new(id, size);
            lock (cache)
            {
                if (placeholderItems.Contains(key)) return null;

                if (cache.TryGetValue(key, out var cachedItem))
                {
                    lruList.Remove(key);
                    lruList.AddLast(key);
                    return cachedItem.Data;
                }
            }

            var absolutePath = library.PathToAbsolute(filePath);

            try
            {
                await _ioSemaphore.WaitAsync();
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                if (token.IsCancellationRequested) return null;

                var embeddedImage = await getEmbeddedArtwork(absolutePath, size, token);
                if (embeddedImage != null)
                {
                    addToCache(key, new(id, size, embeddedImage));
                    return embeddedImage;
                }

                foreach (var name in new[] { "cover.avif", "cover.png", "cover.jpg", "cover.jpeg" })
                {
                    if (token.IsCancellationRequested) return null;
                    string? companionPath = checkCompanionFilePath(absolutePath, name);
                    if (companionPath != null)
                    {
                        var image = await loadAndDecodeFile(companionPath, size, token);
                        if (image != null)
                        {
                            addToCache(key, new(id, size, image));
                            return image;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
            }
            finally
            {
                _ioSemaphore.Release();
            }

            lock (cache)
            {
                placeholderItems.Add(key);
            }
            return null;
        }

        private async Task<ImageSource?> getEmbeddedArtwork(string path, int size, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;
            var pictureData = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return null;
                using var file = TagLib.File.Create(path);
                var pic = file.Tag.Pictures?.FirstOrDefault();
                return pic?.Data.Data;
            });

            if (pictureData == null || token.IsCancellationRequested) return null;

            return await decodeToSoftwareBitmapSource(pictureData, size, token);
        }

        private async Task<ImageSource?> decodeToSoftwareBitmapSource(byte[] pictureData, int size, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return null;

                var softwareBitmap = await Task.Run(async () =>
                {
                    if (token.IsCancellationRequested) return null;
                    using var memoryStream = new MemoryStream(pictureData);
                    using var randomAccessStream = memoryStream.AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)size,
                        ScaledHeight = (uint)size,
                        InterpolationMode = BitmapInterpolationMode.Linear
                    };
                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.ColorManageToSRgb);
                    if (token.IsCancellationRequested) return null;
                    var bmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);
                    bmp.CopyFromBuffer(pixelData.DetachPixelData().AsBuffer());
                    return bmp;
                });

                if (token.IsCancellationRequested || softwareBitmap == null)
                {
                    softwareBitmap?.Dispose();
                    return null;
                }

                var tcs = new TaskCompletionSource<ImageSource?>();
                _taskService.Dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            softwareBitmap.Dispose();
                            tcs.SetResult(null);
                            return;
                        }
                        var source = new SoftwareBitmapSource();
                        await source.SetBitmapAsync(softwareBitmap);
                        tcs.SetResult(source);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to decode artwork: {ex.Message}");
                return null;
            }
        }

        private async Task<SoftwareBitmap?> decodeToBitmap(byte[] pictureData, int size, CancellationToken token)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    if (token.IsCancellationRequested) return null;
                    using var memoryStream = new MemoryStream(pictureData);
                    using var randomAccessStream = memoryStream.AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)size,
                        ScaledHeight = (uint)size,
                        InterpolationMode = BitmapInterpolationMode.Linear
                    };
                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.ColorManageToSRgb);
                    if (token.IsCancellationRequested) return null;
                    var bmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);
                    bmp.CopyFromBuffer(pixelData.DetachPixelData().AsBuffer());
                    return bmp;
                });
            }
            catch
            {
                return null;
            }
        }

        public async Task<SoftwareBitmap?> GetArtworkBitmap(Track track, int size, CancellationToken token = default)
        {
            var absolutePath = library.PathToAbsolute(track.FilePath);
            try
            {
                await _ioSemaphore.WaitAsync(token);
            }
            catch
            {
                return null;
            }
            try
            {
                if (token.IsCancellationRequested) return null;

                var pictureData = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return null;
                    using var file = TagLib.File.Create(absolutePath);
                    var pic = file.Tag.Pictures?.FirstOrDefault();
                    return pic?.Data.Data;
                });

                if (pictureData != null && !token.IsCancellationRequested)
                {
                    var bmp = await decodeToBitmap(pictureData, size, token);
                    if (bmp != null) return bmp;
                }

                foreach (var name in new[] { "cover.avif", "cover.png", "cover.jpg", "cover.jpeg" })
                {
                    if (token.IsCancellationRequested) return null;
                    var companionPath = checkCompanionFilePath(absolutePath, name);
                    if (companionPath != null)
                    {
                        var data = await Task.Run(() =>
                        {
                            if (token.IsCancellationRequested) return null;
                            return File.Exists(companionPath) ? File.ReadAllBytes(companionPath) : null;
                        });
                        if (data != null && !token.IsCancellationRequested)
                        {
                            var bmp = await decodeToBitmap(data, size, token);
                            if (bmp != null) return bmp;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                _ioSemaphore.Release();
            }
            return null;
        }

        private async Task<ImageSource?> loadAndDecodeFile(string path, int size, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return null;
                byte[]? data = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return null;
                    if (File.Exists(path))
                    {
                        return File.ReadAllBytes(path);
                    }
                    return null;
                });

                if (data == null || token.IsCancellationRequested) return null;

                return await decodeToSoftwareBitmapSource(data, size, token);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void addToCache(ArtKey key, ArtItem item)
        {
            lock (cache)
            {
                if (cache.TryGetValue(key, out var existingItem))
                {
                    currentCacheBytes -= existingItem.ByteSize;
                    cache[key] = item;
                    lruList.Remove(key);
                    ReleaseArtwork(key, existingItem.Data);
                }
                else
                {
                    cache.Add(key, item);
                }

                lruList.AddLast(key);
                currentCacheBytes += item.ByteSize;
                AcquireArtwork(key, item.Data);

                while ((currentCacheBytes > CacheMemoryLimit || cache.Count > CacheItemLimit) && lruList.Count > 0)
                {
                    var oldestKey = lruList.First!.Value;

                    if (cache.TryGetValue(oldestKey, out var evictedItem))
                    {
                        currentCacheBytes -= evictedItem.ByteSize;
                        cache.Remove(oldestKey);
                        ReleaseArtwork(oldestKey, evictedItem.Data);
                    }

                    lruList.RemoveFirst();
                }
            }
        }

        public void AcquireArtwork(ArtKey key, ImageSource data)
        {
            lock (cache)
            {
                refCounts[key] = refCounts.GetValueOrDefault(key) + 1;
            }
        }

        public void ReleaseArtwork(ArtKey key, ImageSource data)
        {
            lock (cache)
            {
                int newCount = refCounts.GetValueOrDefault(key) - 1;
                if (newCount <= 0)
                {
                    refCounts.Remove(key);
                }
                else
                {
                    refCounts[key] = newCount;
                }
            }
        }

        public void ClearCache()
        {
            lock (cache)
            {
                foreach (var item in cache.Values)
                {
                    ReleaseArtwork(new ArtKey(item.Id, item.Size), item.Data);
                }
                cache.Clear();
                lruList.Clear();
                placeholderItems.Clear();
                currentCacheBytes = 0;
            }
        }

        public ImageSource? GetCachedArtwork(string id, int size)
        {
            lock (cache)
            {
                if (cache.TryGetValue(new ArtKey(id, size), out var item))
                {
                    return item.Data;
                }
                return null;
            }
        }

        private static string? checkCompanionFilePath(string originalFilePath, string targetFileName)
        {
            string? parentFolder = Path.GetDirectoryName(originalFilePath);

            if (string.IsNullOrEmpty(parentFolder))
            {
                return null;
            }

            return Path.Combine(parentFolder, targetFileName);
        }
    }
}
