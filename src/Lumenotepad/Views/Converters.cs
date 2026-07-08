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
                new GradientStop(Shade(c, 0.10), 0),
                new GradientStop(c, 0.45),
                new GradientStop(Shade(c, -0.14), 1),
            },
        };
    });

    /// <summary>Notebook color → a same-hue, clearly darker 1px border for covers and chips.</summary>
    public static readonly IValueConverter CoverBorder = new FuncValueConverter<string?, IBrush>(hex =>
        new SolidColorBrush(Shade(SafeParse(hex), -0.38)));

    /// <summary>Gallery-card subtitle: "3 sections · 12 pages".</summary>
    public static readonly IValueConverter NotebookStats = new FuncValueConverter<Models.Notebook?, string>(nb =>
    {
        if (nb is null) return "";
        int secs = nb.Sections.Count;
        int pages = nb.Sections.Sum(s => s.Pages.Count);
        return $"{secs} {(secs == 1 ? "section" : "sections")} · {pages} {(pages == 1 ? "page" : "pages")}";
    });
}
