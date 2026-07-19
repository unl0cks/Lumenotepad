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
    public void Cornell_cueRunsFullHeight_summaryBandPinnedToCanvasFoot()
    {
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas);
        Assert.Equal(2, g.Lines.Count);
        // cue column x = 0.28×900 = 252; the rule runs the full page down to the summary band.
        // Summary band = 0.20×600 = 120 tall, pinned to the canvas foot: 1500 − 120 = 1380.
        Assert.Equal((new Point(252, 0), new Point(252, 1380)), g.Lines[0]);
        Assert.Equal((new Point(0, 1380), new Point(1200, 1380)), g.Lines[1]);  // summary: full canvas width
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void Cornell_summaryBandHugsContentFoot_notThePaddedCanvas()
    {
        // When the real content foot is passed it wins over the padded canvas: a page whose content
        // ends at y=1000 puts the summary a band (0.20×600 = 120) above it → 1000 − 120 = 880,
        // instead of the canvas-foot 1380. So the band rides the notes, not the trailing breathing pad.
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas, contentBottom: 1000);
        Assert.Equal((new Point(252, 0), new Point(252, 880)), g.Lines[0]);
        Assert.Equal((new Point(0, 880), new Point(1200, 880)), g.Lines[1]);
    }

    [Fact]
    public void Cornell_summaryStaysOnFirstScreen_whenCanvasShorterThanViewport()
    {
        // Heavy zoom-in makes the visible viewport taller than the (short) canvas — the band must not
        // ride above its 80%-of-screen home: sum = Max(0.80×600, ch − band) = Max(480, 500−120) = 480.
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, new Size(1200, 500));
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);
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
