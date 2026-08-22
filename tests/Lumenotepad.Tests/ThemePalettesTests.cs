using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class ThemePalettesTests
{
    private static bool IsGlassy(string hex) =>
        byte.Parse(hex.TrimStart('#')[..2], System.Globalization.NumberStyles.HexNumber) < 0x40;

    private static bool IsOpaque(string hex) =>
        hex.TrimStart('#').Length == 6 ||
        byte.Parse(hex.TrimStart('#')[..2], System.Globalization.NumberStyles.HexNumber) >= 0xF0;

    private static bool IsTranslucent(string hex) => !IsOpaque(hex) &&
        byte.Parse(hex.TrimStart('#')[..2], System.Globalization.NumberStyles.HexNumber) > 0x00;

    [Fact]
    public void Lumen_fullOff_isGlassFrameWithSolidDarkPaper()
    {
        var t = ThemePalettes.Resolve("Lumen", fullTheme: false, paperLight: false);
        Assert.True(IsGlassy(t.FrameBackground));
        Assert.True(IsOpaque(t.PaperBackground));
        Assert.Equal("#FFFFFFFF", t.PaperText);
        Assert.True(t.DarkChrome);
        Assert.True(t.GlassWindow);
    }

    [Fact]
    public void Lumen_fullOff_lightPaperToggle_flipsPaperOnly()
    {
        var t = ThemePalettes.Resolve("Lumen", fullTheme: false, paperLight: true);
        Assert.True(IsGlassy(t.FrameBackground));
        Assert.True(IsOpaque(t.PaperBackground));
        Assert.NotEqual("#FFFFFFFF", t.PaperText);
        Assert.Equal("#FFFFFFFF", t.TextPrimary);
    }

    [Fact]
    public void Lumen_fullOn_paperMatchesGlass()
    {
        var t = ThemePalettes.Resolve("Lumen", fullTheme: true, paperLight: false);
        Assert.True(IsGlassy(t.PaperBackground));
        Assert.Equal("#FFFFFFFF", t.PaperText);
    }

    [Theory]
    [InlineData("Dark", true)]
    [InlineData("Light", false)]
    [InlineData("Pink", false)]
    [InlineData("Light blue", false)]
    public void SolidThemes_fullOff_solidCanvasWithRealGlassPageOnly(string theme, bool darkChrome)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: false, paperLight: false);
        Assert.True(IsOpaque(t.FrameBackground));
        Assert.True(IsOpaque(t.CanvasBackground));
        Assert.True(IsGlassy(t.PaperBackground));
        Assert.Equal("#FFFFFFFF", t.PaperText);
        Assert.Equal(t.TextPrimary, t.CanvasText);
        Assert.Equal(darkChrome, t.DarkChrome);
        Assert.True(t.GlassWindow);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Pink")]
    [InlineData("Light blue")]
    public void SolidThemes_fullOn_noGlassAnywhere(string theme)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: true, paperLight: false);
        Assert.True(IsOpaque(t.FrameBackground));
        Assert.True(IsOpaque(t.CanvasBackground));
        Assert.False(IsTranslucent(t.PaperBackground));
        Assert.False(t.GlassWindow);
        Assert.Equal(t.TextPrimary, t.PaperText);
    }

    [Theory]
    [InlineData("Lumen", false, true)]
    [InlineData("Lumen", true, true)]
    [InlineData("Dark", false, false)]
    [InlineData("Pink", false, false)]
    [InlineData("Light", true, false)]
    public void FrostedWindow_onlyWhenFrameItselfIsGlass(string theme, bool full, bool expected)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: full, paperLight: false);
        Assert.Equal(expected, t.FrostedWindow);
    }

    [Fact]
    public void ThemesCarryTheirOwnAccents()
    {
        Assert.Equal("#FB6F92", ThemePalettes.Resolve("Pink", true, false).Accent);
        Assert.Equal("#5C85E6", ThemePalettes.Resolve("Light blue", true, false).Accent);
        Assert.Equal("#4DA6FF", ThemePalettes.Resolve("Lumen", false, false).Accent);
    }

    [Fact]
    public void UnknownThemeFallsBackToLumen()
    {
        var t = ThemePalettes.Resolve("Banana", false, false);
        Assert.Equal("#4DA6FF", t.Accent);
        Assert.True(t.GlassWindow);
    }

    [Fact]
    public void ColorMath_shadeAndAlpha()
    {
        Assert.Equal("#FF000000", ThemePalettes.Shade("#FF808080", -1));
        Assert.Equal("#FFFFFFFF", ThemePalettes.Shade("#FF808080", 1));
        Assert.Equal("#554DA6FF", ThemePalettes.Alpha("#4DA6FF", 0x55));
    }

    [Fact]
    public void WithAccent_RecomputesEveryAccentDerivedToken()
    {
        var t = ThemePalettes.Resolve("Lumen", false, false);
        var seeded = ThemePalettes.WithAccent(t, "#E27BA6");

        Assert.Equal("#E27BA6", seeded.Accent);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", 0.15), seeded.AccentHover);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x38), seeded.AccentSoft);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", -0.28), seeded.AccentDeep);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", 0.12), seeded.AccentGradTop);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", -0.10), seeded.AccentGradBottom);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x78), seeded.FieldSelection);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x4D), seeded.NoteChromeFocus);

        Assert.Equal(t.FrameBackground, seeded.FrameBackground);
        Assert.Equal(t.PaperBackground, seeded.PaperBackground);
        Assert.Equal(t.TextPrimary, seeded.TextPrimary);
    }

    [Theory]
    [InlineData("4da6ff", "#4DA6FF")]
    [InlineData("#4DA6FF", "#4DA6FF")]
    [InlineData("  #e27ba6 ", "#E27BA6")]
    public void NormalizeHex_AcceptsSixHexDigits(string input, string expected) =>
        Assert.Equal(expected, ThemePalettes.NormalizeHex(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyzxyz")]
    [InlineData("#4DA6FF00")]
    [InlineData("#4DA")]
    public void NormalizeHex_RejectsInvalid(string? input) =>
        Assert.Null(ThemePalettes.NormalizeHex(input));

    [Theory]
    [InlineData("#4DA6FF", "#FFFFFFFF")]
    [InlineData("#FB6F92", "#FF23262F")]
    [InlineData("#5C85E6", "#FFFFFFFF")]
    [InlineData("#101218", "#FFFFFFFF")]
    [InlineData("#F2D24B", "#FF23262F")]
    [InlineData("#FFFFFF", "#FF23262F")]
    [InlineData("#96CCFF", "#FF23262F")]
    public void IdealTextOn_picksAReadableInk(string accent, string expected) =>
        Assert.Equal(expected, ThemePalettes.IdealTextOn(accent));

    [Theory]
    [InlineData("Lumen")]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Pink")]
    [InlineData("Light blue")]
    public void EveryTheme_carriesAnAccentInk(string theme)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: true, paperLight: false);
        Assert.Equal(ThemePalettes.IdealTextOn(t.Accent), t.AccentText);
    }

    [Fact]
    public void WithAccent_recomputesTheInk()
    {
        var t = ThemePalettes.Resolve("Lumen", false, false);
        var seeded = ThemePalettes.WithAccent(t, "#F2D24B");
        Assert.Equal("#FF23262F", seeded.AccentText);
    }

    [Fact]
    public void Dark_fullOn_lightPaperToggle_flipsPaperOnly()
    {
        var t = ThemePalettes.Resolve("Dark", fullTheme: true, paperLight: true);
        Assert.Equal("#F7F9FD", t.PaperBackground);
        Assert.Equal("#FF23262F", t.PaperText);
        Assert.True(t.DarkChrome);
        Assert.Equal("#101218", t.CanvasBackground);
        Assert.Equal("#FFFFFFFF", t.TextPrimary);
    }

    [Fact]
    public void Dark_fullOff_ignoresLightPaper()
    {
        var t = ThemePalettes.Resolve("Dark", fullTheme: false, paperLight: true);
        Assert.True(IsGlassy(t.PaperBackground));
        Assert.Equal("#FFFFFFFF", t.PaperText);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Pink")]
    [InlineData("Light blue")]
    public void LightThemes_fullOn_darkPaperToggle_flipsPaperOnly(string theme)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: true, paperLight: false, paperDark: true);
        Assert.Equal("#1A1C24", t.PaperBackground);
        Assert.Equal("#FFFFFFFF", t.PaperText);
        Assert.False(t.DarkChrome);
        Assert.Equal(t.TextPrimary, t.CanvasText);
    }

    [Fact]
    public void LightThemes_fullOff_ignoreDarkPaper()
    {
        var t = ThemePalettes.Resolve("Pink", fullTheme: false, paperLight: false, paperDark: true);
        Assert.True(IsGlassy(t.PaperBackground));
    }

    [Fact]
    public void Dark_ignoresDarkPaperToggle()
    {
        var with = ThemePalettes.Resolve("Dark", fullTheme: true, paperLight: false, paperDark: true);
        var without = ThemePalettes.Resolve("Dark", fullTheme: true, paperLight: false, paperDark: false);
        Assert.Equal(without, with);
    }

    [Theory]
    [InlineData("#FF14161C", "#FF161616")]
    [InlineData("#F5171922", "#F5191919")]
    [InlineData("#101218", "#FF121212")]
    [InlineData("#FF292C34", "#FF2C2C2C")]
    [InlineData("#4DA6FF", "#4DA6FF")]
    [InlineData("#33FFFFFF", "#33FFFFFF")]
    [InlineData("#1F000000", "#1F000000")]
    [InlineData("#FB6F92", "#FB6F92")]
    public void Neutral_greysOnlyDarkBlueLeaningColors(string input, string expected) =>
        Assert.Equal(expected, ThemePalettes.Neutral(input));

    [Fact]
    public void Neutralize_greysSurfacesAndKeepsAccents()
    {
        var t = ThemePalettes.Neutralize(ThemePalettes.Resolve("Dark", fullTheme: true, paperLight: false));
        Assert.Equal("#4DA6FF", t.Accent);
        Assert.Equal("#FF121212", t.CanvasBackground);
        Assert.Equal("#FF1C1C1C", t.PaperBackground);
        Assert.Equal("#FF161616", t.WindowBackground);
        Assert.Equal("#FFFFFFFF", t.TextPrimary);
    }
}
