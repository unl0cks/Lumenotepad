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

    [Theory]
    [InlineData(ExportFormat.Txt, "Attachment: report.pdf")]
    [InlineData(ExportFormat.Markdown, "**Attachment:** report.pdf")]
    [InlineData(ExportFormat.Html, "<p class=\"attachment\">📎 report.pdf</p>")]
    public void Attachments_exportAsNamedLines(ExportFormat fmt, string expected)
    {
        var canvas = Page();
        var att = canvas.AddBox(0, 700);
        att.AttachPath = "assets/report.pdf";
        Assert.Contains(expected, Text(PageExport.Export(fmt, "T", canvas)));
    }

    [Fact]
    public void Tags_exportAsMarkers()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(0, 0);
        box.Doc.InsertText(new DocPos(0, 0), "urgent thing");
        box.Doc.SetTag(new DocPos(0, 0), new DocPos(0, 0), "important");

        Assert.Contains("[!] urgent thing", Text(PageExport.Export(ExportFormat.Txt, "T", canvas)));
        Assert.Contains("[!] urgent thing", Text(PageExport.Export(ExportFormat.Markdown, "T", canvas)));
        var html = Text(PageExport.Export(ExportFormat.Html, "T", canvas));
        Assert.Contains("font-weight:bold\">!</span> urgent thing", html);
    }

    private static CanvasDocument TablePage()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddTableBox(0, 0, 2, 2);
        box.Table!.Rows[0][0].InsertText(new DocPos(0, 0), "Name");
        box.Table.Rows[0][1].InsertText(new DocPos(0, 0), "Score");
        box.Table.Rows[1][0].InsertText(new DocPos(0, 0), "Ann");
        box.Table.Rows[1][1].InsertText(new DocPos(0, 0), "42");
        return canvas;
    }

    [Fact]
    public void Tables_exportAsMarkdownGrid()
    {
        var s = Text(PageExport.Export(ExportFormat.Markdown, "T", TablePage()));
        Assert.Contains("| Name | Score |", s);
        Assert.Contains("| --- | --- |", s);       // header separator row
        Assert.Contains("| Ann | 42 |", s);
    }

    [Fact]
    public void Tables_exportAsHtmlTable()
    {
        var s = Text(PageExport.Export(ExportFormat.Html, "T", TablePage()));
        Assert.Contains("<table class=\"grid\">", s);
        Assert.Contains("<td>Name</td>", s);
        Assert.Contains("<td>42</td>", s);
    }

    [Fact]
    public void Tables_exportAsTextGrid()
    {
        var s = Text(PageExport.Export(ExportFormat.Txt, "T", TablePage()));
        Assert.Contains("Name", s);
        Assert.Contains("Score", s);
        Assert.Contains("|", s);                    // pipe-bordered cells
    }

    [Fact]
    public void Tables_pdfStaysValid()
    {
        var b = PageExport.Export(ExportFormat.Pdf, "T", TablePage());
        Assert.Equal((byte)'%', b[0]);              // still a valid PDF with a drawn grid
        Assert.True(b.Length > 400);
    }

    [Fact]
    public void Dividers_exportAsRules()
    {
        var canvas = Page();
        var div = canvas.AddBox(0, 700);
        div.Divider = "h";
        Assert.Contains("---", Text(PageExport.Export(ExportFormat.Markdown, "T", canvas)));
        Assert.Contains("<hr/>", Text(PageExport.Export(ExportFormat.Html, "T", canvas)));
    }

    [Fact]
    public void Pdf_includesImagesAndDividers()
    {
        // A real PNG on disk so the embed path actually runs.
        var root = Directory.CreateTempSubdirectory("lumexport").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            using (var bmp = new SkiaSharp.SKBitmap(40, 24))
            using (var img = SkiaSharp.SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90))
            using (var fs = File.Create(Path.Combine(root, "images", "pic.png")))
                data.SaveTo(fs);

            var canvas = Page();
            var imgBox = canvas.AddBox(0, 500);
            imgBox.ImagePath = "images/pic.png";
            var div = canvas.AddBox(0, 600);
            div.Divider = "h";
            div.Width = 300;

            var plain = PageExport.Export(ExportFormat.Pdf, "T", Page());
            var rich = PageExport.Export(ExportFormat.Pdf, "T", canvas, root);
            Assert.Equal((byte)'%', rich[0]);                 // still a valid PDF
            Assert.True(rich.Length > plain.Length + 100);    // the picture bytes actually embedded

            // A missing image file must not blow up the export.
            imgBox.ImagePath = "images/gone.png";
            var survived = PageExport.Export(ExportFormat.Pdf, "T", canvas, root);
            Assert.Equal((byte)'%', survived[0]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ToPdfMulti_combinesPages_intoOneValidPdf()
    {
        var one = PageExport.ToPdfMulti(new[] { ("Page 1", Page()) });
        var three = PageExport.ToPdfMulti(new[] { ("Page 1", Page()), ("Page 2", Page()), ("Page 3", Page()) });
        Assert.Equal((byte)'%', three[0]);              // "%PDF"
        Assert.Equal((byte)'P', three[1]);
        Assert.True(three.Length > one.Length);         // more pages → more bytes
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
