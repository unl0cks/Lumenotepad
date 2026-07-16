using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Services;

/// <summary>The "Get more fonts" engine (M11, design §9): searches Google Fonts and Fontshare and
/// downloads font files into the app's own <c>userdata/fonts</c> folder — nothing is installed
/// into Windows, no admin rights, fully portable. <see cref="InstalledFontCollection"/> serves the
/// folder to the app at runtime. The network is touched ONLY from the user's explicit search /
/// install clicks, and every failure degrades to an empty result, never a crash.</summary>
public static class FontInstaller
{
    /// <summary>Where downloaded font files live (portable, beside the settings file).</summary>
    public static string FontsDir { get; set; } = Path.Combine(AppSettings.DefaultDir, "fonts");

    public sealed record Hit(string Name, string Source, string Id);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Search both catalogs. Fontshare has a real search API; Google Fonts has no keyless
    /// directory, so the typed name is probed directly — an exact family ("Lobster", "Fira Sans")
    /// comes back as the first hit.</summary>
    public static async Task<List<Hit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var hits = new List<Hit>();
        query = query.Trim();
        if (query.Length == 0) return hits;

        try
        {
            string fam = TitleCase(query);
            using var resp = await Http.GetAsync(GoogleCssUrl(fam), ct);
            if (resp.IsSuccessStatusCode) hits.Add(new Hit(fam, "Google Fonts", fam));
        }
        catch { /* offline / blocked → just no Google hit */ }

        try
        {
            var json = await Http.GetStringAsync(
                "https://api.fontshare.com/v2/fonts?search=" + Uri.EscapeDataString(query), ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var f in doc.RootElement.GetProperty("fonts").EnumerateArray().Take(8))
                if (f.TryGetProperty("name", out var n) && f.TryGetProperty("slug", out var s) &&
                    n.GetString() is { Length: > 0 } name && s.GetString() is { Length: > 0 } slug)
                    hits.Add(new Hit(name, "Fontshare", slug));
        }
        catch { /* offline / API change → just no Fontshare hits */ }

        return hits;
    }

    /// <summary>Download a hit's font files into <see cref="FontsDir"/>; returns how many files
    /// landed (0 = nothing usable / failed).</summary>
    public static async Task<int> InstallAsync(Hit hit, CancellationToken ct = default)
    {
        Directory.CreateDirectory(FontsDir);
        int files = 0;
        if (hit.Source == "Google Fonts")
        {
            var css = await Http.GetStringAsync(GoogleCssUrl(hit.Id), ct);
            int i = 0;
            foreach (var url in ParseCssFontUrls(css))
            {
                var bytes = await Http.GetByteArrayAsync(url, ct);
                string ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (ext.Length == 0) ext = ".ttf";
                await File.WriteAllBytesAsync(
                    Path.Combine(FontsDir, SafeName(hit.Name) + "-" + i++ + ext), bytes, ct);
                files++;
            }
        }
        else
        {
            var zipBytes = await Http.GetByteArrayAsync(
                "https://api.fontshare.com/v2/fonts/download/" + Uri.EscapeDataString(hit.Id), ct);
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            foreach (var name in ChooseFontEntries(zip.Entries.Select(e => e.FullName)))
            {
                var entry = zip.GetEntry(name);
                if (entry is null) continue;
                using var src = entry.Open();
                using var dst = File.Create(Path.Combine(FontsDir, SafeName(Path.GetFileName(name))));
                await src.CopyToAsync(dst, ct);
                files++;
            }
        }
        return files;
    }

    /// <summary>The css2 request whose plain (non-browser) user agent makes Google answer with raw
    /// TTF links. Asks for regular, bold, and italic so the editor's styles have real faces.</summary>
    public static string GoogleCssUrl(string family) =>
        "https://fonts.googleapis.com/css2?family=" +
        Uri.EscapeDataString(family).Replace("%20", "+") + ":ital,wght@0,400;0,700;1,400";

    /// <summary>Pure: every font URL in a css2 stylesheet.</summary>
    public static List<string> ParseCssFontUrls(string css) =>
        Regex.Matches(css, @"url\((https://[^)]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Pure: which entries of a Fontshare zip are worth extracting — desktop font files
    /// only (the WEB folder is browser formats), no variable fonts (the static weights are what a
    /// document app wants), TTFs preferred when a static set exists, else the OTFs. Capped so a
    /// giant family can't flood the folder.</summary>
    public static List<string> ChooseFontEntries(IEnumerable<string> entryNames)
    {
        var desktop = entryNames
            .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                        n.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Contains("/WEB/", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(n).Contains("Variable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var ttf = desktop.Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToList();
        return (ttf.Count > 0 ? ttf : desktop).Take(20).ToList();
    }

    /// <summary>Pure: title-case a typed family name the way Google's catalog spells them
    /// ("fira sans" → "Fira Sans").</summary>
    public static string TitleCase(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    /// <summary>Pure: a filename-safe version of a family/file name.</summary>
    public static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
