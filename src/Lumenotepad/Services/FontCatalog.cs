using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Services;

/// <summary>The browsable font index behind the Font Browser (M11): the full Google Fonts catalog
/// (~1900 families), the Fontshare library (~100), and Fontsource's non-Google open fonts (~120) —
/// all fetched keyless from their public endpoints, deduped by name, and tagged with friendly
/// categories. Metadata only — a family's file is downloaded lazily for preview/install. Fetched
/// once per session and cached; a failed source is skipped, never fatal.</summary>
public static class FontCatalog
{
    public const string Google = "Google Fonts";
    public const string Fontshare = "Fontshare";
    public const string Fontsource = "Fontsource";

    /// <summary><paramref name="Id"/> is the download key for the source (Google: family name;
    /// Fontshare: slug; Fontsource: id).</summary>
    public sealed record CatalogFont(string Name, string Source, string Id, string Category, string Stroke, int Popularity);

    /// <summary>The category chips, in display order. "all" is the implicit first filter; the rest are
    /// multi-selectable and combine with AND (each narrows the results further).</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Categories = new[]
    {
        ("all", "All"),
        ("sans", "Sans Serif"), ("serif", "Serif"), ("slab", "Slab"), ("mono", "Monospace"),
        ("display", "Display"), ("handwriting", "Handwritten"), ("script", "Script"),
        ("cursive", "Cursive"), ("calligraphy", "Calligraphy"), ("brush", "Brush"),
        ("cute", "Cute"), ("rounded", "Rounded"), ("bold", "Bold"), ("thin", "Thin"),
        ("condensed", "Condensed"), ("gothic", "Gothic"), ("stencil", "Stencil"),
        ("pixel", "Pixel"), ("retro", "Retro"), ("techno", "Techno"), ("elegant", "Elegant"),
        ("comic", "Comic"), ("outline", "Outline"), ("typewriter", "Typewriter"),
        ("western", "Western"), ("horror", "Horror"), ("decorative", "Decorative"),
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static IReadOnlyList<CatalogFont>? _cache;

    /// <summary>Load the combined catalog (cached after the first success). Google is sorted
    /// most-popular-first; Fontshare + Fontsource families are spread THROUGH that list so they're
    /// discoverable while scrolling, not buried at the end.</summary>
    public static async Task<IReadOnlyList<CatalogFont>> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is { Count: > 0 }) return _cache;
        var google = new List<CatalogFont>();
        var extras = new List<CatalogFont>();
        try { google.AddRange(ParseGoogle(await Http.GetStringAsync("https://fonts.google.com/metadata/fonts", ct))); }
        catch { /* offline / shape change → no Google */ }
        try { extras.AddRange(ParseFontshare(await Http.GetStringAsync("https://api.fontshare.com/v2/fonts?limit=500", ct))); }
        catch { /* skip Fontshare */ }
        try { extras.AddRange(ParseFontsource(await Http.GetStringAsync("https://api.fontsource.org/v1/fonts", ct))); }
        catch { /* skip Fontsource */ }

