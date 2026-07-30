using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Lumenotepad.Services;

public sealed class InstalledFontCollection : FontCollectionBase
{
    private readonly Uri _key = new(AppFonts.InstalledUri);

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
            catch {  }
        }
    }

    public override Uri Key => _key;
}
