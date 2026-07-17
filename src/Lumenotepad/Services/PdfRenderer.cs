using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Lumenotepad.Services;

/// <summary>Renders PDF pages to bitmaps for the in-app viewer/annotator (M11) via native PDFium
/// (the PDFtoImage wrapper over bblanchon's pdfium binaries) — NOT a browser engine, per the app's
/// hard no-web-runtime rule. Pages render to SkiaSharp then cross into an Avalonia bitmap. Every
/// call is defensive: a corrupt/locked/encrypted PDF yields null or an empty list, never a crash.</summary>
public static class PdfRenderer
{
    /// <summary>Number of pages, or 0 if the bytes aren't a readable PDF.</summary>
    public static int PageCount(byte[] pdf)
    {
        try { return PDFtoImage.Conversion.GetPageCount(pdf); }
        catch { return 0; }
    }

    /// <summary>Each page's size in POINTS (72/inch), or an empty list on failure.</summary>
    public static IReadOnlyList<(double Width, double Height)> PageSizes(byte[] pdf)
    {
        try
        {
            var result = new List<(double, double)>();
            foreach (var s in PDFtoImage.Conversion.GetPageSizes(pdf))
                result.Add((s.Width, s.Height));
            return result;
        }
        catch { return Array.Empty<(double, double)>(); }
    }

    /// <summary>Render one page (0-based) at the given DPI to an Avalonia bitmap; null on failure.</summary>
    public static Bitmap? RenderPage(byte[] pdf, int page, float dpi = 110f)
    {
        try
        {
            using var skbmp = PDFtoImage.Conversion.ToImage(
                pdf, page: page, options: new PDFtoImage.RenderOptions(Dpi: (int)dpi));
            using var image = SkiaSharp.SKImage.FromBitmap(skbmp);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 92);
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    /// <summary>Render one page (0-based) at the given DPI straight to a SkiaSharp bitmap — for the
    /// flatten/export path, which draws the page + baked annotations into a fresh SkiaSharp PDF. The
    /// caller owns (and disposes) the returned bitmap; null on failure.</summary>
    public static SkiaSharp.SKBitmap? RenderPageSk(byte[] pdf, int page, float dpi = 150f)
    {
        try
        {
            return PDFtoImage.Conversion.ToImage(
                pdf, page: page, options: new PDFtoImage.RenderOptions(Dpi: (int)dpi));
        }
        catch { return null; }
    }
}
