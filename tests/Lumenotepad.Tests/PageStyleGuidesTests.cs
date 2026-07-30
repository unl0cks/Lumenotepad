using System.Linq;
using Avalonia;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStyleGuidesTests
{
    private static readonly Size Vp = new(900, 600);
    private static readonly Size Canvas = new(1200, 1500);

    [Theory]
    [InlineData("Freeform")]
    [InlineData("Mindmap")]
    public void FreeformAndMindmap_drawNothing(string style)
    {
        var g = PageStyleGuides.For(style, Vp, Canvas);
        Assert.Empty(g.Lines);
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void Cornell_default_summaryAtEightyPercent_cueRunsDownToIt()
    {

        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas);
        Assert.Equal(2, g.Lines.Count);
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void Cornell_summaryDescendsBelowNotes_asContentGrows()
    {

        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas, contentBottom: 1000);
        Assert.Equal((new Point(252, 0), new Point(252, 1030)), g.Lines[0]);
        Assert.Equal((new Point(0, 1030), new Point(1200, 1030)), g.Lines[1]);
    }

    [Fact]
    public void Cornell_shortNotes_summaryStaysAtHome()
    {

        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas, contentBottom: 100);
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);
    }

    [Fact]
    public void CornellRegions_boxRects_matchTheDividerGeometry()
    {

        var (cue, notes, summary) = PageStyleGuides.CornellRegions(900, 600, notesFoot: 0);
        Assert.Equal(new Rect(16, 16, 220, 0), cue);
        Assert.Equal(new Rect(268, 16, 616, 0), notes);
        Assert.Equal(new Rect(16, 492, 868, 0), summary);
    }

    [Fact]
    public void Regions_dockRects_matchTheGriddedStyleGeometry()
    {
        var two = PageStyleGuides.Regions(PageStyles.TwoColumn, Vp, Canvas);
        Assert.Equal(new[] { "c0", "c1" }, two.Select(r => r.Id));
        Assert.Equal(new Rect(16, 16, 418, 0), two[0].Rect);
        Assert.Equal(new Rect(466, 16, 418, 0), two[1].Rect);

        var box = PageStyleGuides.Regions(PageStyles.Boxing, Vp, Canvas);
        Assert.Equal(4, box.Count);
        Assert.Equal(new Rect(36, 36, 394, 0), box[0].Rect);
        Assert.Equal(new Rect(470, 36, 394, 0), box[1].Rect);

        var chart = PageStyleGuides.Regions(PageStyles.Charting, Vp, Canvas);
        Assert.Equal(new[] { "h0", "h1", "h2" }, chart.Select(r => r.Id));
        Assert.Equal(316, chart[1].Rect.X);

        Assert.Empty(PageStyleGuides.Regions(PageStyles.Freeform, Vp, Canvas));
        Assert.Empty(PageStyleGuides.Regions(PageStyles.Mindmap, Vp, Canvas));
    }

    [Fact]
    public void TwoColumn_singleDivider_fullCanvasHeight()
    {
        var g = PageStyleGuides.For(PageStyles.TwoColumn, Vp, Canvas);
        var line = Assert.Single(g.Lines);
        Assert.Equal((new Point(450, 0), new Point(450, 1500)), line);
    }

    [Fact]
    public void Outline_threeIndentStops()
    {
        var g = PageStyleGuides.For(PageStyles.Outline, Vp, Canvas);
        Assert.Equal(3, g.Lines.Count);
        Assert.Equal(48, g.Lines[0].A.X);
        Assert.Equal(88, g.Lines[1].A.X);
        Assert.Equal(128, g.Lines[2].A.X);
        Assert.All(g.Lines, l => Assert.Equal(1500, l.B.Y));
    }

    [Fact]
    public void Charting_threeColumnsPlusHeader()
    {
        var g = PageStyleGuides.For(PageStyles.Charting, Vp, Canvas);
        Assert.Equal(3, g.Lines.Count);
        Assert.Equal((new Point(300, 0), new Point(300, 1500)), g.Lines[0]);
        Assert.Equal((new Point(600, 0), new Point(600, 1500)), g.Lines[1]);
        Assert.Equal((new Point(0, 64), new Point(1200, 64)), g.Lines[2]);
    }

    [Fact]
    public void Boxing_fourRects()
    {
        var g = PageStyleGuides.For(PageStyles.Boxing, Vp, Canvas);
        Assert.Empty(g.Lines);
        Assert.Equal(4, g.Boxes.Count);
        Assert.Equal(new Rect(24, 24, 418, 268), g.Boxes[0]);
        Assert.Equal(new Rect(458, 24, 418, 268), g.Boxes[1]);
        Assert.Equal(new Rect(24, 308, 418, 268), g.Boxes[2]);
        Assert.Equal(new Rect(458, 308, 418, 268), g.Boxes[3]);
    }

    [Fact]
    public void Sentence_ruledLinesEvery28_fromY48_downTheCanvas()
    {
        var g = PageStyleGuides.For(PageStyles.Sentence, Vp, Canvas);
        Assert.Equal(52, g.Lines.Count);
        Assert.Equal((new Point(0, 48), new Point(1200, 48)), g.Lines[0]);
        Assert.Equal(76, g.Lines[1].A.Y);
    }

    [Fact]
    public void ZeroViewport_fallsBackToCanvasSize()
    {
        var g = PageStyleGuides.For(PageStyles.TwoColumn, default, Canvas);
        Assert.Equal(600, Assert.Single(g.Lines).A.X);
    }
}
