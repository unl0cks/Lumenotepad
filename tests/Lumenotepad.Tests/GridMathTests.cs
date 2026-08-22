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
    [InlineData(PageStyles.Ruled, 0)]
    [InlineData(PageStyles.Ruled, 13)]
    [InlineData(PageStyles.Ruled, 40)]
    [InlineData(PageStyles.Ruled, 177)]
    [InlineData(PageStyles.RuledWide, 0)]
    [InlineData(PageStyles.RuledWide, 50)]
    [InlineData(PageStyles.RuledWide, 200)]
    public void SnapY_centersTheFirstTextRowBetweenRuleLines(string style, double input)
    {
        const double lineHeight = 18;
        double spacing = GridMath.StepFor(style);
        double snapped = GridMath.SnapY(input, style, lineHeight);

        double textTop = snapped + GridMath.ChromeTop;
        double gapAboveText = (spacing - lineHeight) / 2;
        double lineAboveText = textTop - gapAboveText;

        Assert.Equal(0, lineAboveText % spacing, 6);
    }

    [Fact]
    public void SnapY_movesByWholeRuleSteps()
    {
        double a = GridMath.SnapY(100, PageStyles.Ruled, 18);
        double b = GridMath.SnapY(100 + PageStyleGuides.RuleSpacing, PageStyles.Ruled, 18);
        Assert.Equal(PageStyleGuides.RuleSpacing, b - a);
    }

    [Theory]
    [InlineData(PageStyles.Ruled, 30, 28)]
    [InlineData(PageStyles.Ruled, 43, 56)]
    [InlineData(PageStyles.RuledWide, 30, 36)]
    [InlineData(PageStyles.RuledWide, 55, 72)]
    [InlineData(PageStyles.Grid, 29, 20)]
    public void SnapHeight_staysInWholeSteps(string style, double input, double expected) =>
        Assert.Equal(expected, GridMath.SnapHeight(input, style));

    [Fact]
    public void SnapX_leavesRuledPaperFree()
    {
        Assert.Equal(13.7, GridMath.SnapX(13.7, PageStyles.Ruled));
        Assert.Equal(13.7, GridMath.SnapX(13.7, PageStyles.RuledWide));
        Assert.Equal(20, GridMath.SnapX(13.7, PageStyles.Grid));
    }

    [Fact]
    public void RuleSpacing_matchesTheDrawnPaper()
    {
        Assert.Equal(28, PageStyleGuides.RuleSpacing);
        Assert.Equal(36, PageStyleGuides.RuleSpacingWide);
    }
}
