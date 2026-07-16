using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Services;

/// <summary>The browsable font index behind the Font Browser (M11): the full Google Fonts catalog
/// (~1900 families) fetched keyless from the public metadata endpoint, tagged with friendly
/// categories. Metadata only — a family's actual file is downloaded lazily for preview/install.
/// Fetched once per session and cached; a failed fetch yields an empty list, never a crash.</summary>
public static class FontCatalog
{
    public sealed record CatalogFont(string Name, string Category, string Stroke, int Popularity);

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

    /// <summary>Load the catalog (cached after the first success). Latin-capable families only, sorted
    /// most-popular first so the browser opens on recognizable fonts.</summary>
    public static async Task<IReadOnlyList<CatalogFont>> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is { Count: > 0 }) return _cache;
        try
        {
            var json = await Http.GetStringAsync("https://fonts.google.com/metadata/fonts", ct);
            _cache = Parse(json);
        }
        catch { return Array.Empty<CatalogFont>(); }
        return _cache;
    }

    /// <summary>Pure: parse the Google metadata JSON into the catalog (Latin subset, popularity kept
    /// for sorting). Tolerant of missing fields; unknown shape yields an empty list.</summary>
    public static IReadOnlyList<CatalogFont> Parse(string json)
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
                // Latin-only (skip CJK/Arabic/etc. that would render as tofu in the preview).
                if (f.TryGetProperty("subsets", out var subs) && subs.ValueKind == JsonValueKind.Array &&
                    !subs.EnumerateArray().Any(s => s.GetString() == "latin"))
                    continue;
                string cat = f.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                string stroke = f.TryGetProperty("stroke", out var st) ? st.GetString() ?? "" : "";
                int pop = f.TryGetProperty("popularity", out var p) && p.TryGetInt32(out var pv) ? pv : 99999;
                list.Add(new CatalogFont(name, cat, stroke, pop));
            }
        }
        catch { return new List<CatalogFont>(); }
        list.Sort((a, b) => a.Popularity != b.Popularity
            ? a.Popularity.CompareTo(b.Popularity)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
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
            "gothic" => Name("blackletter", "black letter", "fraktur", "medieval", "old english",
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
