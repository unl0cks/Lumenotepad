using System.Linq;
using Avalonia;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStyleTemplateTests
{
    private static readonly Size Vp = new(900, 600);

    [Fact]
    public void Freeform_noStarters() =>
        Assert.Empty(PageStyleTemplate.StartersFor(PageStyles.Freeform, PageStyles.ModeGuides, Vp));

    [Fact]
    public void Mindmap_oneCentralBubble_neverLocked()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Mindmap, PageStyles.ModeRigid, Vp);
        var box = Assert.Single(boxes);
        Assert.Equal("Central idea", box.Doc.GetText());
        Assert.Equal(340, box.X);               // 900/2 − 110
        Assert.Equal(240, box.Y);               // 600 × 0.4
        Assert.Equal(220, box.Width);
        Assert.False(box.Locked);               // a mindmap's bubbles always move, even in rigid mode
        Assert.Equal(0, box.H);
    }

    [Fact]
    public void Cornell_threeLabelledRegions()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Cornell, PageStyles.ModeGuides, Vp);
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Cue", boxes[0].Doc.GetText());
        Assert.Equal("Notes", boxes[1].Doc.GetText());
        Assert.Equal("Summary", boxes[2].Doc.GetText());
        Assert.Equal(16, boxes[0].X);            // cue region, margin 16
        Assert.Equal(220, boxes[0].Width);       // 252 − 32
        Assert.Equal(268, boxes[1].X);           // 252 + 16
        Assert.Equal(616, boxes[1].Width);       // 900 − 252 − 32
        Assert.Equal(492, boxes[2].Y);           // 480 + 12
        Assert.All(boxes, b => Assert.False(b.Locked));
        Assert.All(boxes, b => Assert.Equal(0, b.H));           // auto height when not rigid
    }

    [Fact]
    public void Rigid_locksAndFixesHeights()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Cornell, PageStyles.ModeRigid, Vp);
        Assert.All(boxes, b => Assert.True(b.Locked));
        Assert.Equal(448, boxes[0].H);           // 480 − 32
        Assert.Equal(448, boxes[1].H);
        Assert.Equal(92, boxes[2].H);            // 600 − 480 − 28
    }

    [Fact]
    public void Boxing_fourTopics_insetInsideGuideRects()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Boxing, PageStyles.ModeGuides, Vp);
        Assert.Equal(4, boxes.Count);
        Assert.Equal("Topic 1", boxes[0].Doc.GetText());
        Assert.Equal(36, boxes[0].X);            // 24 + 12 inset
        Assert.Equal(394, boxes[0].Width);       // 418 − 24
    }

    [Fact]
    public void Charting_threeBoldHeaders()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Charting, PageStyles.ModeGuides, Vp);
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Column 1", boxes[0].Doc.GetText());
        Assert.True(boxes[0].Doc.Paragraphs[0].Runs[0].Bold);
        Assert.Equal(316, boxes[1].X);           // 300 + 16
    }

    [Fact]
    public void Outline_singleSkeletonBox()
    {
        var box = Assert.Single(PageStyleTemplate.StartersFor(PageStyles.Outline, PageStyles.ModeGuides, Vp));
        Assert.Equal("Topic\nMain idea\nSupporting detail", box.Doc.GetText());
        Assert.True(box.Doc.Paragraphs[0].Runs[0].Bold);
        Assert.Equal("dot", box.Doc.Paragraphs[1].Bullet);
        Assert.Equal("dot", box.Doc.Paragraphs[2].Bullet);
    }

    [Fact]
    public void Sentence_numberedStarter()
    {
        var box = Assert.Single(PageStyleTemplate.StartersFor(PageStyles.Sentence, PageStyles.ModeGuides, Vp));
        Assert.Equal("First point", box.Doc.GetText());
        Assert.Equal("num", box.Doc.Paragraphs[0].Bullet);
    }

    [Fact]
    public void TwoColumn_twoColumns()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.TwoColumn, PageStyles.ModeStartersOnly, Vp);
        Assert.Equal(2, boxes.Count);
        Assert.Equal("Column 1", boxes[0].Doc.GetText());
        Assert.Equal(466, boxes[1].X);           // 450 + 16
        Assert.All(boxes, b => Assert.False(b.Locked));         // starters-only never locks
    }
}
