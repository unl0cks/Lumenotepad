using System.Collections.Generic;
using Avalonia;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PdfTextSelectionTests
{
    private const double Cw = 0.02, Lh = 0.02;

    private static List<PdfChar> Page(params string[] lines)
    {
        var chars = new List<PdfChar>();
        for (int li = 0; li < lines.Length; li++)
        {
            if (li > 0)
            {
                chars.Add(new PdfChar('\r', 0, 0, 0, 0, true));
                chars.Add(new PdfChar('\n', 0, 0, 0, 0, true));
            }
            double y = 0.1 + li * 0.03, x = 0.1;
            foreach (var ch in lines[li])
            {
                chars.Add(new PdfChar(ch, x, y, x + Cw, y + Lh, false));
                x += Cw;
            }
        }
        return chars;
    }

    private static Point Mid(PdfChar c, double fx = 0.5) => new(c.X0 + (c.X1 - c.X0) * fx, (c.Y0 + c.Y1) / 2);

    [Fact]
    public void CharAt_findsTheBoxUnderThePoint_orNothing()
    {
        var p = Page("Hello");
        Assert.Equal(2, PdfTextSelection.CharAt(p, Mid(p[2])));
        Assert.Equal(-1, PdfTextSelection.CharAt(p, new Point(0.9, 0.9)));
        Assert.Equal(-1, PdfTextSelection.CharAt(new List<PdfChar>(), new Point(0.1, 0.1)));
    }

    [Fact]
    public void CaretAt_usesTheHalfOfTheCharacter()
    {
        var p = Page("Hello");
        Assert.Equal(3, PdfTextSelection.CaretAt(p, Mid(p[3], 0.25)));
        Assert.Equal(4, PdfTextSelection.CaretAt(p, Mid(p[3], 0.75)));
    }

    [Fact]
    public void CaretAt_snapsFromTheMarginsOntoTheLine_andGivesUpFarAway()
    {
        var p = Page("First line", "Second line");
        Assert.Equal(12, PdfTextSelection.CaretAt(p, new Point(0.02, 0.14)));
        Assert.Equal(23, PdfTextSelection.CaretAt(p, new Point(0.9, 0.14)));
        Assert.Equal(-1, PdfTextSelection.CaretAt(p, new Point(0.5, 0.9)));
        Assert.Equal(-1, PdfTextSelection.CaretAt(new List<PdfChar>(), new Point(0.1, 0.1)));
    }

    [Fact]
    public void WordAt_growsOverLettersAndInnerApostrophes()
    {
        var p = Page("the cell's wall");
        Assert.Equal((4, 10), PdfTextSelection.WordAt(p, 6));
        Assert.Equal((3, 4), PdfTextSelection.WordAt(p, 3));
        Assert.Equal((11, 15), PdfTextSelection.WordAt(p, 14));
    }

    [Fact]
    public void LineAt_staysOnOneVisualLine()
    {
        var p = Page("First line", "Second line", "Third");
        Assert.Equal((12, 23), PdfTextSelection.LineAt(p, 15));
        Assert.Equal((0, 10), PdfTextSelection.LineAt(p, 0));
        Assert.Equal((25, 30), PdfTextSelection.LineAt(p, 29));
    }

    [Fact]
    public void Rects_givesOneRectanglePerLine()
    {
        var p = Page("First line", "Second line", "Third");

        var one = PdfTextSelection.Rects(p, 2, 7);
        Assert.Single(one);
        Assert.Equal(0.1 + 2 * Cw, one[0].X, 6);
        Assert.Equal(5 * Cw, one[0].Width, 6);

        var three = PdfTextSelection.Rects(p, 6, 28);
        Assert.Equal(3, three.Count);
        Assert.Equal(0.1, three[0].Y, 6);
        Assert.Equal(0.13, three[1].Y, 6);
        Assert.Equal(0.16, three[2].Y, 6);

        Assert.Empty(PdfTextSelection.Rects(p, 10, 12));
        Assert.Empty(PdfTextSelection.Rects(p, 4, 4));
    }

    [Fact]
    public void Rects_keepsASuperscriptOnItsLine()
    {
        var p = Page("x2y");
        p[1] = new PdfChar('2', p[1].X0, 0.098, p[1].X1, 0.112, false);
        Assert.Single(PdfTextSelection.Rects(p, 0, 3));
    }

    [Fact]
    public void TextOf_turnsGeneratedBreaksIntoNewlines_andTrimsTheEnd()
    {
        var p = Page("First line", "Second line ");
        Assert.Equal("First line\nSecond line", PdfTextSelection.TextOf(p, 0, p.Count));
        Assert.Equal("line", PdfTextSelection.TextOf(p, 6, 10));
        var wide = new List<PdfChar> { new(0x1F600, 0.1, 0.1, 0.12, 0.12, false) };
        Assert.Equal(char.ConvertFromUtf32(0x1F600), PdfTextSelection.TextOf(wide, 0, 1));
    }

    [Fact]
    public void CaretBox_sitsBeforeTheCharacter_orAfterTheLastOnTheLine()
    {
        var p = Page("First line", "Second line");
        var before = PdfTextSelection.CaretBox(p, 2)!.Value;
        Assert.Equal(p[2].X0, before.X, 6);
        var lineEnd = PdfTextSelection.CaretBox(p, 10)!.Value;
        Assert.Equal(p[9].X1, lineEnd.X, 6);
        Assert.Equal(p[9].Y0, lineEnd.Y, 6);
        Assert.Null(PdfTextSelection.CaretBox(new List<PdfChar>(), 0));
    }

    [Fact]
    public void PdfPageText_isEmpty_whenNothingHasABox()
    {
        Assert.True(new PdfPageText(0, new List<PdfChar> { new('\n', 0, 0, 0, 0, true) }).IsEmpty);
        Assert.False(new PdfPageText(0, Page("a")).IsEmpty);
    }

    private static void AssertNoOverlap(IReadOnlyList<Rect> rects)
    {
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                double w = System.Math.Min(rects[i].Right, rects[j].Right) - System.Math.Max(rects[i].Left, rects[j].Left);
                double h = System.Math.Min(rects[i].Bottom, rects[j].Bottom) - System.Math.Max(rects[i].Top, rects[j].Top);
                Assert.False(w > 1e-9 && h > 1e-9, $"rect {i} {rects[i]} overlaps rect {j} {rects[j]}");
            }
    }

    [Fact]
    public void Rects_neverOverlap_whenLineBoxesDo()
    {
        var p = new List<PdfChar>();
        for (int li = 0; li < 3; li++)
        {
            if (li > 0)
            {
                p.Add(new PdfChar('\r', 0, 0, 0, 0, true));
                p.Add(new PdfChar('\n', 0, 0, 0, 0, true));
            }
            double y = 0.1 + li * 0.02;
            for (int k = 0; k < 6; k++) p.Add(new PdfChar('a', 0.1 + k * 0.02, y, 0.12 + k * 0.02, y + 0.03, false));
        }
        var rects = PdfTextSelection.Rects(p, 0, p.Count);
        Assert.Equal(3, rects.Count);
        AssertNoOverlap(rects);
        Assert.Equal(rects[0].Bottom, rects[1].Top, 9);
        Assert.Equal(rects[1].Bottom, rects[2].Top, 9);
        Assert.Equal(0.10, rects[0].Top, 9);
        Assert.Equal(0.17, rects[2].Bottom, 9);
    }

    [Fact]
    public void Rects_mergesPiecesOfOneLineThatOverlap()
    {
        var p = new List<PdfChar>
        {
            new('a', 0.30, 0.1, 0.32, 0.12, false), new('b', 0.32, 0.1, 0.34, 0.12, false),
            new('c', 0.10, 0.1, 0.12, 0.12, false), new('d', 0.12, 0.1, 0.33, 0.12, false),
        };
        var rects = PdfTextSelection.Rects(p, 0, 4);
        Assert.Single(rects);
        Assert.Equal(0.10, rects[0].X, 9);
        Assert.Equal(0.34, rects[0].Right, 9);
    }

    [Fact]
    public void Separate_fixesStoredRectsToo()
    {
        var fixedUp = PdfTextSelection.Separate(new[]
        {
            new Rect(0.1, 0.10, 0.5, 0.03), new Rect(0.1, 0.12, 0.5, 0.03), new Rect(0.1, 0.14, 0.3, 0.03),
        });
        Assert.Equal(3, fixedUp.Count);
        AssertNoOverlap(fixedUp);
    }
}
