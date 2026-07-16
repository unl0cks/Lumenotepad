using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Lumenotepad.Services;

/// <summary>The app's font catalog: the four BUNDLED faces (shipped as embedded resources, always
/// available on any machine), a curated shortlist of familiar system fonts, and — behind the
/// "Extended font list" preference — everything installed.</summary>
public static class AppFonts
{
    /// <summary>The embedded collection key registered in Program.BuildAvaloniaApp.</summary>
    public const string CollectionUri = "fonts:lumenotepad";

    /// <summary>The downloaded-fonts collection key (see <see cref="InstalledFontCollection"/>).</summary>
    public const string InstalledUri = "fonts:installed";

    /// <summary>Family names inside Assets/Fonts (must match the fonts' internal names).</summary>
    public static readonly string[] Bundled = { "Bebas Neue", "Caveat", "Gambarino", "Yuyu" };

    private static readonly List<string> _installed = new();

    /// <summary>Families the user downloaded via the font installer (M11); refreshed by
    /// <see cref="RegisterInstalled"/>.</summary>
    public static IReadOnlyList<string> Installed => _installed;

    /// <summary>Raised after the installed-fonts collection changes, so open font menus refresh.</summary>
    public static event Action? InstalledChanged;

    /// <summary>(Re)load the downloaded-fonts folder into the live font manager and the name lists.
    /// Same-key AddFontCollection replaces the old collection, so a font installed a second ago is
    /// immediately usable. Call once at startup and again after every install.</summary>
    public static void RegisterInstalled()
    {
        var col = new InstalledFontCollection(FontInstaller.FontsDir);
        FontManager.Current.AddFontCollection(col);        // runs Initialize
        _installed.Clear();
        _installed.AddRange(col.Names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        InstalledChanged?.Invoke();
    }

    /// <summary>The owner's default shortlist (only those actually installed are offered).</summary>
    public static readonly string[] Curated =
    {
        "Arial", "Helvetica", "Helvetica Neue", "Verdana", "Tahoma", "Trebuchet MS",
        "Times New Roman", "Georgia", "Garamond", "Courier New", "Impact", "Consolas",
        "Century Gothic", "Roboto", "Cambria", "Segoe UI", "Calibri", "Lucida Sans",
    };

    /// <summary>Resolve a stored family name to a usable FontFamily (bundled names route to the
    /// embedded collection; anything else resolves against the system). Null/blank → the UI default
    /// — virtualized list recycling briefly rebuilds item templates with a null datum, and
    /// <c>new FontFamily(null)</c> throws.</summary>
    public static FontFamily Family(string? name) =>
        string.IsNullOrWhiteSpace(name) ? FontFamily.Default
        : Bundled.Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily($"{CollectionUri}#{name}")
        : _installed.Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily($"{InstalledUri}#{name}")
        : new FontFamily(name);

    /// <summary>Pure filter for the fonts-curation pref: drop disabled names (case-insensitive)
    /// but NEVER the bundled faces — they must stay reachable on every machine.</summary>
    public static IEnumerable<string> WithoutDisabled(IEnumerable<string> names,
                                                      IReadOnlyCollection<string>? disabled)
    {
        if (disabled is not { Count: > 0 }) return names;
        var hidden = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase);
        foreach (var b in Bundled) hidden.Remove(b);
        return names.Where(n => !hidden.Contains(n));
    }

    /// <summary>The names offered by the toolbar's font menu: bundled first, then the curated
    /// shortlist (or every installed family when <paramref name="extended"/>), minus any the
    /// fonts-curation pref disabled (bundled faces are never hidden).</summary>
    public static IReadOnlyList<string> ListNames(bool extended, IReadOnlyCollection<string>? disabled = null)
    {
        var system = FontManager.Current.SystemFonts.Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> rest = extended
            ? system.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            : Curated.Where(system.Contains);
        var own = new HashSet<string>(Bundled.Concat(_installed), StringComparer.OrdinalIgnoreCase);
        // Bundled first, then the user's downloaded fonts (hideable like any other), then the rest.
        return Bundled.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Concat(WithoutDisabled(_installed.Where(n => !Bundled.Contains(n, StringComparer.OrdinalIgnoreCase)), disabled))
            .Concat(WithoutDisabled(rest.Where(n => !own.Contains(n)), disabled))
            .ToList();
    }
}
