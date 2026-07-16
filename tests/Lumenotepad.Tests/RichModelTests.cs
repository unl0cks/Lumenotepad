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
    public void SetTag_appliesToRange_splitDoesNotCarryIt()
    {
        var d = Doc("flagged thought");
        d.SetTag(new DocPos(0, 0), new DocPos(0, 0), "important");
        Assert.Equal("important", d.Paragraphs[0].Tag);

        d.SplitParagraph(d.End);                       // Enter: the tag marks that ONE thought
        Assert.Equal("important", d.Paragraphs[0].Tag);
        Assert.Null(d.Paragraphs[1].Tag);

        d.SetTag(new DocPos(0, 0), new DocPos(0, 0), null);
        Assert.Null(d.Paragraphs[0].Tag);
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

    [Fact]
    public void SmartListKind_DetectsPrefixes()
    {
        Assert.Equal("num", RichTextEditor.SmartListKind("1."));
        Assert.Equal("dot", RichTextEditor.SmartListKind("-"));
        Assert.Equal("dot", RichTextEditor.SmartListKind("*"));
        Assert.Null(RichTextEditor.SmartListKind("2."));      // only "1." starts a list
        Assert.Null(RichTextEditor.SmartListKind("a."));
        Assert.Null(RichTextEditor.SmartListKind(""));
        Assert.Null(RichTextEditor.SmartListKind("hello -"));
    }

    // ---- M10: alignment, text types, super/subscript, links ----

    [Fact]
    public void SetAlign_and_AlignOf_acrossParagraphs()
    {
        var d = Doc("one\ntwo\nthree");
        d.SetAlign(new DocPos(0, 0), new DocPos(1, 0), TextAlign.Center);
        Assert.Equal(TextAlign.Center, d.Paragraphs[0].Align);
        Assert.Equal(TextAlign.Center, d.Paragraphs[1].Align);
        Assert.Equal(TextAlign.Left, d.Paragraphs[2].Align);
        Assert.Equal(TextAlign.Center, d.AlignOf(new DocPos(0, 0), new DocPos(1, 1)));
        Assert.Null(d.AlignOf(new DocPos(0, 0), new DocPos(2, 0)));   // mixed → null
    }

    [Fact]
    public void SetParaStyle_and_ParaStyleOf()
    {
        var d = Doc("heading\nbody");
        d.SetParaStyle(new DocPos(0, 0), new DocPos(0, 3), ParaStyle.Heading1);
        Assert.Equal(ParaStyle.Heading1, d.Paragraphs[0].Style);
        Assert.Equal(ParaStyle.Body, d.Paragraphs[1].Style);
        Assert.Equal(ParaStyle.Heading1, d.ParaStyleOf(new DocPos(0, 0), new DocPos(0, 3)));
        Assert.Null(d.ParaStyleOf(new DocPos(0, 0), new DocPos(1, 0)));
    }

    [Fact]
    public void SplitParagraph_carriesAlign_continuesAsBody()
    {
        var d = Doc("title text");
        d.SetAlign(new DocPos(0, 0), new DocPos(0, 0), TextAlign.Right);
        d.SetParaStyle(new DocPos(0, 0), new DocPos(0, 0), ParaStyle.Title);
        d.SplitParagraph(new DocPos(0, 10));
        Assert.Equal(TextAlign.Right, d.Paragraphs[1].Align);        // alignment carries
        Assert.Equal(ParaStyle.Body, d.Paragraphs[1].Style);         // heading → body after Enter
    }

    [Fact]
    public void Baseline_and_Link_areRunFormatState()
    {
        var d = Doc("H2O and a link");
        d.ApplyFormat(new DocPos(0, 1), new DocPos(0, 2), r => r.Baseline = Baseline.Sub);   // the "2"
        Assert.True(d.RangeAll(new DocPos(0, 1), new DocPos(0, 2), r => r.Baseline == Baseline.Sub));
        Assert.True(d.RangeAll(new DocPos(0, 0), new DocPos(0, 1), r => r.Baseline == Baseline.Normal));

        d.ApplyFormat(new DocPos(0, 10), new DocPos(0, 14), r => r.Link = "https://x.test");
        Assert.True(d.RangeAll(new DocPos(0, 10), new DocPos(0, 14), r => r.Link == "https://x.test"));
        Assert.NotEqual(d.FormatAt(new DocPos(0, 11)).Link, d.FormatAt(new DocPos(0, 1)).Link);
    }

    [Fact]
    public void Json_roundTrips_alignStyleBaselineLink()
    {
        var d = Doc("H2O title");
        d.SetAlign(new DocPos(0, 0), new DocPos(0, 0), TextAlign.Justify);
        d.SetParaStyle(new DocPos(0, 0), new DocPos(0, 0), ParaStyle.Subtitle);
        d.ApplyFormat(new DocPos(0, 1), new DocPos(0, 2), r => r.Baseline = Baseline.Sub);
        d.ApplyFormat(new DocPos(0, 4), new DocPos(0, 9), r => r.Link = "https://a.test");

        var back = RichDocJson.FromJson(RichDocJson.ToJson(d));
        Assert.Equal(TextAlign.Justify, back.Paragraphs[0].Align);
        Assert.Equal(ParaStyle.Subtitle, back.Paragraphs[0].Style);
        Assert.True(back.RangeAll(new DocPos(0, 1), new DocPos(0, 2), r => r.Baseline == Baseline.Sub));
        Assert.True(back.RangeAll(new DocPos(0, 4), new DocPos(0, 9), r => r.Link == "https://a.test"));
    }

    [Fact]
    public void InsertFootnote_addsMarkerAndNumberedEntry()
    {
        var d = Doc("See here.");
        var caret = d.InsertFootnote(new DocPos(0, 8), "A source.");   // before the period
        Assert.Equal(new DocPos(0, 11), caret);                        // caret after "[1]"

        Assert.Contains("[1]", d.Paragraphs[0].Text);
        var fn = d.Paragraphs[^1];
        Assert.True(fn.Footnote);
        Assert.Equal("[1] A source.", fn.Text);

        d.InsertFootnote(new DocPos(0, 0), "Second.");                 // numbering continues
        Assert.Equal(2, d.Paragraphs.Count(p => p.Footnote));
        Assert.Equal("[2] Second.", d.Paragraphs[^1].Text);
    }

    [Fact]
    public void Footnote_flag_roundTrips()
    {
        var d = Doc("body");
        d.InsertFootnote(new DocPos(0, 4), "note");
        var back = RichDocJson.FromJson(RichDocJson.ToJson(d));
        Assert.True(back.Paragraphs[^1].Footnote);
        Assert.Equal("[1] note", back.Paragraphs[^1].Text);
    }

    [Fact]
    public void TextTypeSizes_scaleFromBody()
    {
        Assert.Equal(15, RichTextEditor.BaseSizeFor(ParaStyle.Body, 15));
        Assert.Equal(30, RichTextEditor.BaseSizeFor(ParaStyle.Title, 15));
        Assert.True(RichTextEditor.BaseSizeFor(ParaStyle.Heading1, 15) > RichTextEditor.BaseSizeFor(ParaStyle.Heading2, 15));
        Assert.True(RichTextEditor.BaseSizeFor(ParaStyle.Heading2, 15) > RichTextEditor.BaseSizeFor(ParaStyle.Heading3, 15));
    }
}
