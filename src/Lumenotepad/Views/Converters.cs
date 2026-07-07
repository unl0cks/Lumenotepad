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
    {
        try { return new SolidColorBrush(Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#4DA6FF" : hex)); }
        catch { return new SolidColorBrush(Color.Parse("#4DA6FF")); }
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
