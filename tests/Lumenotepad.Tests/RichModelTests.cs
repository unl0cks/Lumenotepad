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
    public void Move_crossesParagraphBoundaries()
    {
        var d = Doc("ab\ncd");
        Assert.Equal(new DocPos(1, 0), d.Move(new DocPos(0, 2), +1));
        Assert.Equal(new DocPos(0, 2), d.Move(new DocPos(1, 0), -1));
        Assert.Equal(new DocPos(0, 0), d.Move(new DocPos(0, 0), -1));   // clamped at start
    }
}
