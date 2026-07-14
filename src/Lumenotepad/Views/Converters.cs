using System;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lumenotepad.Views;

/// <summary>Small value converters used by the notebook UI.</summary>
public static class Converters
{
    /// <summary>Up to two uppercase initials for a notebook chip ("Biology" → "BI", "My Notebook" → "MN").</summary>
    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(name =>
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return (words[0].Length >= 2 ? words[0].Substring(0, 2) : words[0]).ToUpperInvariant();
        return string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    });

    /// <summary>A hex color string ("#4DA6FF") → a brush, for binding a stored color to a Background.</summary>
    public static readonly IValueConverter HexBrush = new FuncValueConverter<string?, IBrush>(hex =>
        new SolidColorBrush(SafeParse(hex)));

    /// <summary>Blend a color toward white (f &gt; 0) or black (f &lt; 0); f in -1..1.</summary>
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

    /// <summary>Notebook color → the cover fill: a soft top-lit vertical gradient (lighter edge in,
    /// darker base) so covers and chips read as surfaces instead of flat paint.</summary>
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

    /// <summary>Notebook color → a same-hue, gently darker 1px border for covers and chips. The
    /// border is composited fully opaque (not an alpha overlay), so softening it means shrinking the
    /// shade delta rather than the alpha — an alpha cut would just blend toward whatever sits behind
    /// the card, which varies by theme/placement, instead of reading consistently softer everywhere.
    /// -0.38 → -0.27 (~30% smaller delta); global across every theme by design (owner: gentler outlines).</summary>
    public static readonly IValueConverter CoverBorder = new FuncValueConverter<string?, IBrush>(hex =>
        new SolidColorBrush(Shade(SafeParse(hex), -0.27)));

    /// <summary>Notebook color → a very faint glow (for the selected rail chip). Blur kept small so
    /// the halo fits inside the rail's ~12px side slack instead of clipping at the edges.</summary>
    public static readonly IValueConverter GlowShadow = new FuncValueConverter<string?, BoxShadows>(hex =>
    {
        var c = SafeParse(hex);
        return BoxShadows.Parse($"0 0 4 0 #5A{c.R:X2}{c.G:X2}{c.B:X2}");
    });

    // Covers decoded at card size (full-resolution photos re-rasterizing every hover-animation
    // frame is what made card hover lag) and cached per path+mtime.
    private static readonly System.Collections.Generic.Dictionary<string, (DateTime Stamp, IBrush Brush)> CoverCache = new();

    /// <summary>Absolute cover-image path → a fill brush for the card (null = keep the color cover).</summary>
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

    /// <summary>Gallery-card subtitle: "3 sections · 12 pages".</summary>
    public static readonly IValueConverter NotebookStats = new FuncValueConverter<Models.Notebook?, string>(nb =>
    {
        if (nb is null) return "";
        int secs = nb.Sections.Count;
        int pages = nb.Sections.Sum(s => s.Pages.Count);
        return $"{secs} {(secs == 1 ? "section" : "sections")} · {pages} {(pages == 1 ? "page" : "pages")}";
    });
}
