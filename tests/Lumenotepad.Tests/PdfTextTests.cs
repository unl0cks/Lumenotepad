using System;
using System.Linq;
using Avalonia;
using Lumenotepad.Editor;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class PdfTextTests
{
    private const double Tol = 0.004;

    private static PdfChar First(PdfPageText page, char c) => page.Chars.First(x => x.CodePoint == c && !x.Generated);

    [Fact]
    public void ReadsTextAndPlacesItWhereItWasDrawn()
    {
        var page = PdfText.ReadAll(TestPdf.Build(new[] { new TestPdf.Line(72, 700, "Hello World") })).Single();
        Assert.Equal("Hello World", PdfTextSelection.TextOf(page.Chars, 0, page.Chars.Count));
        var h = First(page, 'H');
        Assert.InRange(h.X0, 72 / 612.0 - Tol, 72 / 612.0 + Tol);
        double baseline = (792 - 700) / 792.0;
        Assert.True(h.Y0 < baseline && baseline < h.Y1, $"H spans {h.Y0}..{h.Y1}, baseline {baseline}");
        Assert.True(First(page, 'W').X0 > h.X1);
    }

    [Fact]
    public void RotatedPage_mapsIntoTheRotatedView()
    {
        var h = First(PdfText.ReadAll(TestPdf.Build(new[] { new TestPdf.Line(72, 700, "Hello") }, rotate: 90))[0], 'H');
        double x = 700 / 792.0, y = 72 / 612.0;
        Assert.True(h.X0 < x && x < h.X1, $"H spans x {h.X0}..{h.X1}, expected {x} inside");
        Assert.InRange(h.Y0, y - Tol, y + Tol);
    }

    [Fact]
    public void CroppedPage_measuresFromTheCropBox()
    {
        var h = First(PdfText.ReadAll(TestPdf.Build(new[] { new TestPdf.Line(72, 700, "Hello") },
            cropBox: new[] { 50.0, 50, 562, 742 }))[0], 'H');
        Assert.InRange(h.X0, 22 / 512.0 - Tol, 22 / 512.0 + Tol);
        double baseline = (742 - 700) / 692.0;
        Assert.True(h.Y0 < baseline && baseline < h.Y1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(0, true)]
    public void TextBoxesCoverTheInkTheRendererDraws(int rotate, bool crop)
    {
        var pdf = TestPdf.Build(new[] { new TestPdf.Line(72, 700, "Mitochondria"), new TestPdf.Line(72, 660, "make ATP") },
            rotate, crop ? new[] { 50.0, 50, 562, 742 } : null);
        var chars = PdfText.ReadAll(pdf)[0].Chars;
        var boxes = PdfTextSelection.Rects(chars, 0, chars.Count);
        var union = boxes.Aggregate((a, b) => a.Union(b));
        using var bmp = PdfRenderer.RenderPageSk(pdf, 0, 144f);
        Assert.NotNull(bmp);
        var ink = InkBounds(bmp!);
        Assert.True(union.Inflate(Tol).Contains(ink), $"ink {ink} not inside text boxes {union}");
        Assert.True(union.Height < ink.Height * 1.6 + 0.01 && union.Width < ink.Width * 1.6 + 0.01,
            $"text boxes {union} far larger than ink {ink}");
    }

    [Fact]
    public void TwoLines_becomeTwoRectsAndANewlineInTheText()
    {
        var chars = PdfText.ReadAll(TestPdf.Build(new[]
        {
            new TestPdf.Line(72, 700, "First line"), new TestPdf.Line(72, 680, "Second line"),
        }))[0].Chars;
        Assert.Equal("First line\nSecond line", PdfTextSelection.TextOf(chars, 0, chars.Count));
        Assert.Equal(2, PdfTextSelection.Rects(chars, 0, chars.Count).Count);
    }

    [Fact]
    public void PageWithoutText_isEmpty_andBadInputIsHarmless()
    {
        Assert.True(PdfText.ReadAll(TestPdf.Blank()).Single().IsEmpty);
        Assert.Empty(PdfText.ReadAll(new byte[] { 1, 2, 3, 4 }));
        Assert.Empty(PdfText.ReadAll(Array.Empty<byte>()));
    }

    [Fact]
    public void AgreesWithTheRendererOnPageCount()
    {
        var pdf = TestPdf.Build(new[] { new TestPdf.Line(72, 700, "Hi") });
        Assert.Equal(PdfRenderer.PageCount(pdf), PdfText.ReadAll(pdf).Count);
    }

    private static Rect InkBounds(SkiaSharp.SKBitmap bmp)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha < 128 || c.Red + c.Green + c.Blue > 600) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        Assert.True(maxX >= 0, "renderer drew no ink");
        return new Rect(minX / (double)bmp.Width, minY / (double)bmp.Height,
                        (maxX + 1 - minX) / (double)bmp.Width, (maxY + 1 - minY) / (double)bmp.Height);
    }

    [Fact]
    public void TightlySpacedLines_giveRectsThatDoNotOverlap()
    {
        var chars = PdfText.ReadAll(TestPdf.Build(new[]
        {
            new TestPdf.Line(72, 700, "JSTOR is a not-for-profit service that helps", 10),
            new TestPdf.Line(72, 691, "scholars, researchers, and students discover", 10),
            new TestPdf.Line(72, 682, "use, and build upon a wide range of content", 10),
        }))[0].Chars;
        var rects = PdfTextSelection.Rects(chars, 0, chars.Count);
        Assert.Equal(3, rects.Count);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                double w = Math.Min(rects[i].Right, rects[j].Right) - Math.Max(rects[i].Left, rects[j].Left);
                double h = Math.Min(rects[i].Bottom, rects[j].Bottom) - Math.Max(rects[i].Top, rects[j].Top);
                Assert.False(w > 1e-9 && h > 1e-9, $"line {i} {rects[i]} overlaps line {j} {rects[j]}");
            }
    }
}
