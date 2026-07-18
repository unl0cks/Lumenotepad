using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PdfAnnotationTests
{
    [Fact]
    public void RoundTrip_preservesHighlightAndNote()
    {
        var doc = new PdfAnnotationDoc();
        doc.Items.Add(new PdfAnnotation
        {
            Page = 0, Kind = PdfAnnotation.Highlight, Color = "#66FFD54A",
            X = 0.1, Y = 0.2, W = 0.3, H = 0.05,
        });
        doc.Items.Add(new PdfAnnotation
        {
            Page = 2, Kind = PdfAnnotation.Note, Color = "#F2FFE9A8",
            X = 0.5, Y = 0.5, Text = "check this figure",
        });

        var restored = PdfAnnotationDoc.FromJson(doc.ToJson());

        Assert.Equal(2, restored.Items.Count);
        var hi = restored.Items[0];
        Assert.Equal(PdfAnnotation.Highlight, hi.Kind);
        Assert.Equal(0.1, hi.X, 5);
        Assert.Equal(0.3, hi.W, 5);
        var note = restored.Items[1];
        Assert.Equal(2, note.Page);
        Assert.Equal("check this figure", note.Text);
    }

    [Fact]
    public void RoundTrip_preservesArrowEndpointsAndTextBox()
    {
        var doc = new PdfAnnotationDoc();
        doc.Items.Add(new PdfAnnotation
        {
            Page = 1, Kind = PdfAnnotation.Arrow, Color = "#FF4DA6FF",
            X = 0.2, Y = 0.3, X2 = 0.6, Y2 = 0.7,
        });
        doc.Items.Add(new PdfAnnotation
        {
            Page = 0, Kind = PdfAnnotation.TextBox, X = 0.1, Y = 0.1, W = 0.3, H = 0.05,
            Color = "#00000000", Text = "written over the page",
        });

        var restored = PdfAnnotationDoc.FromJson(doc.ToJson());

        var arrow = restored.Items[0];
        Assert.Equal(PdfAnnotation.Arrow, arrow.Kind);
        Assert.Equal(0.6, arrow.X2, 5);
        Assert.Equal(0.7, arrow.Y2, 5);
        var text = restored.Items[1];
        Assert.Equal(PdfAnnotation.TextBox, text.Kind);
        Assert.Equal("written over the page", text.Text);
    }

    [Fact]
    public void RoundTrip_preservesTextFormattingFlags()
    {
        var doc = new PdfAnnotationDoc();
        doc.Items.Add(new PdfAnnotation
        {
            Page = 0, Kind = PdfAnnotation.TextBox, X = 0.1, Y = 0.1, W = 0.3, H = 0.05,
            Text = "styled", Bold = true, Italic = true, Underline = true, Strike = true,
        });
        var restored = PdfAnnotationDoc.FromJson(doc.ToJson()).Items[0];
        Assert.True(restored.Bold);
        Assert.True(restored.Italic);
        Assert.True(restored.Underline);
        Assert.True(restored.Strike);
    }

    [Fact]
    public void RoundTrip_preservesRichNoteContent()
    {
        var rich = new RichDocument();
        rich.InsertText(new DocPos(0, 0), "hi",
            new RunFormat(true, false, false, false, null, null, null, null, Baseline.Super, null));
        var json = RichDocJson.ToJson(rich);

        var doc = new PdfAnnotationDoc();
        doc.Items.Add(new PdfAnnotation
        {
            Page = 0, Kind = PdfAnnotation.Note, X = 0.1, Y = 0.1, W = 0.2, H = 0.05, Rich = json,
        });

        var restored = PdfAnnotationDoc.FromJson(doc.ToJson()).Items[0];
        Assert.Equal(json, restored.Rich);
        var back = RichDocJson.FromJson(restored.Rich);
        var run = back.Paragraphs[0].Runs[0];
        Assert.Equal("hi", run.Text);
        Assert.True(run.Bold);
        Assert.Equal(Baseline.Super, run.Baseline);
    }

    [Fact]
    public void FromJson_blankOrCorrupt_yieldsEmpty_neverThrows()
    {
        Assert.Empty(PdfAnnotationDoc.FromJson(null).Items);
        Assert.Empty(PdfAnnotationDoc.FromJson("").Items);
        Assert.Empty(PdfAnnotationDoc.FromJson("not json {{{").Items);
    }

    [Fact]
    public void SidecarPath_sitsNextToThePdf()
    {
        Assert.Equal(@"C:\notes\report.pdf.lumenotes.json",
            PdfAnnotationDoc.SidecarPath(@"C:\notes\report.pdf"));
    }

    [Fact]
    public void Hub_sharesOneDocPerPath_seedsFromDiskOnlyOnce()
    {
        PdfAnnotationHub.Reset();
        try
        {
            var seed = new PdfAnnotationDoc();
            seed.Items.Add(new PdfAnnotation { Page = 0, Kind = PdfAnnotation.Note, X = 0.1, Y = 0.1, Text = "hi" });

            var a = PdfAnnotationHub.Get(@"C:\x\a.pdf", seed.ToJson());
            Assert.Single(a.Items);

            // Same path (any case), even with different disk json → the SAME cached instance.
            var again = PdfAnnotationHub.Get(@"C:\X\A.PDF", null);
            Assert.Same(a, again);

            var other = PdfAnnotationHub.Get(@"C:\x\b.pdf", null);
            Assert.NotSame(a, other);
            Assert.Empty(other.Items);
        }
        finally { PdfAnnotationHub.Reset(); }
    }
}
