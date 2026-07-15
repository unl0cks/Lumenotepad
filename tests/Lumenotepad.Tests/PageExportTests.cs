using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Lumenotepad.Editor;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class PageExportTests
{
    private static CanvasDocument Page()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(0, 0);
        var d = box.Doc;
        d.Paragraphs.Clear();
        // "Big heading" as Heading1, a bold word, a link, a bullet, a checkbox
        var head = new Paragraph { Style = ParaStyle.Heading1, Align = TextAlign.Center };
        head.Runs.Add(new RichRun { Text = "Big heading" });
        var body = new Paragraph();
        body.Runs.Add(new RichRun { Text = "plain " });
        body.Runs.Add(new RichRun { Text = "bold", Bold = true });
        body.Runs.Add(new RichRun { Text = " and a " });
        body.Runs.Add(new RichRun { Text = "site", Link = "https://x.test" });
        var bullet = new Paragraph { Bullet = "dot" };
        bullet.Runs.Add(new RichRun { Text = "point one" });
        var check = new Paragraph { Bullet = "check", Checked = true };
        check.Runs.Add(new RichRun { Text = "done" });
        d.Paragraphs.AddRange(new[] { head, body, bullet, check });
        return canvas;
    }

    private static string Text(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public void Text_hasTitleAndContent()
    {
        var s = Text(PageExport.Export(ExportFormat.Txt, "My Page", Page()));
        Assert.Contains("My Page", s);
        Assert.Contains("Big heading", s);
        Assert.Contains("bold", s);
        Assert.Contains("[x] done", s);
        Assert.Contains("• point one", s);
    }

    [Fact]
    public void Markdown_headingsBulletsLinks()
    {
        var s = Text(PageExport.Export(ExportFormat.Markdown, "My Page", Page()));
        Assert.Contains("# My Page", s);
        Assert.Contains("## Big heading", s);       // Heading1 → ##
        Assert.Contains("**bold**", s);
        Assert.Contains("[site](https://x.test)", s);
        Assert.Contains("- [x] done", s);
    }

    [Fact]
    public void Html_isWellFormedWithStylesAndLink()
    {
        var s = Text(PageExport.Export(ExportFormat.Html, "My Page", Page()));
        Assert.Contains("<!DOCTYPE html>", s);
        Assert.Contains("<h1 style=\"text-align:center\">Big heading</h1>", s);
        Assert.Contains("<strong>bold</strong>", s);
        Assert.Contains("<a href=\"https://x.test\">site</a>", s);
        Assert.Contains("<ul>", s);
        Assert.Contains("<input type=\"checkbox\" disabled checked>", s);   // the ticked "done" item
        Assert.DoesNotContain("☑", s);                                      // no tofu-prone ballot glyphs
    }

    [Theory]
    [InlineData(ExportFormat.Txt)]
    [InlineData(ExportFormat.Rtf)]
    public void Checkboxes_useAsciiBrackets_notBallotGlyphs(ExportFormat fmt)
    {
        var s = Text(PageExport.Export(fmt, "T", Page()));
        Assert.DoesNotContain("☑", s);
        Assert.DoesNotContain("☐", s);
        Assert.Contains("[x]", s);
    }

    [Fact]
    public void Html_escapesSpecialCharacters()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(0, 0);
        box.Doc.InsertText(new DocPos(0, 0), "a < b & \"c\"");
        var s = Text(PageExport.Export(ExportFormat.Html, "T", canvas));
        Assert.Contains("a &lt; b &amp; &quot;c&quot;", s);
        Assert.DoesNotContain("a < b & \"c\"", s);
    }

    [Fact]
    public void Rtf_startsWithHeaderAndHasControlWords()
    {
        var s = Text(PageExport.Export(ExportFormat.Rtf, "My Page", Page()));
        Assert.StartsWith(@"{\rtf1", s);
        Assert.EndsWith("}", s);
        Assert.Contains(@"\qc", s);         // centered heading
        Assert.Contains(@"\b", s);          // bold run
    }

    [Fact]
    public void Pdf_producesValidHeader()
    {
        var b = PageExport.Export(ExportFormat.Pdf, "My Page", Page());
        Assert.True(b.Length > 400);
        Assert.Equal((byte)'%', b[0]);
        Assert.Equal((byte)'P', b[1]);      // "%PDF"
        Assert.Equal((byte)'D', b[2]);
        Assert.Equal((byte)'F', b[3]);
    }

    [Theory]
    [InlineData(ExportFormat.Docx, "word/document.xml")]
    [InlineData(ExportFormat.Odt, "content.xml")]
    [InlineData(ExportFormat.Epub, "OEBPS/page.xhtml")]
    public void ZipPackages_containExpectedEntries(ExportFormat fmt, string entry)
    {
        var bytes = PageExport.Export(fmt, "My Page", Page());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains(entry, names);

        var e = zip.GetEntry(entry)!;
        using var reader = new StreamReader(e.Open());
        Assert.Contains("Big heading", reader.ReadToEnd());
    }

    [Fact]
    public void OdtAndEpub_storeMimetypeFirstUncompressed()
    {
        foreach (var fmt in new[] { ExportFormat.Odt, ExportFormat.Epub })
        {
            var bytes = PageExport.Export(fmt, "T", Page());
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var first = zip.Entries[0];
            Assert.Equal("mimetype", first.FullName);
            Assert.Equal(first.Length, first.CompressedLength);   // stored, not deflated
        }
    }

    [Fact]
    public void EmptyBoxes_skipped_titleFallsBack()
    {
        var canvas = new CanvasDocument();
        canvas.AddBox(0, 0);                    // empty box
        var s = Text(PageExport.Export(ExportFormat.Markdown, "  ", canvas));
        Assert.Contains("# Untitled", s);
    }
}
