using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Services;

/// <summary>The browsable font index behind the Font Browser (M11): the full Google Fonts catalog
/// (~1900 families) plus the Fontshare library (~100), both fetched keyless from their public
/// endpoints and tagged with friendly categories. Metadata only — a family's actual file is
/// downloaded lazily for preview/install. Fetched once per session and cached; a failed fetch
/// yields whatever succeeded, never a crash.</summary>
public static class FontCatalog
{
    public const string Google = "Google Fonts";
    public const string Fontshare = "Fontshare";

    /// <summary><paramref name="Id"/> is the download key for the source (Google: family name;
    /// Fontshare: slug).</summary>
    public sealed record CatalogFont(string Name, string Source, string Id, string Category, string Stroke, int Popularity);

    /// <summary>The category chips, in display order. "all" is the implicit first filter.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Categories = new[]
    {
        ("all", "All"),
        ("handwriting", "Handwritten"),
        ("script", "Script"),
        ("cursive", "Cursive"),
        ("cute", "Cute"),
        ("fancy", "Fancy"),
        ("gothic", "Gothic"),
        ("blocky", "Blocky"),
        ("serif", "Serif"),
        ("sans", "Sans Serif"),
        ("mono", "Monospace"),
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static IReadOnlyList<CatalogFont>? _cache;

    /// <summary>Load the combined catalog (cached after the first success). Google is sorted
    /// most-popular-first; Fontshare families are spread THROUGH that list (synthetic popularity) so
    /// they're discoverable while scrolling, not buried at the end.</summary>
    public static async Task<IReadOnlyList<CatalogFont>> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is { Count: > 0 }) return _cache;
        var google = new List<CatalogFont>();
        var fontshare = new List<CatalogFont>();
        try { google.AddRange(ParseGoogle(await Http.GetStringAsync("https://fonts.google.com/metadata/fonts", ct))); }
        catch { /* offline / shape change → no Google */ }
        try { fontshare.AddRange(ParseFontshare(await Http.GetStringAsync("https://api.fontshare.com/v2/fonts?limit=500", ct))); }
        catch { /* offline / shape change → no Fontshare */ }

