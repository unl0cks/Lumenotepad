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
        // No notes foot passed → the summary rests at its 80%-of-screen home (0.80×600 = 480) and the
        // cue rule runs from the top down to meet it. Full canvas width for the summary rule.
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas);
        Assert.Equal(2, g.Lines.Count);
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void Cornell_summaryDescendsBelowNotes_asContentGrows()
    {
        // Notes content reaching y=1000 pushes the summary a small gap (0.05×600 = 30) below it →
        // 1030, and the cue rule follows down to meet it. Line and the docked summary box share this
        // exact math (PageStyleGuides.CornellMetrics), so they never drift apart.
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas, contentBottom: 1000);
        Assert.Equal((new Point(252, 0), new Point(252, 1030)), g.Lines[0]);
        Assert.Equal((new Point(0, 1030), new Point(1200, 1030)), g.Lines[1]);
    }

    [Fact]
    public void Cornell_shortNotes_summaryStaysAtHome()
    {
        // Notes ending high on the page (y=100) don't drag the summary up above its 80% home: it stays
        // at 480, so a nearly-empty Cornell page keeps its familiar first-screen layout.
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas, contentBottom: 100);
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);
    }

    [Fact]
    public void CornellRegions_boxRects_matchTheDividerGeometry()
    {
        // The docked boxes snap to these rects; their edges line up with the guide dividers above.
        var (cue, notes, summary) = PageStyleGuides.CornellRegions(900, 600, notesFoot: 0);
        Assert.Equal(new Rect(16, 16, 220, 0), cue);        // cue.right = 236, just left of the x=252 divider
        Assert.Equal(new Rect(268, 16, 616, 0), notes);     // notes.left = 268, just right of it
        Assert.Equal(new Rect(16, 492, 868, 0), summary);   // summary top = 480 rule + 12 gap
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
        Assert.Equal((new Point(0, 64), new Point(1200, 64)), g.Lines[2]);    // header underline
    }

    [Fact]
    public void Boxing_fourRects()
    {
        var g = PageStyleGuides.For(PageStyles.Boxing, Vp, Canvas);
        Assert.Empty(g.Lines);
        Assert.Equal(4, g.Boxes.Count);
        Assert.Equal(new Rect(24, 24, 418, 268), g.Boxes[0]);   // (900−48−16)/2 × (600−48−16)/2
        Assert.Equal(new Rect(458, 24, 418, 268), g.Boxes[1]);
        Assert.Equal(new Rect(24, 308, 418, 268), g.Boxes[2]);
        Assert.Equal(new Rect(458, 308, 418, 268), g.Boxes[3]);
    }

    [Fact]
    public void Sentence_ruledLinesEvery28_fromY48_downTheCanvas()
    {
        var g = PageStyleGuides.For(PageStyles.Sentence, Vp, Canvas);
        Assert.Equal(52, g.Lines.Count);                        // 48 + 28k ≤ 1500 → k = 0..51
        Assert.Equal((new Point(0, 48), new Point(1200, 48)), g.Lines[0]);
        Assert.Equal(76, g.Lines[1].A.Y);
    }

    [Fact]
    public void ZeroViewport_fallsBackToCanvasSize()
    {
        var g = PageStyleGuides.For(PageStyles.TwoColumn, default, Canvas);
        Assert.Equal(600, Assert.Single(g.Lines).A.X);          // 0.5 × canvas width
    }
}
