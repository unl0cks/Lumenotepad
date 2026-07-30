using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Lumenotepad.Services;

public static class PdfRenderer
{

    public static int PageCount(byte[] pdf)
    {
        try { return PDFtoImage.Conversion.GetPageCount(pdf); }
        catch { return 0; }
    }

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
