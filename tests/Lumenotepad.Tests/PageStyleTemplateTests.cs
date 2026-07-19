using System.Collections.Generic;
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
        Assert.Equal("cue", boxes[0].Region);
        Assert.Equal("notes", boxes[1].Region);
        Assert.Equal("summary", boxes[2].Region);
        Assert.All(boxes, b => Assert.True(b.Locked));          // docked regions are always locked
        Assert.All(boxes, b => Assert.Equal(0, b.H));           // auto height — the docker owns Y
    }

    [Fact]
    public void Cornell_regionsAreDockedRegardlessOfMode()
    {
        // Cornell always docks (locked + auto height): the canvas owns its geometry, so the rigid
        // fixed-height path never applies to it — unlike the plain starter styles below.
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Cornell, PageStyles.ModeRigid, Vp);
        Assert.All(boxes, b => Assert.True(b.Locked));
        Assert.All(boxes, b => Assert.Equal(0, b.H));
        Assert.Equal(new[] { "cue", "notes", "summary" }, boxes.Select(b => b.Region));
    }

    [Theory]
    [InlineData(PageStyles.ModeGuides)]
    [InlineData(PageStyles.ModeRigid)]
    public void Structured_startersDockLockedAndAutoHeight_whenGuidesShown(int mode)
    {
        // With guides drawn, every structured style's starters dock: tagged + locked + auto height
        // (no more rigid fixed-height path — the canvas owns their geometry so they track the guides).
        var boxes = PageStyleTemplate.StartersFor(PageStyles.TwoColumn, mode, Vp);
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, b => Assert.True(b.Locked));
        Assert.All(boxes, b => Assert.Equal(0, b.H));
        Assert.Equal(new[] { "c0", "c1" }, boxes.Select(b => b.Region));
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
    public void RetagLegacyStarters_tagsRealStarters_ignoringStrayBoxes()
    {
        // Reproduces the FITTA Boxing page: a stray empty box the user left plus the four Topic starters,
        // all untagged (saved before regions existed). A count-based match failed (5 ≠ 4 regions) so the
        // page never docked; label matching tags the four Topics and leaves the stray box free.
        var boxes = new List<NoteBox>
        {
            MakeBox(""),                                     // stray empty box at some random spot
            MakeBox("Topic 1"), MakeBox("Topic 2"), MakeBox("Topic 3"), MakeBox("Topic 4"),
        };
        int n = PageStyleTemplate.RetagLegacyStarters(boxes, PageStyles.Boxing, Vp);
        Assert.Equal(4, n);
        Assert.Null(boxes[0].Region);                        // stray box left free
        Assert.False(boxes[0].Locked);
        Assert.Equal(new[] { "b0", "b1", "b2", "b3" }, boxes.Skip(1).Select(b => b.Region));
        Assert.All(boxes.Skip(1), b => Assert.True(b.Locked));
        Assert.Equal(0, PageStyleTemplate.RetagLegacyStarters(boxes, PageStyles.Boxing, Vp));  // idempotent
    }

    private static NoteBox MakeBox(string label)
    {
        var b = new NoteBox();
        b.Doc.Paragraphs.Clear();
        b.Doc.Paragraphs.Add(label.Length == 0
            ? new Paragraph()
            : new Paragraph { Runs = { new RichRun { Text = label, Bold = true } } });
        return b;
    }

    [Fact]
    public void RegionLabel_mapsBackToStarterLabels_forLegacyReTagging()
    {
        // NoteCanvas re-tags legacy starters by matching a box's first line to these labels, so a page
        // with stray extra boxes still docks its real starters. Keep them in sync with the templates.
        Assert.Equal("Cue", PageStyleTemplate.RegionLabel(PageStyles.Cornell, "cue"));
        Assert.Equal("Summary", PageStyleTemplate.RegionLabel(PageStyles.Cornell, "summary"));
        Assert.Equal("Topic 1", PageStyleTemplate.RegionLabel(PageStyles.Boxing, "b0"));
        Assert.Equal("Topic 4", PageStyleTemplate.RegionLabel(PageStyles.Boxing, "b3"));
        Assert.Equal("Column 2", PageStyleTemplate.RegionLabel(PageStyles.TwoColumn, "c1"));
        Assert.Equal("Column 3", PageStyleTemplate.RegionLabel(PageStyles.Charting, "h2"));
        Assert.Equal("Topic", PageStyleTemplate.RegionLabel(PageStyles.Outline, "outline"));
        Assert.Equal("First point", PageStyleTemplate.RegionLabel(PageStyles.Sentence, "sentence"));

        // The mapped label is exactly what the freshly stamped starter shows (its first line).
        var boxing = PageStyleTemplate.StartersFor(PageStyles.Boxing, PageStyles.ModeGuides, Vp);
        for (int i = 0; i < boxing.Count; i++)
            Assert.Equal(PageStyleTemplate.RegionLabel(PageStyles.Boxing, boxing[i].Region!),
                         boxing[i].Doc.Paragraphs[0].Text);
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
