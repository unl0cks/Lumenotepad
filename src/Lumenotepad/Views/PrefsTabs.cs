using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Views;

public sealed record PrefsTab(string Name, string[] Headings);

public static class PrefsTabs
{
    private static readonly Dictionary<string, PrefsTab[]> Table = new()
    {
        ["general"] = new[]
        {
            new PrefsTab("Startup", new[] { "STARTUP", "SYSTEM TRAY" }),
            new PrefsTab("Home screen", new[] { "HOMEPAGE", "GALLERY" }),
            new PrefsTab("Deleting", new[] { "ASK BEFORE DELETING" }),
        },
        ["appearance"] = new[]
        {
            new PrefsTab("Theme", new[] { "THEME", "ACCENT" }),
            new PrefsTab("Glass & shape", new[] { "GLASS", "SHAPE" }),
            new PrefsTab("Motion", new[] { "MOTION" }),
        },
        ["layout"] = new[]
        {
            new PrefsTab("Panels", new[] { "LAYOUT", "PANELS" }),
            new PrefsTab("Toolbar", new[] { "TOOLBAR" }),
            new PrefsTab("Pages", new[] { "PAGES" }),
            new PrefsTab("Paper", new[] { "PAPER" }),
        },
        ["editor"] = new[]
        {
            new PrefsTab("Typing", new[] { "CARET", "TYPING" }),
            new PrefsTab("Text", new[] { "TEXT DEFAULTS" }),
            new PrefsTab("Spacing", new[] { "SPACING" }),
            new PrefsTab("Lists", new[] { "SMART LISTS", "BULLET COLORS", "NUMBERED LISTS" }),
            new PrefsTab("Colors", new[] { "QUICK HIGHLIGHT", "TOOLBAR PALETTES" }),
        },
        ["data"] = new[]
        {
            new PrefsTab("Saving & backup", new[] { "SAVING", "STORAGE", "AUTOMATIC BACKUP" }),
            new PrefsTab("Import & export", new[] { "IMPORT & EXPORT" }),
            new PrefsTab("Maintenance", new[] { "MAINTENANCE" }),
        },
    };

    public static IEnumerable<string> Pages => Table.Keys;

    public static IReadOnlyList<PrefsTab>? For(string page) => Table.TryGetValue(page, out var t) ? t : null;

    public static string? TabOf(string page, string heading) =>
        For(page)?.FirstOrDefault(t => t.Headings.Any(h => string.Equals(h, heading, StringComparison.OrdinalIgnoreCase)))?.Name;
}
