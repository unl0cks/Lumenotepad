using Lumenotepad.Editor;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class MarkdownExportTests
{
    private static Paragraph P(string text, string? bullet = null, bool chk = false) =>
        new() { Runs = { new RichRun { Text = text } }, Bullet = bullet, Checked = chk };

    private static NoteBox Box(double x, double y, params Paragraph[] paras)
    {
        var doc = new RichDocument();
        doc.Paragraphs.Clear();
        foreach (var p in paras) doc.Paragraphs.Add(p);
        return new NoteBox(doc) { X = x, Y = y };
    }

    [Fact]
    public void TitleOnly_whenNoBoxes()
    {
        var md = MarkdownExport.PageToMarkdown("My Page", new CanvasDocument());
        Assert.Equal("# My Page\n", md);
    }

    [Fact]
    public void BlankTitle_fallsBackToUntitled()
    {
        var md = MarkdownExport.PageToMarkdown("  ", new CanvasDocument());
        Assert.Equal("# Untitled\n", md);
    }

    [Fact]
    public void PlainParagraph_afterHeading()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 0, P("Hello world")));
        Assert.Equal("# T\n\nHello world\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void Lists_bulletNumberedChecklist()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 0,
            P("A", "dot"),
            P("One", "num"),
            P("Two", "num"),
            P("todo", "check", chk: false),
            P("done", "check", chk: true)));
        Assert.Equal("# T\n\n- A\n1. One\n2. Two\n- [ ] todo\n- [x] done\n",
            MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void InlineEmphasis_boldItalicStrike()
    {
        var doc = new CanvasDocument();
        var rich = new RichDocument();
        rich.Paragraphs.Clear();
        rich.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new RichRun { Text = "b", Bold = true },
                new RichRun { Text = "i", Italic = true },
                new RichRun { Text = "x", Bold = true, Italic = true },
                new RichRun { Text = "s", Strike = true },
            },
        });
        doc.Boxes.Add(new NoteBox(rich) { X = 0, Y = 0 });
        Assert.Equal("# T\n\n**b***i****x***~~s~~\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void EmphasisKeepsSurroundingSpacesOutsideMarkers()
    {
        var doc = new CanvasDocument();
        var rich = new RichDocument();
        rich.Paragraphs.Clear();
        rich.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = " hi ", Bold = true } } });
        doc.Boxes.Add(new NoteBox(rich) { X = 0, Y = 0 });
        Assert.Equal("# T\n\n **hi** \n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void Boxes_orderedByYThenX_emptySkipped()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 100, P("second")));
        doc.Boxes.Add(Box(0, 10, P("first")));
        doc.Boxes.Add(new NoteBox() { X = 0, Y = 5 });
        Assert.Equal("# T\n\nfirst\n\nsecond\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Theory]
    [InlineData("Photosynthesis", "Photosynthesis")]
    [InlineData("A/B: c?", "A-B- c")]
    [InlineData("   ", "Untitled")]
    [InlineData("...", "Untitled")]
    public void SafeName_stripsIllegalChars(string raw, string expected) =>
        Assert.Equal(expected, MarkdownExport.SafeName(raw));
}
