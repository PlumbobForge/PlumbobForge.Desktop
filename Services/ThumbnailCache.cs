using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace PlumbobForge.Desktop.Services;

/// <summary>
/// Thread-safe bounded LRU memory cache for package thumbnails.
/// Decodes each thumbnail to display resolution (~180px) lazily on-demand.
/// Caps memory usage to ~35-45 MB regardless of library size.
/// </summary>
public static class ThumbnailCache
{
    private const int MaxCacheCapacity = 300;
    private const int TargetThumbnailWidth = 180;

    private readonly record struct CacheKey(long ItemId, bool Grayscale);

    private static readonly LruCache<CacheKey, Bitmap> _cache = new(MaxCacheCapacity);
    private static readonly ConcurrentDictionary<CacheKey, Task<Bitmap?>> _inFlight = new();

    public static Bitmap? TryGet(long itemId, bool grayscale = false)
    {
        return _cache.TryGetValue(new CacheKey(itemId, grayscale), out var bmp) ? bmp : null;
    }

    public static async Task<Bitmap?> GetOrLoadAsync(long itemId, string filePath, bool grayscale = false)
    {
        var key = new CacheKey(itemId, grayscale);
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await _inFlight.GetOrAdd(key, async k =>
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var original = SKBitmap.Decode(filePath);
                        if (original == null || original.Width <= 0 || original.Height <= 0)
                        {
                            return null;
                        }

                        int targetWidth = Math.Min(TargetThumbnailWidth, original.Width);
                        int targetHeight = (int)((float)original.Height / original.Width * targetWidth);
                        if (targetHeight <= 0) targetHeight = targetWidth;

                        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using var surface = SKSurface.Create(info);
                        if (surface == null) return null;

                        using var paint = new SKPaint
                        {
                            FilterQuality = SKFilterQuality.Medium
                        };

                        if (k.Grayscale)
                        {
                            // Luma weights: 0.2126 R + 0.7152 G + 0.0722 B
                            paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                            {
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0,       0,       0,       1, 0
                            });
                        }

                        surface.Canvas.DrawBitmap(original, new SKRect(0, 0, targetWidth, targetHeight), paint);

                        // Direct zero-copy into Avalonia WriteableBitmap (no PNG/JPEG re-encoding)
                        var wb = new WriteableBitmap(
                            new PixelSize(targetWidth, targetHeight),
                            new Vector(96, 96),
                            PixelFormat.Bgra8888,
                            AlphaFormat.Premul);

                        using (var fb = wb.Lock())
                        {
                            surface.ReadPixels(info, fb.Address, fb.RowBytes, 0, 0);
                        }

                        _cache.Set(k, wb);
                        return (Bitmap)wb;
                    }
                    catch
                    {
                        return null;
                    }
                });
            }
            finally
            {
                _inFlight.TryRemove(k, out _);
            }
        });
    }

    public static void Invalidate(long itemId)
    {
        if (_cache.Remove(new CacheKey(itemId, false), out var colorBmp))
        {
            try { colorBmp?.Dispose(); } catch { }
        }
        if (_cache.Remove(new CacheKey(itemId, true), out var grayBmp))
        {
            try { grayBmp?.Dispose(); } catch { }
        }
    }

    public static void Clear()
    {
        var bitmaps = _cache.Clear();
        foreach (var bmp in bitmaps)
        {
            try { bmp.Dispose(); } catch { }
        }
        _inFlight.Clear();
    }

    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _map;
        private readonly LinkedList<CacheItem> _list = new();
        private readonly object _lock = new();

        private readonly struct CacheItem
        {
            public readonly TKey Key;
            public readonly TValue Value;
            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        public LruCache(int capacity)
        {
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _list.Remove(node);
                    _list.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
                value = default;
                return false;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existingNode))
                {
                    _list.Remove(existingNode);
                    _map.Remove(key);
                }
                else if (_map.Count >= _capacity)
                {
                    var oldest = _list.Last;
                    if (oldest != null)
                    {
                        _list.RemoveLast();
                        _map.Remove(oldest.Value.Key);
                        if (oldest.Value.Value is IDisposable d)
                        {
                            try { d.Dispose(); } catch { }
                        }
                    }
                }

                var newNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
                _list.AddFirst(newNode);
                _map[key] = newNode;
            }
        }

        public bool Remove(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _list.Remove(node);
                    _map.Remove(key);
                    value = node.Value.Value;
                    return true;
                }
                value = default;
                return false;
            }
        }

        public List<TValue> Clear()
        {
            lock (_lock)
            {
                var values = new List<TValue>(_map.Count);
                foreach (var node in _map.Values)
                {
                    values.Add(node.Value.Value);
                }
                _map.Clear();
                _list.Clear();
                return values;
            }
        }
    }
}
