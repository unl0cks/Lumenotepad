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

    /// <summary>Family names inside Assets/Fonts (must match the fonts' internal names).</summary>
    public static readonly string[] Bundled = { "Bebas Neue", "Caveat", "Gambarino", "Yuyu" };

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
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> rest = extended
            ? installed.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            : Curated.Where(installed.Contains);
        return Bundled.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Concat(WithoutDisabled(rest.Where(n => !Bundled.Contains(n, StringComparer.OrdinalIgnoreCase)), disabled))
            .ToList();
    }
}
