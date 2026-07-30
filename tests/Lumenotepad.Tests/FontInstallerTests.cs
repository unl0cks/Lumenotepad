using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class FontInstallerTests
{
    [Theory]
    [InlineData("fira sans", "Fira Sans")]
    [InlineData("LOBSTER", "Lobster")]
    [InlineData("  playfair   display  ", "Playfair Display")]
    public void TitleCase_matchesGoogleCatalogSpelling(string input, string expected)
        => Assert.Equal(expected, FontInstaller.TitleCase(input));

    [Fact]
    public void GoogleCssUrl_encodesFamilyWithPlus_andRequestsBoldItalic()
    {
        var url = FontInstaller.GoogleCssUrl("Fira Sans");
        Assert.Contains("family=Fira+Sans", url);
        Assert.Contains("ital,wght@0,400;0,700;1,400", url);
    }

    [Fact]
    public void ParseCssFontUrls_extractsEveryDistinctUrl()
    {
        const string css =
            "@font-face{font-family:'X';src:url(https://a.test/1.ttf) format('truetype');}" +
            "@font-face{font-family:'X';src:url(https://a.test/2.ttf) format('truetype');}" +
            "@font-face{font-family:'X';src:url(https://a.test/1.ttf) format('truetype');}";
        var urls = FontInstaller.ParseCssFontUrls(css);
        Assert.Equal(2, urls.Count);
        Assert.Contains("https://a.test/1.ttf", urls);
        Assert.Contains("https://a.test/2.ttf", urls);
    }

    [Fact]
    public void ChooseFontEntries_prefersStaticTtf_dropsWebAndVariable()
    {
        var entries = new[]
        {
            "Font_Complete/Fonts/TTF/Font-Regular.ttf",
            "Font_Complete/Fonts/TTF/Font-Bold.ttf",
            "Font_Complete/Fonts/TTF/Font-Variable.ttf",
            "Font_Complete/Fonts/OTF/Font-Regular.otf",
            "Font_Complete/Fonts/WEB/fonts/Font-Regular.woff2",
            "Font_Complete/Fonts/WEB/fonts/Font-Regular.ttf",
        };
        var chosen = FontInstaller.ChooseFontEntries(entries);
        Assert.Equal(2, chosen.Count);
        Assert.All(chosen, c => Assert.EndsWith(".ttf", c));
        Assert.All(chosen, c => Assert.DoesNotContain("/WEB/", c));
        Assert.DoesNotContain(chosen, c => c.Contains("Variable"));
    }

    [Fact]
    public void ChooseFontEntries_fallsBackToOtf_whenNoStaticTtf()
    {
        var entries = new[]
        {
            "F/OTF/F-Regular.otf",
            "F/OTF/F-Bold.otf",
            "F/TTF/F-Variable.ttf",
        };
        var chosen = FontInstaller.ChooseFontEntries(entries);
        Assert.Equal(2, chosen.Count);
        Assert.All(chosen, c => Assert.EndsWith(".otf", c));
    }

    [Fact]
    public void ChooseFontEntries_capsFloodOfFaces()
    {
        var many = Enumerable.Range(0, 40).Select(i => $"F/TTF/F-{i}.ttf").ToArray();
        Assert.Equal(20, FontInstaller.ChooseFontEntries(many).Count);
    }

    [Fact]
    public void SafeName_stripsPathSeparators()
    {
        var safe = FontInstaller.SafeName("Font/Bold:Regular.ttf");
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain(':', safe);
    }
}
