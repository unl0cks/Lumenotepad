using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class GridMathTests
{

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(11, 20)]
    [InlineData(20, 20)]
    [InlineData(29, 20)]
    [InlineData(31, 40)]
    [InlineData(347, 340)]
    public void Snap_landsOnNearestCell(double input, double expected) =>
        Assert.Equal(expected, GridMath.Snap(input));

    [Theory]
    [InlineData(PageStyles.Blank, 11, 20)]
    [InlineData(PageStyles.Grid, 29, 20)]
    [InlineData(PageStyles.Dots, 31, 40)]
    public void SnapY_followsTheCellForSquareGrids(string style, double input, double expected) =>
        Assert.Equal(expected, GridMath.SnapY(input, style));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 0)]
    [InlineData(15, 28)]
    [InlineData(28, 28)]
    [InlineData(41, 28)]
    [InlineData(43, 56)]
    public void SnapY_landsOnRuleLinesForRuledPaper(double input, double expected) =>
        Assert.Equal(expected, GridMath.SnapY(input, PageStyles.Ruled));

    [Fact]
    public void SnapX_leavesRuledPaperFree()
    {
        Assert.Equal(13.7, GridMath.SnapX(13.7, PageStyles.Ruled));
        Assert.Equal(20, GridMath.SnapX(13.7, PageStyles.Grid));
    }

    [Fact]
    public void RuleSpacing_matchesTheDrawnPaper() =>
        Assert.Equal(28, PageStyleGuides.RuleSpacing);
}
