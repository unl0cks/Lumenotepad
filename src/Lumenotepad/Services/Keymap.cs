using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Lumenotepad.Services;

/// <summary>Custom keybindings (M8 Part 6): the editor's formatting shortcuts, rebindable from
/// Preferences → Shortcuts. Defaults match the combos the app always had; overrides come from
/// AppSettings.KeyOverrides as gesture strings ("Ctrl+Shift+H") — an invalid string silently
/// falls back to the default, so a hand-edited settings file can never brick typing. Structural
/// keys (Ctrl+A/Z/Y/C/X/V, navigation) are deliberately NOT here.</summary>
public static class Keymap
{
    public static readonly (string Action, string Label, string Default)[] Actions =
    {
        ("bold", "Bold", "Ctrl+B"),
        ("italic", "Italic", "Ctrl+I"),
        ("underline", "Underline", "Ctrl+U"),
        ("strike", "Strikethrough", "Ctrl+Shift+S"),
        ("highlight", "Quick highlight", "Ctrl+Shift+H"),
        ("date", "Insert date & time", "Ctrl+Shift+T"),
        ("bullets", "Bullet list", "Ctrl+Shift+D8"),
        ("numbers", "Numbered list", "Ctrl+Shift+D7"),
    };

    private static Dictionary<string, KeyGesture> _map = BuildDefaults();
    /// <summary>The same combos with Cmd substituted for Ctrl. macOS types every one of these with the
    /// Command key, which arrives as <see cref="KeyModifiers.Meta"/> and matches nothing in _map.</summary>
    private static Dictionary<string, KeyGesture> _mac = BuildMac(BuildDefaults());
    private static Dictionary<string, string> _overrides = new();

    /// <summary>The platform's primary shortcut modifier is down. Cmd counts on macOS; Ctrl counts
    /// everywhere, so no combo that worked before stops working.</summary>
    public static bool HasCommand(KeyModifiers m) =>
        m.HasFlag(KeyModifiers.Control) || (OperatingSystem.IsMacOS() && m.HasFlag(KeyModifiers.Meta));

    /// <summary>Strictly the platform's command modifier — Cmd on macOS, Ctrl elsewhere. Use this for
    /// MOUSE chords: on macOS Ctrl+click is how you right-click, so treating Ctrl as a command modifier
    /// there would fire the chord and open the context menu at the same time.</summary>
    public static bool HasCommandStrict(KeyModifiers m) =>
        m.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

    private static Dictionary<string, KeyGesture> BuildMac(Dictionary<string, KeyGesture> src)
    {
        var d = new Dictionary<string, KeyGesture>(src.Count);
        foreach (var (action, g) in src)
            d[action] = g.KeyModifiers.HasFlag(KeyModifiers.Control)
                ? new KeyGesture(g.Key, (g.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta)
                : g;
        return d;
    }

    private static Dictionary<string, KeyGesture> BuildDefaults()
    {
        var d = new Dictionary<string, KeyGesture>();
        foreach (var (action, _, def) in Actions) d[action] = KeyGesture.Parse(def);
        return d;
    }

    /// <summary>Replace the active overrides (null/empty = pure defaults). Unknown actions and
    /// unparseable gestures are ignored.</summary>
    public static void SetOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        var map = BuildDefaults();
        var kept = new Dictionary<string, string>();
        if (overrides is not null)
            foreach (var (action, gesture) in overrides)
                if (map.ContainsKey(action))
                    try
                    {
                        map[action] = KeyGesture.Parse(gesture);
                        kept[action] = gesture;
                    }
                    catch { /* invalid → the default stays */ }
        _map = map;
        _mac = BuildMac(map);
        _overrides = kept;
    }

    public static bool Matches(string action, KeyEventArgs e) =>
        (_map.TryGetValue(action, out var g) && g.Matches(e)) ||
        (OperatingSystem.IsMacOS() && _mac.TryGetValue(action, out var mg) && mg.Matches(e));

    public static bool IsDefault(string action) => !_overrides.ContainsKey(action);

    /// <summary>The active combo, prettified for people ("Ctrl+Shift+8", not "…D8").</summary>
    public static string DisplayFor(string action) =>
        _map.TryGetValue(action, out var g) ? Pretty(g.ToString()) : "";

    private static string Pretty(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bD(\d)\b", "$1");
        // The binding is stored as Ctrl+… on every platform, but macOS presses it with Command, so the
        // Shortcuts page would otherwise list combos that do not exist on her keyboard.
        return OperatingSystem.IsMacOS() ? s.Replace("Ctrl", "Cmd") : s;
    }

    /// <summary>Build a canonical gesture string from a captured key press; null = not bindable
    /// (a bare modifier, or an unmodified key that would break normal typing — F-keys excepted).</summary>
    public static string? FromEvent(KeyEventArgs e)
    {
        var k = e.Key;
        if (k is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
              or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return null;
        var m = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
        // Cmd is macOS's Ctrl. Fold it in so a combo captured there is bindable, and so it is STORED as
        // "Ctrl+…" — the settings file then means the same thing on both platforms.
        if (OperatingSystem.IsMacOS() && m.HasFlag(KeyModifiers.Meta))
            m = (m & ~KeyModifiers.Meta) | KeyModifiers.Control;
        bool fnKey = k >= Key.F1 && k <= Key.F24;
        if (!fnKey && (m & (KeyModifiers.Control | KeyModifiers.Alt)) == 0) return null;
        var parts = new List<string>(4);
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(k.ToString());
        return string.Join("+", parts);
    }
}
