using System;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lumenotepad.Views;

public static class Converters
{

    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(name =>
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return (words[0].Length >= 2 ? words[0].Substring(0, 2) : words[0]).ToUpperInvariant();
        return string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    });

    public static readonly IValueConverter HexBrush = new FuncValueConverter<string?, IBrush>(hex =>
        new SolidColorBrush(SafeParse(hex)));

    public static Color Shade(Color c, double f)
    {
        byte Mix(byte ch) => (byte)Math.Clamp(f >= 0 ? ch + (255 - ch) * f : ch * (1 + f), 0, 255);
        return new Color(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static Color SafeParse(string? hex)
    {
        try { return Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#4DA6FF" : hex); }
        catch { return Color.Parse("#4DA6FF"); }
    }

    public static readonly IValueConverter CoverGradient = new FuncValueConverter<string?, IBrush>(hex =>
    {
        var c = SafeParse(hex);
        return new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0.5, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Shade(c, 0.17), 0),
                new GradientStop(c, 0.45),
                new GradientStop(Shade(c, -0.20), 1),
            },
        };
    });

    public static readonly IValueConverter CoverBorder = new FuncValueConverter<string?, IBrush>(hex =>
        new SolidColorBrush(Shade(SafeParse(hex), -0.27)));

    public static readonly IValueConverter GlowShadow = new FuncValueConverter<string?, BoxShadows>(hex =>
    {
        var c = SafeParse(hex);
        return BoxShadows.Parse($"0 0 4 0 #5A{c.R:X2}{c.G:X2}{c.B:X2}");
    });

    private static readonly System.Collections.Generic.Dictionary<string, (DateTime Stamp, IBrush Brush)> CoverCache = new();

    public static readonly IValueConverter CoverImage = new FuncValueConverter<string?, IBrush?>(path =>
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            var stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (CoverCache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Brush;
            using var fs = System.IO.File.OpenRead(path);
            var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, 420);
            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            CoverCache[path] = (stamp, brush);
            return brush;
        }
        catch { return null; }
    });

    public static readonly IValueConverter NotebookStats = new FuncValueConverter<Models.Notebook?, string>(nb =>
    {
        if (nb is null) return "";
        int secs = nb.Sections.Count;
        int pages = nb.Sections.Sum(s => s.Pages.Count);
        return $"{secs} {(secs == 1 ? "section" : "sections")} · {pages} {(pages == 1 ? "page" : "pages")}";
    });
}