        if (google.Count == 0 && extras.Count == 0) return Array.Empty<CatalogFont>();
        _cache = Merge(google, extras);
        return _cache;
    }

    /// <summary>Pure: interleave the extra-source families into the popularity-sorted Google list by
    /// giving each a synthetic popularity spread across Google's range, and drop names Google already
    /// has (Google wins duplicates).</summary>
    public static IReadOnlyList<CatalogFont> Merge(IReadOnlyList<CatalogFont> google, IReadOnlyList<CatalogFont> extras)
    {
        var seen = new HashSet<string>(google.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        var uniqueExtras = new List<CatalogFont>();
        foreach (var e in extras)
            if (seen.Add(e.Name)) uniqueExtras.Add(e);

        int span = Math.Max(google.Count, 100);
        var spread = uniqueExtras.Select((f, i) =>
            f with { Popularity = (int)Math.Round((i + 0.5) * span / Math.Max(1, uniqueExtras.Count)) });
        return google.Concat(spread)
            .OrderBy(f => f.Popularity)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Pure: parse the Google metadata JSON (Latin subset, popularity kept for sorting).</summary>
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

    /// <summary>Pure: parse the Fontshare fonts JSON, normalizing its category strings to the
    /// Google-style category + stroke the heuristics expect.</summary>
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

    /// <summary>Pure: parse the Fontsource fonts JSON, keeping only its NON-Google ("other") fonts
    /// (the Google ones would just duplicate the Google source), Latin subset.</summary>
    public static IReadOnlyList<CatalogFont> ParseFontsource(string json)
    {
        var list = new List<CatalogFont>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (f.TryGetProperty("type", out var t) && t.GetString() == "google") continue;   // dedupe against Google source
                if (!f.TryGetProperty("id", out var idEl) || idEl.GetString() is not { Length: > 0 } id) continue;
                if (!f.TryGetProperty("family", out var famEl) || famEl.GetString() is not { Length: > 0 } name) continue;
                if (f.TryGetProperty("subsets", out var subs) && subs.ValueKind == JsonValueKind.Array &&
                    !subs.EnumerateArray().Any(s => s.GetString() == "latin"))
                    continue;
                string raw = f.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                list.Add(new CatalogFont(name, Fontsource, id, NormalizeFontsourceCategory(raw), "", 0));
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
        if (r.Contains("serif")) return ("Serif", "");
        if (r.Contains("sans")) return ("Sans Serif", "");
        if (r.Contains("display")) return ("Display", "");
        return ("Display", "");
    }

    /// <summary>Pure: map a Fontsource lowercase category ("sans-serif", "handwriting"…) to Google-style.</summary>
    public static string NormalizeFontsourceCategory(string raw) => raw.ToLowerInvariant() switch
    {
        "sans-serif" => "Sans Serif",
        "serif" => "Serif",
        "monospace" => "Monospace",
        "handwriting" => "Handwriting",
        "display" => "Display",
        _ => "Display",
    };

    /// <summary>Pure: does a font belong to a category chip? Categories overlap by design (they're
    /// filters, not a partition), so a slab face is both "Serif" and "Slab".</summary>
    public static bool MatchesCategory(CatalogFont f, string categoryKey)
    {
        if (categoryKey == "all") return true;
        string n = f.Name.ToLowerInvariant();
        string s = f.Stroke.ToLowerInvariant();
        bool Name(params string[] kw) => kw.Any(k => n.Contains(k));
        return categoryKey switch
        {
            "sans" => f.Category == "Sans Serif",
            "serif" => f.Category == "Serif",
            "slab" => s.Contains("slab") || Name("slab"),
            "mono" => f.Category == "Monospace" || Name("mono"),
            "display" => f.Category == "Display",
            "handwriting" => f.Category == "Handwriting",
            "script" => f.Category == "Handwriting" && Name("script", "brush", "signature", "calligraph", "pen"),
            "cursive" => Name("cursive", "hand", "flow", "swash", "dancing", "sacramento", "allura",
                              "tangerine", "pinyon", "parisienne", "great vibes"),
            "calligraphy" => Name("calligr", "pinyon", "tangerine", "allura", "parisienne", "great vibes",
                                  "petit formal", "italianno", "pirata"),
            "brush" => Name("brush", "marker", "ink", "paint", "sketch", "permanent marker", "reenie", "gochi"),
            "cute" => Name("bubble", "comic", "candy", "cute", "kawaii", "round", "marker", "doodle",
                          "sniglet", "baloo", "fredoka", "chewy", "bungee", "patrick", "schoolbell",
                          "gochi", "pangolin", "grand hotel", "sue ellen"),
            "rounded" => Name("round", "baloo", "quicksand", "fredoka", "nunito", "comfortaa", "varela",
                             "chewy", "dosis"),
            "bold" => Name("black", "heavy", "fat", "ultra", "bold", "extrabold", "impact", "anton",
                          "titan", "archivo black", "passion one"),
            "thin" => Name("thin", "light", "hairline", "fine"),
            "condensed" => Name("condensed", "narrow", "compressed", "tight", "oswald", "bebas"),
            "gothic" => s.Contains("blackletter") ||
                        Name("blackletter", "fraktur", "medieval", "old english", "uncial", "pirata",
                             "unifraktur", "grenze", "germania", "cloister"),
            "stencil" => Name("stencil", "army", "military", "stamp"),
            "pixel" => Name("pixel", "pixelify", "vt323", "press start", "silkscreen", "handjet",
                           "arcade", "8bit", "dotgothic", "workbench"),
            "retro" => Name("retro", "vintage", "groovy", "disco", "deco", "monoton", "shrikhand",
                           "lobster", "pacifico", "bungee", "kalam", "righteous"),
            "techno" => Name("techno", "cyber", "future", "orbitron", "exo", "rajdhani", "quantico",
                            "electrolize", "audiowide", "michroma", "syncopate"),
            "elegant" => Name("playfair", "cormorant", "cinzel", "marcellus", "italiana", "bodoni",
                             "eb garamond", "cardo", "prata", "gilda"),
            "comic" => Name("comic", "cartoon", "bangers", "luckiest", "fredoka", "chewy", "bubblegum"),
            "outline" => Name("outline", "hollow", "shadows into", "codystar", "wallpoet"),
            "typewriter" => Name("typewriter", "courier", "special elite", "cutive", "jetbrains",
                                "source code", "cousine", "nanum gothic coding"),
            "western" => Name("western", "cowboy", "rye", "ranch", "rodeo", "smokum", "ewert"),
            "horror" => Name("horror", "blood", "creepy", "ghost", "nosifer", "creepster", "eater",
                            "butcher", "frijole", "nightmare"),
            "decorative" => f.Category == "Display",
            _ => false,
        };
    }

    /// <summary>Pure: filter by a SET of category chips (AND — each further narrows) plus the name
    /// query. "all" (or an empty set) applies no category filter.</summary>
    public static IReadOnlyList<CatalogFont> Filter(
        IReadOnlyList<CatalogFont> catalog, IReadOnlyCollection<string> categoryKeys, string? query)
    {
        var q = query?.Trim() ?? "";
        var active = categoryKeys.Where(k => k != "all").ToList();
        return catalog.Where(f =>
                active.All(k => MatchesCategory(f, k)) &&
                (q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
