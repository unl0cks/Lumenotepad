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
    public void SolidThemes_fullOff_solidCanvasWithFrostedPaperOnly(string theme, bool darkChrome)
    {
        var t = ThemePalettes.Resolve(theme, fullTheme: false, paperLight: false);
        Assert.True(IsOpaque(t.FrameBackground));
        Assert.True(IsOpaque(t.CanvasBackground));     // the body around the page is NOT glass
        Assert.True(IsTranslucent(t.PaperBackground)); // only the page box reads frosted
        Assert.Equal(t.TextPrimary, t.PaperText);      // one text family — frost sits on the canvas
        Assert.Equal(t.TextPrimary, t.CanvasText);
        Assert.Equal(darkChrome, t.DarkChrome);
        Assert.False(t.GlassWindow);                   // no acrylic backdrop outside Lumen
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
}
