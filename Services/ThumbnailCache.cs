using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace PlumbobForge.Desktop.Services;

/// <summary>
/// Thread-safe single-decode persistent memory cache for package thumbnails.
/// Decodes each thumbnail to 140px color and grayscale versions.
/// Zero allocations, zero disk reads, and zero disposes during scrolling.
/// </summary>
public static class ThumbnailCache
{
    private static readonly ConcurrentDictionary<long, Bitmap> _bitmaps = new();
    private static readonly ConcurrentDictionary<long, Bitmap> _grayscaleBitmaps = new();
    private static readonly ConcurrentDictionary<long, Task<(Bitmap? Color, Bitmap? Grayscale)>> _inFlight = new();

    public static Bitmap? TryGet(long itemId, bool grayscale = false)
    {
        if (grayscale)
        {
            return _grayscaleBitmaps.TryGetValue(itemId, out var gBmp) ? gBmp : null;
        }
        return _bitmaps.TryGetValue(itemId, out var bmp) ? bmp : null;
    }

    public static async Task<Bitmap?> GetOrLoadAsync(long itemId, string filePath, bool grayscale = false)
    {
        if (grayscale && _grayscaleBitmaps.TryGetValue(itemId, out var existingGray))
        {
            return existingGray;
        }
        if (!grayscale && _bitmaps.TryGetValue(itemId, out var existingColor))
        {
            return existingColor;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        var result = await _inFlight.GetOrAdd(itemId, async id =>
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var original = SKBitmap.Decode(filePath);
                        if (original == null) return (null, null);

                        int targetWidth = 320;
                        int targetHeight = (int)((float)original.Height / original.Width * targetWidth);
                        if (targetHeight <= 0) targetHeight = targetWidth;

                        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

                        // 1. Color Bitmap
                        Bitmap? colorBmp = null;
                        using (var colorSurface = SKSurface.Create(info))
                        {
                            if (colorSurface != null)
                            {
                                using var colorPaint = new SKPaint { FilterQuality = SKFilterQuality.High };
                                colorSurface.Canvas.DrawBitmap(original, new SKRect(0, 0, targetWidth, targetHeight), colorPaint);
                                using var colorImage = colorSurface.Snapshot();
                                using var colorData = colorImage.Encode(SKEncodedImageFormat.Png, 95);
                                using var colorMs = new MemoryStream();
                                colorData.SaveTo(colorMs);
                                colorMs.Position = 0;
                                colorBmp = new Bitmap(colorMs);
                            }
                        }

                        // 2. Grayscale Bitmap (Luma weights: 0.2126 R + 0.7152 G + 0.0722 B)
                        Bitmap? grayBmp = null;
                        using (var graySurface = SKSurface.Create(info))
                        {
                            if (graySurface != null)
                            {
                                using var grayPaint = new SKPaint
                                {
                                    ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                                    {
                                        0.2126f, 0.7152f, 0.0722f, 0, 0,
                                        0.2126f, 0.7152f, 0.0722f, 0, 0,
                                        0.2126f, 0.7152f, 0.0722f, 0, 0,
                                        0,       0,       0,       1, 0
                                    }),
                                    FilterQuality = SKFilterQuality.High
                                };
                                graySurface.Canvas.DrawBitmap(original, new SKRect(0, 0, targetWidth, targetHeight), grayPaint);
                                using var grayImage = graySurface.Snapshot();
                                using var grayData = grayImage.Encode(SKEncodedImageFormat.Png, 95);
                                using var grayMs = new MemoryStream();
                                grayData.SaveTo(grayMs);
                                grayMs.Position = 0;
                                grayBmp = new Bitmap(grayMs);
                            }
                        }

                        return (colorBmp, grayBmp);
                    }
                    catch
                    {
                        return (null, null);
                    }
                });
            }
            finally
            {
                _inFlight.TryRemove(id, out _);
            }
        });

        if (result.Color != null)
        {
            _bitmaps[itemId] = result.Color;
        }
        if (result.Grayscale != null)
        {
            _grayscaleBitmaps[itemId] = result.Grayscale;
        }

        return grayscale ? result.Grayscale : result.Color;
    }

    public static void Invalidate(long itemId)
    {
        if (_bitmaps.TryRemove(itemId, out var bmp))
        {
            try { bmp.Dispose(); } catch { }
        }
        if (_grayscaleBitmaps.TryRemove(itemId, out var gBmp))
        {
            try { gBmp.Dispose(); } catch { }
        }
    }

    public static void Clear()
    {
        foreach (var bmp in _bitmaps.Values)
        {
            try { bmp.Dispose(); } catch { }
        }
        _bitmaps.Clear();

        foreach (var bmp in _grayscaleBitmaps.Values)
        {
            try { bmp.Dispose(); } catch { }
        }
        _grayscaleBitmaps.Clear();
        _inFlight.Clear();
    }
}
