using System.Linq;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class RichModelTests
{
    private static RichDocument Doc(string text)
    {
        var d = new RichDocument();
        d.InsertText(new DocPos(0, 0), text);
        return d;
    }

    [Fact]
    public void InsertText_intoMiddleOfRun_keepsSurroundingFormat()
    {
        var d = Doc("hello world");
        d.InsertText(new DocPos(0, 5), " brave");
        Assert.Equal("hello brave world", d.GetText());
        Assert.Single(d.Paragraphs[0].Runs);   // same format → normalized to one run
    }

    [Fact]
    public void InsertText_withNewlines_splitsParagraphs()
    {
        var d = Doc("one\ntwo\nthree");
        Assert.Equal(3, d.Paragraphs.Count);
        Assert.Equal("two", d.Paragraphs[1].Text);
        var end = d.End;
        Assert.Equal(new DocPos(2, 5), end);
    }

    [Fact]
    public void SplitParagraph_atMiddle_movesTailToNewParagraph()
    {
        var d = Doc("hello world");
        var p = d.SplitParagraph(new DocPos(0, 5));
        Assert.Equal(new DocPos(1, 0), p);
        Assert.Equal("hello", d.Paragraphs[0].Text);
        Assert.Equal(" world", d.Paragraphs[1].Text);
    }

    [Fact]
    public void DeleteRange_withinParagraph()
    {
        var d = Doc("hello brave world");
        d.DeleteRange(new DocPos(0, 5), new DocPos(0, 11));
        Assert.Equal("hello world", d.GetText());
    }

    [Fact]
    public void DeleteRange_acrossParagraphs_merges()
    {
        var d = Doc("one\ntwo\nthree");
        d.DeleteRange(new DocPos(0, 2), new DocPos(2, 3));
        Assert.Equal("onee", d.GetText());
        Assert.Single(d.Paragraphs);
    }

    [Fact]
    public void ApplyFormat_splitsRuns_andRangeAllDetects()
    {
        var d = Doc("hello world");
        var a = new DocPos(0, 6);
        var b = new DocPos(0, 11);
        d.ApplyFormat(a, b, r => r.Bold = true);

        Assert.Equal(2, d.Paragraphs[0].Runs.Count);   // "hello " + bold "world"
        Assert.True(d.RangeAll(a, b, r => r.Bold));
        Assert.False(d.RangeAll(new DocPos(0, 0), b, r => r.Bold));

        // toggle back off → normalizes to a single run again
        d.ApplyFormat(a, b, r => r.Bold = false);
        Assert.Single(d.Paragraphs[0].Runs);
    }

    [Fact]
    public void FormatAt_reportsFormatOfCharBeforeCaret()
    {
        var d = Doc("plain");
        d.InsertText(d.End, "bold", bold: true);
        Assert.True(d.FormatAt(d.End).Bold);
        Assert.False(d.FormatAt(new DocPos(0, 3)).Bold);
    }

    [Fact]
    public void SnapshotRestore_roundTrips()
    {
        var d = Doc("one\ntwo");
        d.ApplyFormat(new DocPos(0, 0), new DocPos(0, 3), r => r.Italic = true);
        var snap = d.TakeSnapshot();

        d.DeleteRange(new DocPos(0, 0), d.End);
        Assert.Equal("", d.GetText());

        d.Restore(snap);
        Assert.Equal("one\ntwo", d.GetText());
        Assert.True(d.RangeAll(new DocPos(0, 0), new DocPos(0, 3), r => r.Italic));
    }

    [Fact]
    public void SetBullet_appliesAcrossRange_andBulletAllDetects()
    {
        var d = Doc("one\ntwo\nthree");
        d.SetBullet(new DocPos(0, 0), new DocPos(1, 0), "dot");
        Assert.Equal("dot", d.Paragraphs[0].Bullet);
        Assert.Equal("dot", d.Paragraphs[1].Bullet);
        Assert.Null(d.Paragraphs[2].Bullet);
        Assert.True(d.BulletAll(new DocPos(0, 0), new DocPos(1, 0), "dot"));
        Assert.False(d.BulletAll(new DocPos(0, 0), new DocPos(2, 0), "dot"));
    }

    [Fact]
    public void SplitParagraph_inheritsBullet_butNotChecked()
    {
        var d = Doc("task one");
        d.SetBullet(new DocPos(0, 0), new DocPos(0, 0), "check");
        d.ToggleChecked(0);
        d.SplitParagraph(d.End);

        Assert.Equal("check", d.Paragraphs[1].Bullet);
        Assert.True(d.Paragraphs[0].Checked);
        Assert.False(d.Paragraphs[1].Checked);
    }

    [Fact]
    public void SetBullet_awayFromCheck_resetsChecked()
    {
        var d = Doc("task");
        d.SetBullet(new DocPos(0, 0), new DocPos(0, 0), "check");
        d.ToggleChecked(0);
        d.SetBullet(new DocPos(0, 0), new DocPos(0, 0), "dot");
        Assert.False(d.Paragraphs[0].Checked);
    }

    [Fact]
    public void NumRunAt_FindsContiguousRun_AndRejectsNonNum()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "a\nb\nc\nd\ne");
        doc.SetBullet(new DocPos(1, 0), new DocPos(3, 0), "num");   // paras 1..3 numbered

        Assert.Equal((1, 3), doc.NumRunAt(2));
        Assert.Equal((1, 3), doc.NumRunAt(1));
        Assert.Equal((1, 3), doc.NumRunAt(3));
        Assert.Null(doc.NumRunAt(0));
        Assert.Null(doc.NumRunAt(4));
        Assert.Null(doc.NumRunAt(-1));
        Assert.Null(doc.NumRunAt(99));
    }

    [Fact]
    public void SetNumFlag_SetsWholeRun_NotNeighbors_AndRaisesChanged()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "a\nb\nc\nd");
        doc.SetBullet(new DocPos(0, 0), new DocPos(2, 0), "num");   // paras 0..2 numbered

        bool changed = false;
        doc.Changed += () => changed = true;
        doc.SetNumFlag(1, 'b', true);

        Assert.True(changed);
        Assert.True(doc.Paragraphs[0].NumBold);
        Assert.True(doc.Paragraphs[1].NumBold);
        Assert.True(doc.Paragraphs[2].NumBold);
        Assert.Null(doc.Paragraphs[3].NumBold);      // outside the run

        doc.SetNumFlag(1, 'b', null);                // clearing restores inherit
        Assert.Null(doc.Paragraphs[0].NumBold);

        changed = false;
        doc.SetNumFlag(3, 'b', true);                // not a numbered paragraph → no-op
        Assert.False(changed);
    }

    [Fact]
    public void Move_crossesParagraphBoundaries()
    {
        var d = Doc("ab\ncd");
        Assert.Equal(new DocPos(1, 0), d.Move(new DocPos(0, 2), +1));
        Assert.Equal(new DocPos(0, 2), d.Move(new DocPos(1, 0), -1));
        Assert.Equal(new DocPos(0, 0), d.Move(new DocPos(0, 0), -1));   // clamped at start
    }

    [Fact]
    public void NumFlag_ResolvesParaThenDefaultThenRun()
    {
        Assert.True(RichTextEditor.NumFlag(true, false, false));    // paragraph override wins
        Assert.False(RichTextEditor.NumFlag(false, true, true));
        Assert.True(RichTextEditor.NumFlag(null, true, false));     // then the global default
        Assert.False(RichTextEditor.NumFlag(null, false, true));
        Assert.True(RichTextEditor.NumFlag(null, null, true));      // then the text run
        Assert.False(RichTextEditor.NumFlag(null, null, false));
    }
}
