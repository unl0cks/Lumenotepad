using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Lumenotepad.Services;

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

    private static Dictionary<string, KeyGesture> _mac = BuildMac(BuildDefaults());
    private static Dictionary<string, string> _overrides = new();

    public static bool HasCommand(KeyModifiers m) =>
        m.HasFlag(KeyModifiers.Control) || (OperatingSystem.IsMacOS() && m.HasFlag(KeyModifiers.Meta));

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
                    catch {  }
        _map = map;
        _mac = BuildMac(map);
        _overrides = kept;
    }

    public static bool Matches(string action, KeyEventArgs e) =>
        (_map.TryGetValue(action, out var g) && g.Matches(e)) ||
        (OperatingSystem.IsMacOS() && _mac.TryGetValue(action, out var mg) && mg.Matches(e));

    public static bool IsDefault(string action) => !_overrides.ContainsKey(action);

    public static string DisplayFor(string action) =>
        _map.TryGetValue(action, out var g) ? Pretty(g.ToString()) : "";

    private static string Pretty(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bD(\d)\b", "$1");

        return OperatingSystem.IsMacOS() ? s.Replace("Ctrl", "Cmd") : s;
    }

    public static string? FromEvent(KeyEventArgs e)
    {
        var k = e.Key;
        if (k is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
              or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return null;
        var m = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);

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
