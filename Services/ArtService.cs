using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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

    public class ArtService
    {
        public static readonly ArtService Instance = new();
        public static readonly int CacheMemoryLimit = 100 * 1024 * 1024;

        private readonly Dictionary<ArtKey, ArtItem> cache = new();
        private readonly LinkedList<ArtKey> lruList = new();
        private readonly HashSet<ArtKey> placeholderItems = new();
        private readonly Dictionary<ArtKey, int> refCounts = new();
        private int currentCacheBytes = 0;

        private readonly SemaphoreSlim _ioSemaphore = new(2, 2);

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

                List<Track> tracks = new();
                foreach (var trackId in album.TrackIds)
                {
                    if (token.IsCancellationRequested) return null;
                    var track = await LibraryService.Instance.GetTrack(trackId);
                    if (track != null)
                    {
                        tracks.Add(track);
                    }
                }

                if (tracks.Count == 0 || token.IsCancellationRequested) return null;

                var firstTrack = tracks[0];
                var absolutePath = LibraryService.Instance.PathToAbsolute(firstTrack.FilePath);
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

                foreach (var track in tracks)
                {
                    if (token.IsCancellationRequested) return null;
                    var path = LibraryService.Instance.PathToAbsolute(track.FilePath);
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

        public async Task<RandomAccessStreamReference?> GetArtworkStreamReference(Track track, CancellationToken token = default)
        {
            var absolutePath = LibraryService.Instance.PathToAbsolute(track.FilePath);
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

            var absolutePath = LibraryService.Instance.PathToAbsolute(filePath);

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

                if (token.IsCancellationRequested) return null;

                string? avifPath = checkCompanionFilePath(absolutePath, "cover.avif");
                if (avifPath != null)
                {
                    if (token.IsCancellationRequested) return null;
                    var avifImage = await loadAndDecodeFile(avifPath, size, token);
                    if (avifImage != null)
                    {
                        addToCache(key, new(id, size, avifImage));
                        return avifImage;
                    }
                }

                if (token.IsCancellationRequested) return null;

                string? pngPath = checkCompanionFilePath(absolutePath, "cover.png");
                if (pngPath != null)
                {
                    if (token.IsCancellationRequested) return null;
                    var pngImage = await loadAndDecodeFile(pngPath, size, token);
                    if (pngImage != null)
                    {
                        addToCache(key, new(id, size, pngImage));
                        return pngImage;
                    }
                }

                if (token.IsCancellationRequested) return null;

                string? jpgPath = checkCompanionFilePath(absolutePath, "cover.jpg");
                if (jpgPath != null)
                {
                    if (token.IsCancellationRequested) return null;
                    var jpgImage = await loadAndDecodeFile(jpgPath, size, token);
                    if (jpgImage != null)
                    {
                        addToCache(key, new(id, size, jpgImage));
                        return jpgImage;
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
                TaskService.Instance.Dispatcher.TryEnqueue(async () =>
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
                Debug.WriteLine($"Failed to decode software bitmap: {ex.Message}");
                return null;
            }
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

                while ((currentCacheBytes > CacheMemoryLimit || cache.Count > 500) && lruList.Count > 0)
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
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
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
