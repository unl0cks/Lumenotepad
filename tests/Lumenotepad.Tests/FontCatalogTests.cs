using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class FontCatalogTests
{
    private const string SampleJson = """
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

    [Fact]
    public void Parse_dropsNonLatin_keepsFieldsAndSortsByPopularity()
    {
        var cat = FontCatalog.Parse(SampleJson);
        Assert.DoesNotContain(cat, f => f.Name == "Noto Sans JP");   // non-latin dropped
        Assert.Equal("Roboto", cat[0].Name);                          // most popular first
        Assert.Equal("Sans Serif", cat[0].Category);
        Assert.True(cat.Select(f => f.Popularity).SequenceEqual(cat.Select(f => f.Popularity).OrderBy(p => p)));
    }

    [Fact]
    public void Parse_badJson_yieldsEmpty_neverThrows()
    {
        Assert.Empty(FontCatalog.Parse("not json"));
        Assert.Empty(FontCatalog.Parse("{}"));
    }

    [Theory]
    [InlineData("Dancing Script", "Handwriting", "handwriting", true)]
    [InlineData("Dancing Script", "Handwriting", "cursive", true)]      // name matches "dancing"
    [InlineData("Roboto", "Sans Serif", "handwriting", false)]
    [InlineData("Roboto", "Sans Serif", "sans", true)]
    [InlineData("Lobster", "Display", "fancy", true)]
    [InlineData("UnifrakturCook", "Display", "gothic", true)]           // name matches "unifraktur"
    [InlineData("Fredoka", "Sans Serif", "cute", true)]                 // name matches "fredoka"
    [InlineData("Anton", "Sans Serif", "blocky", true)]                 // name matches "anton"
    [InlineData("Roboto", "Sans Serif", "all", true)]
    public void MatchesCategory_appliesHeuristics(string name, string category, string key, bool expected)
    {
        var f = new FontCatalog.CatalogFont(name, category, "", 1);
        Assert.Equal(expected, FontCatalog.MatchesCategory(f, key));
    }

    [Fact]
    public void Filter_combinesCategoryAndCaseInsensitiveName()
    {
        var cat = FontCatalog.Parse(SampleJson);
        var sans = FontCatalog.Filter(cat, "sans", null);
        Assert.Contains(sans, f => f.Name == "Roboto");
        Assert.DoesNotContain(sans, f => f.Name == "Lobster");

        var q = FontCatalog.Filter(cat, "all", "script");
        Assert.Single(q);
        Assert.Equal("Dancing Script", q[0].Name);

        Assert.Equal(cat.Count, FontCatalog.Filter(cat, "all", "   ").Count);   // blank query = no name filter
    }
}
