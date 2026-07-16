using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Lumenotepad.Services;

/// <summary>Renders live previews for the Font Browser (M11): downloads a Google font's regular face
/// once (cached in memory for the session), then draws the user's preview text in it with synthetic
/// bold/italic/underline/strike via SkiaSharp — no Avalonia font-collection churn, so a thousand-row
/// gallery never pollutes the app's real font list. Bytes are fetched lazily per visible row.</summary>
public static class FontPreviewRenderer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, byte[]> ByteCache = new();

    /// <summary>The Google css2 request for a family's plain regular face (one file); the non-browser
    /// user agent makes Google answer with a raw TTF link.</summary>
    public static string RegularCssUrl(string family) =>
        "https://fonts.googleapis.com/css2?family=" +
        Uri.EscapeDataString(family).Replace("%20", "+");

    /// <summary>Download (and cache) a Google family's regular font file bytes; null on failure.</summary>
    public static async Task<byte[]?> GetBytesAsync(string family, CancellationToken ct = default)
    {
        if (ByteCache.TryGetValue(family, out var cached)) return cached;
        try
        {
            var css = await Http.GetStringAsync(RegularCssUrl(family), ct);
            var url = FontInstaller.ParseCssFontUrls(css).FirstOrDefault();
            if (url is null) return null;
            var bytes = await Http.GetByteArrayAsync(url, ct);
            ByteCache[family] = bytes;
            return bytes;
        }
        catch { return null; }
    }

    /// <summary>Render one line of preview text in the given font file to an Avalonia bitmap, with
    /// synthetic styling. <paramref name="pixelHeight"/> is the cap height in device pixels. Returns
    /// null if the bytes aren't a usable font.</summary>
    public static Bitmap? Render(byte[] fontBytes, string text, bool bold, bool italic,
                                 bool underline, bool strike, uint colorArgb, float pixelHeight = 30f)
    {
        if (string.IsNullOrEmpty(text)) text = " ";
        SKTypeface? tf = null;
        try
        {
            using var data = SKData.CreateCopy(fontBytes);
            tf = SKTypeface.FromData(data);
            if (tf is null) return null;

            using var font = new SKFont(tf, pixelHeight) { Embolden = bold, Edging = SKFontEdging.SubpixelAntialias };
            if (italic) font.SkewX = -0.22f;
            using var paint = new SKPaint { Color = new SKColor(colorArgb), IsAntialias = true };

            float width = font.MeasureText(text, out var bounds);
            const float padX = 4f, padY = 6f;
            int w = Math.Max(1, (int)MathF.Ceiling(width + padX * 2 + (italic ? pixelHeight * 0.22f : 0)));
            int h = Math.Max(1, (int)MathF.Ceiling(pixelHeight + padY * 2));

            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            float baseline = padY + pixelHeight;
            canvas.DrawText(text, padX, baseline, font, paint);

            if (underline || strike)
            {
                using var line = new SKPaint
                {
                    Color = new SKColor(colorArgb), IsAntialias = true,
                    StrokeWidth = MathF.Max(1f, pixelHeight * 0.06f),
                };
                if (underline)
                {
                    float uy = baseline + pixelHeight * 0.12f;
                    canvas.DrawLine(padX, uy, padX + width, uy, line);
                }
                if (strike)
                {
                    float sy = baseline - pixelHeight * 0.28f;
                    canvas.DrawLine(padX, sy, padX + width, sy, line);
                }
            }

            using var image = surface.Snapshot();
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            png.SaveTo(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
        finally { tf?.Dispose(); }
    }
}
