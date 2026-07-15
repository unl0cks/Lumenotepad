using System.Collections.Generic;
using Avalonia.Input;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class KeymapTests
{
    private static KeyEventArgs Ev(Key key, KeyModifiers mods) =>
        new() { Key = key, KeyModifiers = mods };

    [Fact]
    public void Defaults_matchTheHistoricCombos()
    {
        Keymap.SetOverrides(null);
        Assert.True(Keymap.Matches("bold", Ev(Key.B, KeyModifiers.Control)));
        Assert.True(Keymap.Matches("strike", Ev(Key.S, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.True(Keymap.Matches("bullets", Ev(Key.D8, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.False(Keymap.Matches("bold", Ev(Key.B, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.All(Keymap.Actions, a => Assert.True(Keymap.IsDefault(a.Action)));
    }

    [Fact]
    public void Overrides_rebind_invalidFallsBack_unknownIgnored()
    {
        Keymap.SetOverrides(new Dictionary<string, string>
        {
            ["bold"] = "Ctrl+Alt+B",
            ["italic"] = "not a gesture !!",
            ["nosuch"] = "Ctrl+Q",
        });
        Assert.True(Keymap.Matches("bold", Ev(Key.B, KeyModifiers.Control | KeyModifiers.Alt)));
        Assert.False(Keymap.Matches("bold", Ev(Key.B, KeyModifiers.Control)));
        Assert.False(Keymap.IsDefault("bold"));
        Assert.True(Keymap.Matches("italic", Ev(Key.I, KeyModifiers.Control)));   // invalid → default
        Assert.True(Keymap.IsDefault("italic"));
        Keymap.SetOverrides(null);                        // leave the statics clean for other tests
    }

    [Fact]
    public void DisplayFor_prettifiesDigitKeys()
    {
        Keymap.SetOverrides(null);
        Assert.Equal("Ctrl+Shift+8", Keymap.DisplayFor("bullets"));
        Assert.Equal("Ctrl+B", Keymap.DisplayFor("bold"));
    }

    [Fact]
    public void FromEvent_buildsCanonicalGestures_rejectsUnbindable()
    {
        Assert.Equal("Ctrl+Shift+H", Keymap.FromEvent(Ev(Key.H, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.Equal("Ctrl+Alt+D5", Keymap.FromEvent(Ev(Key.D5, KeyModifiers.Control | KeyModifiers.Alt)));
        Assert.Equal("F6", Keymap.FromEvent(Ev(Key.F6, KeyModifiers.None)));       // F-keys may bind bare
        Assert.Null(Keymap.FromEvent(Ev(Key.LeftCtrl, KeyModifiers.Control)));     // bare modifier
        Assert.Null(Keymap.FromEvent(Ev(Key.K, KeyModifiers.None)));               // would break typing
        Assert.Null(Keymap.FromEvent(Ev(Key.K, KeyModifiers.Shift)));              // Shift alone too
    }

    [Fact]
    public void FromEvent_roundTripsThroughParse()
    {
        var s = Keymap.FromEvent(Ev(Key.D7, KeyModifiers.Control | KeyModifiers.Shift))!;
        Keymap.SetOverrides(new Dictionary<string, string> { ["numbers"] = s });
        Assert.True(Keymap.Matches("numbers", Ev(Key.D7, KeyModifiers.Control | KeyModifiers.Shift)));
        Keymap.SetOverrides(null);
    }
}
