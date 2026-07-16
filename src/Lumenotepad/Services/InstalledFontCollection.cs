using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Lumenotepad.Services;

/// <summary>Serves the fonts the user downloaded via <see cref="FontInstaller"/> (userdata/fonts)
/// to Avalonia at runtime under the <c>fonts:installed</c> key — the download-time re-register
/// (FontManager.AddFontCollection replaces a same-key collection) makes a freshly installed font
/// usable immediately, no app restart. Unreadable/corrupt files are skipped, never fatal.
///
/// FontCollectionBase seals Count/indexer/GetEnumerator over its own family list, so we only supply
/// Key and populate that list: TryAddGlyphTypeface(stream) parses a face and registers its glyphs;
/// AddFontFamily makes the family enumerable/resolvable. Must be constructed AFTER the font manager
/// exists (App startup / after each install), which TryAddGlyphTypeface relies on.</summary>
public sealed class InstalledFontCollection : FontCollectionBase
{
    private readonly Uri _key = new(AppFonts.InstalledUri);

    /// <summary>Family names discovered while loading (deduped, in file order).</summary>
    public List<string> Names { get; } = new();

    public InstalledFontCollection(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir)
                     .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(file);
                if (!TryAddGlyphTypeface(stream, out var glyphTypeface)) continue;
                string name = glyphTypeface.FamilyName;
                if (Names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                Names.Add(name);
                AddFontFamily(new FontFamily($"{AppFonts.InstalledUri}#{name}"));
            }
            catch { /* corrupt/locked file → skip it */ }
        }
    }

    public override Uri Key => _key;
}