        if (google.Count == 0 && fontshare.Count == 0) return Array.Empty<CatalogFont>();
        _cache = Merge(google, fontshare);
        return _cache;
    }

    /// <summary>Pure: interleave Fontshare families into the popularity-sorted Google list by giving
    /// each a synthetic popularity spread across Google's range.</summary>
    public static IReadOnlyList<CatalogFont> Merge(IReadOnlyList<CatalogFont> google, IReadOnlyList<CatalogFont> fontshare)
    {
        int span = Math.Max(google.Count, 100);
        var spread = fontshare.Select((f, i) =>
            f with { Popularity = (int)Math.Round((i + 0.5) * span / Math.Max(1, fontshare.Count)) });
        return google.Concat(spread)
            .OrderBy(f => f.Popularity)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Pure: parse the Google metadata JSON (Latin subset, popularity kept for sorting).
    /// Tolerant of missing fields; unknown shape yields an empty list.</summary>
    public static IReadOnlyList<CatalogFont> ParseGoogle(string json)
    {
        var list = new List<CatalogFont>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("familyMetadataList", out var fams)) return list;
            foreach (var f in fams.EnumerateArray())
            {
                if (!f.TryGetProperty("family", out var famEl) || famEl.GetString() is not { Length: > 0 } name)
                    continue;
                if (f.TryGetProperty("subsets", out var subs) && subs.ValueKind == JsonValueKind.Array &&
                    !subs.EnumerateArray().Any(s => s.GetString() == "latin"))
                    continue;
                string cat = f.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                string stroke = f.TryGetProperty("stroke", out var st) ? st.GetString() ?? "" : "";
                int pop = f.TryGetProperty("popularity", out var p) && p.TryGetInt32(out var pv) ? pv : 99999;
                list.Add(new CatalogFont(name, Google, name, cat, stroke, pop));
            }
        }
        catch { return new List<CatalogFont>(); }
        return list;
    }

    /// <summary>Pure: parse the Fontshare fonts JSON. Its category strings ("Sans", "Slab",
    /// "Blackletter", "Serif, Display"…) are normalized to the Google-style category + stroke the
    /// heuristics expect, so the same chips filter both sources.</summary>
    public static IReadOnlyList<CatalogFont> ParseFontshare(string json)
    {
        var list = new List<CatalogFont>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fonts", out var fonts)) return list;
            foreach (var f in fonts.EnumerateArray())
            {
                if (!f.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
                if (!f.TryGetProperty("slug", out var s) || s.GetString() is not { Length: > 0 } slug) continue;
                string raw = f.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                var (cat, stroke) = NormalizeFontshareCategory(raw);
                list.Add(new CatalogFont(name, Fontshare, slug, cat, stroke, 0));
            }
        }
        catch { return new List<CatalogFont>(); }
        return list;
    }

    /// <summary>Pure: map a Fontshare category label to the Google-style (category, stroke) pair.</summary>
    public static (string Category, string Stroke) NormalizeFontshareCategory(string raw)
    {
        string r = raw.ToLowerInvariant();
        if (r.Contains("blackletter")) return ("Display", "Blackletter");
        if (r.Contains("script") || r.Contains("handwritten")) return ("Handwriting", "");
        if (r.Contains("slab")) return ("Serif", "Slab Serif");
        if (r.Contains("mono")) return ("Monospace", "");
        if (r.Contains("serif")) return ("Serif", "");        // before "sans" so "Sans, Serif" → Serif is avoided
        if (r.Contains("sans")) return ("Sans Serif", "");
        if (r.Contains("display")) return ("Display", "");
        return ("Display", "");                                // unknown → Display (the catch-all bucket)
    }

    /// <summary>Pure: does a font belong to a category chip? Categories overlap by design (they're
    /// filters, not a partition) — "Blocky" and "Serif" can both include a slab face.</summary>
    public static bool MatchesCategory(CatalogFont f, string categoryKey)
    {
        if (categoryKey == "all") return true;
        string n = f.Name.ToLowerInvariant();
        string s = f.Stroke.ToLowerInvariant();
        bool Name(params string[] kw) => kw.Any(k => n.Contains(k));
        return categoryKey switch
        {
            "handwriting" => f.Category == "Handwriting",
            "script" => f.Category == "Handwriting" && Name("script", "brush", "signature", "calligraph", "pen"),
            "cursive" => f.Category == "Handwriting" && Name("cursive", "hand", "flow", "swash", "dancing", "sacramento", "allura", "tangerine"),
            "cute" => Name("bubble", "comic", "candy", "cute", "kawaii", "round", "marker", "doodle",
                           "sniglet", "baloo", "fredoka", "chewy", "bungee", "patrick", "schoolbell",
                           "gochi", "sue ellen", "short stack", "grand hotel", "pangolin"),
            "fancy" => f.Category == "Display",
            "gothic" => s.Contains("blackletter") ||
                        Name("blackletter", "black letter", "fraktur", "medieval", "old english",
                             "uncial", "pirata", "unifraktur", "grenze", "gothic", "germania", "cloister"),
            "blocky" => s.Contains("slab") || f.Category == "Monospace" ||
                        Name("slab", "block", "stencil", "impact", "heavy", "black ", "condensed",
                             "bungee", "titan", "squada", "stint", "anton", "bebas"),
            "serif" => f.Category == "Serif",
            "sans" => f.Category == "Sans Serif",
            "mono" => f.Category == "Monospace",
            _ => true,
        };
    }

    /// <summary>Pure: the category + text filter applied to a catalog (case-insensitive name contains).</summary>
    public static IReadOnlyList<CatalogFont> Filter(
        IReadOnlyList<CatalogFont> catalog, string categoryKey, string? query)
    {
        var q = query?.Trim() ?? "";
        return catalog.Where(f => MatchesCategory(f, categoryKey) &&
                                  (q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                      .ToList();
    }
}
