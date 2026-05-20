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

        public async Task<BitmapImage?> GetArtwork(Track track, int size)
        {
            // placeholder check
            ArtKey key = new(track.Id, size);
            if (placeholderItems.Contains(key)) return null;

            // if present in cache
            if (cache.TryGetValue(key, out var cachedItem))
            {
                return cachedItem.Data;
            }

            // if it has embedded art, also adds to cache
            var embeddedImage = await getEmbeddedArtwork(track.FilePath, size);
            if (embeddedImage != null)
            {
                addToCache(key, new(track.Id, size, embeddedImage));
                return embeddedImage;
            }

            // if folder has cover.avif, adds to cache
            string? avifPath = checkCompanionFileExists(track.FilePath, "cover.avif");
            if (avifPath != null)
            {
                var avifImage = await getImage(avifPath, size);
                if (avifImage != null)
                {
                    addToCache(key, new(track.Id, size, avifImage));
                    return avifImage;
                }
            }

            // if folder has cover.png, adds to cache
            string? pngPath = checkCompanionFileExists(track.FilePath, "cover.png");
            if (pngPath != null)
            {
                var pngImage = await getImage(pngPath, size);
                if (pngImage != null)
                {
                    addToCache(key, new(track.Id, size, pngImage));
                    return pngImage;
                }
            }

            // if folder has cover.jpg, adds to cache
            string? jpgPath = checkCompanionFileExists(track.FilePath, "cover.jpg");
            if (jpgPath != null)
            {
                var jpgImage = await getImage(jpgPath, size);
                if (jpgImage != null)
                {
                    addToCache(key, new(track.Id, size, jpgImage));
                    return jpgImage;
                }
            }

            // add to placeholderItems so it knows not to scan again
            placeholderItems.Add(key);
            return null;
        }
        private async Task<BitmapImage?> getEmbeddedArtwork(string path, int size)
        {
            using (var file = TagLib.File.Create(path))
            {
                var pic = file.Tag.Pictures?.FirstOrDefault();
                if (pic == null) return null;

                var bitmap = new BitmapImage();

                
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = size;
                bitmap.DecodePixelHeight = size;

                using (var memoryStream = new MemoryStream(pic.Data.Data))
                {
                    await bitmap.SetSourceAsync(memoryStream.AsRandomAccessStream());
                }

                return bitmap;
            }
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
            cache.Add(key, item);
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
