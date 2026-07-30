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

public static class FontInstaller
{

    public static string FontsDir { get; set; } = Path.Combine(AppSettings.DefaultDir, "fonts");

    public sealed record Hit(string Name, string Source, string Id);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

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
        catch {  }

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
        catch {  }

        return hits;
    }

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
        else if (hit.Source == "Fontsource")
        {

            foreach (var (w, ital, tag) in new[] { (400, false, "r"), (700, false, "b"), (400, true, "i") })
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(FontsourceTtfUrl(hit.Id, w, ital), ct);
                    await File.WriteAllBytesAsync(
                        Path.Combine(FontsDir, SafeName(hit.Name) + "-" + tag + ".ttf"), bytes, ct);
                    files++;
                }
                catch {  }
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

    public static string GoogleCssUrl(string family) =>
        "https://fonts.googleapis.com/css2?family=" +
        Uri.EscapeDataString(family).Replace("%20", "+") + ":ital,wght@0,400;0,700;1,400";

    public static List<string> ParseCssFontUrls(string css) =>
        Regex.Matches(css, @"url\((https://[^)]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();

    public static string FontsourceTtfUrl(string id, int weight, bool italic) =>
        $"https://cdn.jsdelivr.net/fontsource/fonts/{id}@latest/latin-{weight}-{(italic ? "italic" : "normal")}.ttf";

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

    public static string TitleCase(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    public static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
