using Lumenotepad.Editor;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStylesTests
{
    [Theory]
    [InlineData("None", "Blank")]
    [InlineData("Dots", "Dots")]
    [InlineData("Lines", "Grid")]
    [InlineData("garbage", "Blank")]
    public void MapGlobalGrid_mapsPart3Keys(string global, string expected) =>
        Assert.Equal(expected, PageStyles.MapGlobalGrid(global));

    [Theory]
    [InlineData("Blank", "Blank")]
    [InlineData("Ruled", "Ruled")]
    [InlineData("Grid", "Grid")]
    public void MapGlobalGrid_passesThroughNewKeys(string stored, string expected) =>
        Assert.Equal(expected, PageStyles.MapGlobalGrid(stored));

    [Fact]
    public void EffectiveGrid_pageOverNotebookOverGlobal()
    {
        var nb = new Notebook();
        var pg = new Page();
        Assert.Equal("Blank", PageStyles.EffectiveGrid(pg, nb, "None"));      // all inherit → global
        nb.DefaultGridStyle = "Ruled";
        Assert.Equal("Ruled", PageStyles.EffectiveGrid(pg, nb, "None"));      // notebook wins
        pg.GridStyle = "Dots";
        Assert.Equal("Dots", PageStyles.EffectiveGrid(pg, nb, "None"));       // page wins
    }

    [Fact]
    public void EffectiveStyle_pageOverridesNotebook_modeFollowsOwner()
    {
        var nb = new Notebook { DefaultPageStyle = "Cornell", DefaultPageStyleMode = 2 };
        var pg = new Page();
        Assert.Equal(("Cornell", 2), PageStyles.EffectiveStyle(pg, nb));      // inherit both
        pg.PageStyle = "Boxing";
        pg.PageStyleMode = 1;
        Assert.Equal(("Boxing", 1), PageStyles.EffectiveStyle(pg, nb));       // page wins both
    }

    [Fact]
    public void Defaults_freeformAndInherit()
    {
        var nb = new Notebook();
        Assert.Null(nb.DefaultGridStyle);
        Assert.Equal("Freeform", nb.DefaultPageStyle);
        Assert.Equal(0, nb.DefaultPageStyleMode);
        Assert.Null(nb.DefaultFont);
        Assert.Equal(15, nb.DefaultFontSize);
        var pg = new Page();
        Assert.Null(pg.GridStyle);
        Assert.Null(pg.PageStyle);
        Assert.Equal(0, pg.PageStyleMode);
    }
}
