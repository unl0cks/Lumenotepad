using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

/// <summary>The owner's theme matrix: Theme = the FRAME; Full theme OFF = contrasting paper
/// (glass paper under solid frames, solid paper under the Lumen glass frame); ON = paper matches.</summary>
public class ThemePalettesTests
{
    private static bool IsGlassy(string hex) =>       // low-alpha overlay = glass material
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
        Assert.True(IsGlassy(t.FrameBackground));      // frame stays glass
        Assert.True(IsOpaque(t.PaperBackground));
        Assert.NotEqual("#FFFFFFFF", t.PaperText);     // dark text on the light paper
        Assert.Equal("#FFFFFFFF", t.TextPrimary);      // frame text still light
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
        Assert.True(IsOpaque(t.CanvasBackground));     // the body around the page is NOT glass
        Assert.True(IsGlassy(t.PaperBackground));      // the page box is REAL glass (acrylic hole)
        Assert.Equal("#FFFFFFFF", t.PaperText);        // glass reads light on every theme
        Assert.Equal(t.TextPrimary, t.CanvasText);     // solid surroundings keep theme text
        Assert.Equal(darkChrome, t.DarkChrome);
        Assert.True(t.GlassWindow);                    // acrylic needed for the page hole
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
        Assert.Equal(t.TextPrimary, t.PaperText);      // one text family everywhere
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
        // non-accent tokens untouched
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
}
