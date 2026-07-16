using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class FontCatalogTests
{
    private const string GoogleJson = """
    {
      "familyMetadataList": [
        { "family": "Roboto", "category": "Sans Serif", "stroke": "", "popularity": 1, "subsets": ["latin"] },
        { "family": "Lobster", "category": "Display", "stroke": "", "popularity": 40, "subsets": ["latin"] },
        { "family": "Dancing Script", "category": "Handwriting", "stroke": "", "popularity": 20, "subsets": ["latin"] },
        { "family": "UnifrakturCook", "category": "Display", "stroke": "", "popularity": 900, "subsets": ["latin"] },
        { "family": "Fredoka", "category": "Sans Serif", "stroke": "", "popularity": 60, "subsets": ["latin"] },
        { "family": "Anton", "category": "Sans Serif", "stroke": "", "popularity": 30, "subsets": ["latin"] },
        { "family": "Noto Sans JP", "category": "Sans Serif", "stroke": "", "popularity": 5, "subsets": ["japanese"] }
      ]
    }
    """;

    private const string FontshareJson = """
    { "fonts": [
        { "name": "Satoshi", "slug": "satoshi", "category": "Sans" },
        { "name": "Sentient", "slug": "sentient", "category": "Serif, Display" },
        { "name": "Zodiak", "slug": "zodiak", "category": "Blackletter" }
    ] }
    """;

    [Fact]
    public void ParseGoogle_dropsNonLatin_tagsSourceAndId()
    {
        var cat = FontCatalog.ParseGoogle(GoogleJson);
        Assert.DoesNotContain(cat, f => f.Name == "Noto Sans JP");
        var roboto = cat.First(f => f.Name == "Roboto");
        Assert.Equal(FontCatalog.Google, roboto.Source);
        Assert.Equal("Roboto", roboto.Id);
        Assert.Equal("Sans Serif", roboto.Category);
    }

    [Fact]
    public void ParseGoogle_badJson_yieldsEmpty_neverThrows()
    {
        Assert.Empty(FontCatalog.ParseGoogle("not json"));
        Assert.Empty(FontCatalog.ParseGoogle("{}"));
    }

    [Fact]
    public void ParseFontshare_tagsSlugAsId_normalizesCategory()
    {
        var fs = FontCatalog.ParseFontshare(FontshareJson);
        Assert.Equal(3, fs.Count);
        var satoshi = fs.First(f => f.Name == "Satoshi");
        Assert.Equal(FontCatalog.Fontshare, satoshi.Source);
        Assert.Equal("satoshi", satoshi.Id);           // slug is the download key
        Assert.Equal("Sans Serif", satoshi.Category);  // "Sans" → "Sans Serif"
        Assert.Equal("Display", fs.First(f => f.Name == "Zodiak").Category);
        Assert.Equal("Blackletter", fs.First(f => f.Name == "Zodiak").Stroke);
    }

    [Theory]
    [InlineData("Sans", "Sans Serif", "")]
    [InlineData("Serif, Display", "Serif", "")]
    [InlineData("Slab", "Serif", "Slab Serif")]
    [InlineData("Blackletter", "Display", "Blackletter")]
    [InlineData("Script", "Handwriting", "")]
    public void NormalizeFontshareCategory_mapsToGoogleStyle(string raw, string cat, string stroke)
        => Assert.Equal((cat, stroke), FontCatalog.NormalizeFontshareCategory(raw));

    [Fact]
    public void Merge_interleavesFontshareThroughGoogleByPopularity()
    {
        var g = FontCatalog.ParseGoogle(GoogleJson);
        var f = FontCatalog.ParseFontshare(FontshareJson);
        var merged = FontCatalog.Merge(g, f);

        Assert.Equal(g.Count + f.Count, merged.Count);
        // Fontshare families are spread in, not all clustered at the end.
        int lastFontshareIndex = merged.Select((x, idx) => (x, idx))
            .Where(t => t.x.Source == FontCatalog.Fontshare).Max(t => t.idx);
        Assert.True(lastFontshareIndex < merged.Count - 1, "at least one Google font should follow the last Fontshare one");
        Assert.True(merged.Select(x => x.Popularity).SequenceEqual(merged.Select(x => x.Popularity).OrderBy(p => p)));
    }

    [Theory]
    [InlineData("Dancing Script", "Handwriting", "cursive", true)]
    [InlineData("Roboto", "Sans Serif", "sans", true)]
    [InlineData("UnifrakturCook", "Display", "gothic", true)]
    [InlineData("Fredoka", "Sans Serif", "cute", true)]
    [InlineData("Anton", "Sans Serif", "blocky", true)]
    [InlineData("Roboto", "Sans Serif", "handwriting", false)]
    public void MatchesCategory_appliesHeuristics(string name, string category, string key, bool expected)
    {
        var f = new FontCatalog.CatalogFont(name, FontCatalog.Google, name, category, "", 1);
        Assert.Equal(expected, FontCatalog.MatchesCategory(f, key));
    }

    [Fact]
    public void MatchesCategory_fontshareSlabIsBlocky_viaStroke()
    {
        var slab = new FontCatalog.CatalogFont("Erode", FontCatalog.Fontshare, "erode", "Serif", "Slab Serif", 5);
        Assert.True(FontCatalog.MatchesCategory(slab, "blocky"));
    }

    [Fact]
    public void Filter_combinesCategoryAndCaseInsensitiveName()
    {
        var cat = FontCatalog.ParseGoogle(GoogleJson);
        Assert.Contains(FontCatalog.Filter(cat, "sans", null), f => f.Name == "Roboto");
        Assert.DoesNotContain(FontCatalog.Filter(cat, "sans", null), f => f.Name == "Lobster");

        var q = FontCatalog.Filter(cat, "all", "script");
        Assert.Single(q);
        Assert.Equal("Dancing Script", q[0].Name);
    }

    [Fact]
    public void FontshareTtfUrl_extractsTruetype_makesAbsolute()
    {
        const string css = "@font-face{font-family:'X';src:url('//cdn.fontshare.com/wf/AAA/BBB/CCC.woff2') format('woff2')," +
                           "url('//cdn.fontshare.com/wf/AAA/BBB/CCC.ttf') format('truetype');}";
        Assert.Equal("https://cdn.fontshare.com/wf/AAA/BBB/CCC.ttf", FontPreviewRenderer.FontshareTtfUrl(css));
        Assert.Null(FontPreviewRenderer.FontshareTtfUrl("no font here"));
    }
}
