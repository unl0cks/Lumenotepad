using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Lumenotepad.Services;

public static class AppFonts
{

    public const string CollectionUri = "fonts:lumenotepad";

    public const string InstalledUri = "fonts:installed";

    public static readonly string[] Bundled = { "Bebas Neue", "Caveat", "Gambarino", "Yuyu" };

    private static readonly List<string> _installed = new();

    public static IReadOnlyList<string> Installed => _installed;

    public static event Action? InstalledChanged;

    public static void RegisterInstalled()
    {
        var col = new InstalledFontCollection(FontInstaller.FontsDir);
        FontManager.Current.AddFontCollection(col);
        _installed.Clear();
        _installed.AddRange(col.Names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        InstalledChanged?.Invoke();
    }

    public static readonly string[] Curated =
    {
        "Arial", "Helvetica", "Helvetica Neue", "Verdana", "Tahoma", "Trebuchet MS",
        "Times New Roman", "Georgia", "Garamond", "Courier New", "Impact", "Consolas",
        "Century Gothic", "Roboto", "Cambria", "Segoe UI", "Calibri", "Lucida Sans",
    };

    public static FontFamily Family(string? name) =>
        string.IsNullOrWhiteSpace(name) ? FontFamily.Default
        : Bundled.Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily($"{CollectionUri}#{name}")
        : _installed.Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily($"{InstalledUri}#{name}")
        : new FontFamily(name);

    public static IEnumerable<string> WithoutDisabled(IEnumerable<string> names,
                                                      IReadOnlyCollection<string>? disabled)
    {
        if (disabled is not { Count: > 0 }) return names;
        var hidden = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase);
        foreach (var b in Bundled) hidden.Remove(b);
        return names.Where(n => !hidden.Contains(n));
    }

    public static IReadOnlyList<string> ListNames(bool extended, IReadOnlyCollection<string>? disabled = null)
    {
        var system = FontManager.Current.SystemFonts.Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> rest = extended
            ? system.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            : Curated.Where(system.Contains);
        var own = new HashSet<string>(Bundled.Concat(_installed), StringComparer.OrdinalIgnoreCase);

        return Bundled.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Concat(WithoutDisabled(_installed.Where(n => !Bundled.Contains(n, StringComparer.OrdinalIgnoreCase)), disabled))
            .Concat(WithoutDisabled(rest.Where(n => !own.Contains(n)), disabled))
            .ToList();
    }
}
