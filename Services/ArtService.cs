using Microsoft.UI.Xaml.Media.Imaging;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Moonrise.Services
{
    public readonly record struct ArtKey(string Id, int Size);
    public record ArtItem(string Id, int Size, BitmapImage Data)
    {
        public int ByteSize => Size * Size * 4;
    }

    public class ArtService
    {
        public static readonly ArtService Instance = new();
        public static readonly int CacheMemoryLimit = 80 * 1024 * 1024; // 80mb

        Dictionary<ArtKey, ArtItem> cache = new();
        LinkedList<ArtKey> lruList = new();

        HashSet<ArtKey> placeholderItems = new();
        int currentCacheBytes = 0;

        public Task<BitmapImage?> GetArtwork(Track track, int size) => GetArtworkInternal(track.Id, track.FilePath, size);
        public Task<BitmapImage?> GetArtwork(QueueTrack track, int size) => GetArtworkInternal(track.Id, track.FilePath, size);

        private async Task<BitmapImage?> GetArtworkInternal(string id, string filePath, int size)
        {
            // placeholder check
            ArtKey key = new(id, size);
            if (placeholderItems.Contains(key)) return null;

            // if present in cache
            if (cache.TryGetValue(key, out var cachedItem))
            {
                lruList.Remove(key);
                lruList.AddLast(key);
                return cachedItem.Data;
            }

            // if it has embedded art, also adds to cache
            var embeddedImage = await getEmbeddedArtwork(filePath, size);
            if (embeddedImage != null)
            {
                addToCache(key, new(id, size, embeddedImage));
                return embeddedImage;
            }

            // if folder has cover.avif, adds to cache
            string? avifPath = checkCompanionFileExists(filePath, "cover.avif");
            if (avifPath != null)
            {
                var avifImage = await getImage(avifPath, size);
                if (avifImage != null)
                {
                    addToCache(key, new(id, size, avifImage));
                    return avifImage;
                }
            }

            // if folder has cover.png, adds to cache
            string? pngPath = checkCompanionFileExists(filePath, "cover.png");
            if (pngPath != null)
            {
                var pngImage = await getImage(pngPath, size);
                if (pngImage != null)
                {
                    addToCache(key, new(id, size, pngImage));
                    return pngImage;
                }
            }

            // if folder has cover.jpg, adds to cache
            string? jpgPath = checkCompanionFileExists(filePath, "cover.jpg");
            if (jpgPath != null)
            {
                var jpgImage = await getImage(jpgPath, size);
                if (jpgImage != null)
                {
                    addToCache(key, new(id, size, jpgImage));
                    return jpgImage;
                }
            }

            // add to placeholderItems so it knows not to scan again
            placeholderItems.Add(key);
            return null;
        }
        private async Task<BitmapImage?> getEmbeddedArtwork(string path, int size)
        {
            var pictureData = await Task.Run(() =>
            {
                using var file = TagLib.File.Create(path);
                var pic = file.Tag.Pictures?.FirstOrDefault();
                return pic?.Data.Data;
            });

            if (pictureData == null) return null;

            var bitmap = new BitmapImage();
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = size;
            bitmap.DecodePixelHeight = size;

            using var memoryStream = new MemoryStream(pictureData);
            await bitmap.SetSourceAsync(memoryStream.AsRandomAccessStream());

            return bitmap;
        }
        public async Task<BitmapImage?> getImage(string path, int size)
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();

            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = size;
            bitmap.DecodePixelHeight = size;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    await bitmap.SetSourceAsync(fs.AsRandomAccessStream());
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"failed to decode image at {path}: {ex.Message}");
                return null;
            }
        }
        private void addToCache(ArtKey key, ArtItem item)
        {
            if (cache.TryGetValue(key, out var existingItem))
            {
                currentCacheBytes -= existingItem.ByteSize;
                cache[key] = item;
                
                lruList.Remove(key);
            }
            else
            {
                cache.Add(key, item);
            }
            
            lruList.AddLast(key);
            currentCacheBytes += item.ByteSize;

            while (currentCacheBytes > CacheMemoryLimit && lruList.Count > 0)
            {
                var oldestKey = lruList.First!.Value;

                if (cache.TryGetValue(oldestKey, out var evictedItem))
                {
                    currentCacheBytes -= evictedItem.ByteSize;
                    cache.Remove(oldestKey);
                }

                lruList.RemoveFirst();
            }
        }
        //public async Task<BitmapImage> GetArtwork(Album album)
        //{
        //TODO: add embedded track tag scanning
        //}

        private static string? checkCompanionFileExists(string originalFilePath, string targetFileName)
        {
            string? parentFolder = Path.GetDirectoryName(originalFilePath);

            if (string.IsNullOrEmpty(parentFolder))
            {
                return null;
            }

            string specificFilePath = Path.Combine(parentFolder, targetFileName);

            if (File.Exists(specificFilePath)) return specificFilePath;
            return null;
        }
    }
}
