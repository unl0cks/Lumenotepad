# PDF Text Highlighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Highlights in the PDF viewer follow the text: select text and press Highlight, or click where the text starts and where it ends; the drag-a-box highlight stays as the Area mode.

**Architecture:** `PdfText` reads every character box from PDFium (the engine that already renders pages) and maps it into the 0 to 1 page units annotations use. `PdfTextSelection` is pure geometry over those boxes. The viewer draws selections on a new layer under the annotations, shows a Highlight/Copy bar on a layer above them, and stores a text highlight as one annotation with one rectangle per line.

**Tech Stack:** Avalonia 12.0.5, .NET 10, PDFium 137.0.7149 via P/Invoke (shipped by PDFtoImage 5.1.0), SkiaSharp 3.119.4, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-16-pdf-text-highlight-design.md`

## Global Constraints

- No comments in any code. Nothing in code or files may mention AI or any assistant.
- No em dashes in any file except the app's greeting text.
- No new packages.
- Doubles written into PDF test fixtures use invariant culture (this machine uses a decimal comma).
- All PDFium access holds `PdfRenderer.Gate`.
- UI strings (verbatim): "Reading the page text…", "This page is a picture, so there's no text to select. Drag to highlight an area.", "Now click where the highlight should end.", "Copied."
- Implementation note: the spec's `Views/PdfTextSelector.cs` is realised as `Views/PdfViewer.TextSelect.cs`, a partial of `PdfViewer`. Same file split, no callback plumbing.

---

### Task 1: Text highlights in the annotation model

**Files:**
- Modify: `src/Lumenotepad/Editor/PdfAnnotations.cs`
- Test: `tests/Lumenotepad.Tests/PdfAnnotationTests.cs`

**Interfaces:**
- Produces: `List<double[]>? PdfAnnotation.Rects` (each `[x, y, w, h]`), `bool PdfAnnotation.IsTextHighlight`, `static PdfAnnotation PdfAnnotation.TextHighlight(int page, IReadOnlyList<Avalonia.Rect> rects, string color)`.

- [ ] **Step 1: Write the failing tests** (append to `PdfAnnotationTests`)

```csharp
    [Fact]
    public void TextHighlight_keepsEveryLineAndTheUnion()
    {
        var a = PdfAnnotation.TextHighlight(3,
            new[] { new Avalonia.Rect(0.1, 0.20, 0.5, 0.02), new Avalonia.Rect(0.1, 0.23, 0.3, 0.02) }, "#66FFD54A");

        Assert.True(a.IsTextHighlight);
        Assert.Equal(3, a.Page);
        Assert.Equal(PdfAnnotation.Highlight, a.Kind);
        Assert.Equal(2, a.Rects!.Count);
        Assert.Equal(0.1, a.X, 6);
        Assert.Equal(0.20, a.Y, 6);
        Assert.Equal(0.5, a.W, 6);
        Assert.Equal(0.05, a.H, 6);
    }

    [Fact]
    public void TextHighlight_roundTripsRects_andPlainHighlightsStayPlain()
    {
        var doc = new PdfAnnotationDoc();
        doc.Items.Add(PdfAnnotation.TextHighlight(0,
            new[] { new Avalonia.Rect(0.1, 0.2, 0.3, 0.02), new Avalonia.Rect(0.1, 0.23, 0.2, 0.02) }, "#66FFD54A"));
        doc.Items.Add(new PdfAnnotation { Page = 0, Kind = PdfAnnotation.Highlight, X = 0.5, Y = 0.5, W = 0.1, H = 0.1 });

        string json = doc.ToJson();
        var restored = PdfAnnotationDoc.FromJson(json);

        Assert.True(restored.Items[0].IsTextHighlight);
        Assert.Equal(0.23, restored.Items[0].Rects![1][1], 6);
        Assert.False(restored.Items[1].IsTextHighlight);
        Assert.Null(restored.Items[1].Rects);
        Assert.DoesNotContain("IsTextHighlight", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"rects\""));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfAnnotationTests`
Expected: build error, `PdfAnnotation` has no `TextHighlight`.

- [ ] **Step 3: Implement** in `PdfAnnotations.cs`: add `using Avalonia;` and, inside `PdfAnnotation` after `Strike`:

```csharp
    [JsonPropertyName("rects")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Rects { get; set; }

    [JsonIgnore] public bool IsTextHighlight => Kind == Highlight && Rects is { Count: > 0 };

    public static PdfAnnotation TextHighlight(int page, IReadOnlyList<Rect> rects, string color)
    {
        var list = new List<double[]>(rects.Count);
        var a = new PdfAnnotation { Page = page, Kind = Highlight, Color = color, Rects = list };
        if (rects.Count == 0) return a;
        var union = rects[0];
        foreach (var r in rects)
        {
            list.Add(new[] { r.X, r.Y, r.Width, r.Height });
            union = union.Union(r);
        }
        a.X = union.X; a.Y = union.Y; a.W = union.Width; a.H = union.Height;
        return a;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfAnnotationTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Editor/PdfAnnotations.cs tests/Lumenotepad.Tests/PdfAnnotationTests.cs
git commit -m "Store text highlights as one annotation with a rectangle per line"
```

---

### Task 2: Selection geometry

**Files:**
- Create: `src/Lumenotepad/Editor/PdfTextSelection.cs`
- Test: `tests/Lumenotepad.Tests/PdfTextSelectionTests.cs`

**Interfaces:**
- Produces: `record struct PdfChar(int CodePoint, double X0, double Y0, double X1, double Y1, bool Generated)` with `HasBox`, `CenterY`; `record PdfPageText(int Page, IReadOnlyList<PdfChar> Chars)` with `IsEmpty`; static class `PdfTextSelection` with `CharAt`, `CaretAt`, `WordAt`, `LineAt`, `Rects`, `TextOf`, `CaretBox`. All coordinates are 0 to 1 page units, origin top-left. Carets run 0..N between characters; ranges are `[start, end)`.

- [ ] **Step 1: Write the failing tests**

```csharp
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
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfTextSelectionTests`
Expected: build error, `PdfChar` not found.

- [ ] **Step 3: Implement** `src/Lumenotepad/Editor/PdfTextSelection.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;

namespace Lumenotepad.Editor;

public readonly record struct PdfChar(int CodePoint, double X0, double Y0, double X1, double Y1, bool Generated)
{
    public bool HasBox => X1 > X0 && Y1 > Y0;
    public double CenterY => (Y0 + Y1) / 2;
    public double Height => Y1 - Y0;
}

public sealed record PdfPageText(int Page, IReadOnlyList<PdfChar> Chars)
{
    public bool IsEmpty => !Chars.Any(c => c.HasBox);
}

public static class PdfTextSelection
{
    private const double Slop = 0.15;

    public static int CharAt(IReadOnlyList<PdfChar> chars, Point p)
    {
        for (int i = 0; i < chars.Count; i++)
        {
            var c = chars[i];
            if (!c.HasBox) continue;
            double pad = c.Height * Slop;
            if (p.X >= c.X0 - pad && p.X <= c.X1 + pad && p.Y >= c.Y0 - pad && p.Y <= c.Y1 + pad) return i;
        }
        return -1;
    }

    public static int CaretAt(IReadOnlyList<PdfChar> chars, Point p)
    {
        int best = -1;
        double bestDx = double.MaxValue;
        for (int i = 0; i < chars.Count; i++)
        {
            var c = chars[i];
            if (!c.HasBox || p.Y < c.Y0 || p.Y > c.Y1) continue;
            double dx = p.X < c.X0 ? c.X0 - p.X : p.X > c.X1 ? p.X - c.X1 : 0;
            if (dx < bestDx) { bestDx = dx; best = i; }
        }
        if (best < 0)
        {
            double bestD = double.MaxValue;
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (!c.HasBox) continue;
                double dx = p.X < c.X0 ? c.X0 - p.X : p.X > c.X1 ? p.X - c.X1 : 0;
                double dy = p.Y < c.Y0 ? c.Y0 - p.Y : p.Y > c.Y1 ? p.Y - c.Y1 : 0;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return -1;
            double dyBest = p.Y < chars[best].Y0 ? chars[best].Y0 - p.Y : p.Y - chars[best].Y1;
            if (dyBest > chars[best].Height) return -1;
        }
        var hit = chars[best];
        return p.X > (hit.X0 + hit.X1) / 2 ? best + 1 : best;
    }

    public static (int Start, int End) WordAt(IReadOnlyList<PdfChar> chars, int index)
    {
        if (index < 0 || index >= chars.Count) return (0, 0);
        if (!IsWordChar(chars, index)) return (index, index + 1);
        int a = index, b = index + 1;
        while (a > 0 && IsWordChar(chars, a - 1)) a--;
        while (b < chars.Count && IsWordChar(chars, b)) b++;
        return (a, b);
    }

    public static (int Start, int End) LineAt(IReadOnlyList<PdfChar> chars, int index)
    {
        if (index < 0 || index >= chars.Count) return (0, 0);
        int refIndex = index;
        while (refIndex < chars.Count && !chars[refIndex].HasBox) refIndex++;
        if (refIndex >= chars.Count) return (index, index + 1);
        var r = chars[refIndex];
        int a = refIndex;
        for (int i = refIndex - 1; i >= 0; i--)
        {
            var c = chars[i];
            if (IsBreak(c)) break;
            if (!c.HasBox) continue;
            if (!SameLine(r, c)) break;
            a = i;
        }
        int b = refIndex + 1;
        for (int i = refIndex + 1; i < chars.Count; i++)
        {
            var c = chars[i];
            if (IsBreak(c)) break;
            if (!c.HasBox) continue;
            if (!SameLine(r, c)) break;
            b = i + 1;
        }
        return (a, b);
    }

    public static IReadOnlyList<Rect> Rects(IReadOnlyList<PdfChar> chars, int start, int end)
    {
        var rects = new List<Rect>();
        start = Math.Max(0, start);
        end = Math.Min(chars.Count, end);
        Rect? cur = null;
        for (int i = start; i < end; i++)
        {
            var c = chars[i];
            if (!c.HasBox) continue;
            var r = new Rect(c.X0, c.Y0, c.X1 - c.X0, c.Y1 - c.Y0);
            if (cur is { } k && c.CenterY >= k.Top && c.CenterY <= k.Bottom && c.X0 >= k.Left - k.Height * 0.5)
                cur = k.Union(r);
            else
            {
                if (cur is { } done) rects.Add(done);
                cur = r;
            }
        }
        if (cur is { } last) rects.Add(last);
        return rects;
    }

    public static string TextOf(IReadOnlyList<PdfChar> chars, int start, int end)
    {
        var sb = new StringBuilder();
        start = Math.Max(0, start);
        end = Math.Min(chars.Count, end);
        for (int i = start; i < end; i++)
        {
            int cp = chars[i].CodePoint;
            if (cp == '\r') continue;
            if (cp == '\n') { sb.Append('\n'); continue; }
            if (cp < 0x20 && cp != '\t') continue;
            if (!Rune.IsValid(cp) || cp == 0xFFFE || cp == 0xFFFF) continue;
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString().TrimEnd();
    }

    public static Rect? CaretBox(IReadOnlyList<PdfChar> chars, int caret)
    {
        if (caret >= 0 && caret < chars.Count && chars[caret].HasBox)
        {
            var c = chars[caret];
            return new Rect(c.X0, c.Y0, 0, c.Height);
        }
        for (int i = Math.Min(caret, chars.Count) - 1; i >= 0; i--)
            if (chars[i].HasBox)
            {
                var c = chars[i];
                return new Rect(c.X1, c.Y0, 0, c.Height);
            }
        for (int i = Math.Max(0, caret); i < chars.Count; i++)
            if (chars[i].HasBox)
            {
                var c = chars[i];
                return new Rect(c.X0, c.Y0, 0, c.Height);
            }
        return null;
    }

    private static bool IsBreak(PdfChar c) => c.CodePoint is '\r' or '\n';

    private static bool SameLine(PdfChar a, PdfChar b) => b.CenterY >= a.Y0 && b.CenterY <= a.Y1;

    private static bool IsLetterOrDigit(int cp) => Rune.IsValid(cp) && Rune.IsLetterOrDigit(new Rune(cp));

    private static bool IsWordChar(IReadOnlyList<PdfChar> chars, int i)
    {
        int cp = chars[i].CodePoint;
        if (IsLetterOrDigit(cp)) return true;
        if (cp is '\'' or '’')
            return i > 0 && i + 1 < chars.Count && IsLetterOrDigit(chars[i - 1].CodePoint) && IsLetterOrDigit(chars[i + 1].CodePoint);
        return false;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfTextSelectionTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Editor/PdfTextSelection.cs tests/Lumenotepad.Tests/PdfTextSelectionTests.cs
git commit -m "Add text selection geometry for PDF pages"
```

---

### Task 3: Reading character boxes from PDFium

**Files:**
- Modify: `src/Lumenotepad/Services/PdfRenderer.cs` (gate)
- Create: `src/Lumenotepad/Services/PdfText.cs`
- Create: `tests/Lumenotepad.Tests/TestPdf.cs`
- Test: `tests/Lumenotepad.Tests/PdfTextTests.cs`

**Interfaces:**
- Consumes: `PdfChar`, `PdfPageText`, `PdfTextSelection` (Task 2).
- Produces: `internal static readonly object PdfRenderer.Gate`; `static IReadOnlyList<PdfPageText> PdfText.ReadAll(byte[] pdf)` which never throws.

- [ ] **Step 1: Write the test PDF builder** `tests/Lumenotepad.Tests/TestPdf.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lumenotepad.Tests;

internal static class TestPdf
{
    public sealed record Line(double X, double Y, string Text, double Size = 12);

    public static byte[] Build(IReadOnlyList<Line> lines, int rotate = 0, double[]? cropBox = null,
                               double width = 612, double height = 792)
    {
        var content = new StringBuilder();
        foreach (var l in lines)
            content.Append(Inv($"BT /F1 {l.Size} Tf {l.X} {l.Y} Td ({Escape(l.Text)}) Tj ET\n"));
        string stream = content.ToString();
        string extra = (rotate != 0 ? Inv($" /Rotate {rotate}") : "")
            + (cropBox is { Length: 4 } c ? Inv($" /CropBox [{c[0]} {c[1]} {c[2]} {c[3]}]") : "");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            Inv($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}]{extra} /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
        };
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public static byte[] Blank() => Build(Array.Empty<Line>());

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 2: Write the failing tests** `tests/Lumenotepad.Tests/PdfTextTests.cs`

The ink test is the ground truth: whatever the renderer paints must sit inside the boxes the text layer reports.

```csharp
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
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfTextTests`
Expected: build error, `PdfText` not found.

- [ ] **Step 4: Add the gate** in `PdfRenderer.cs`: add `internal static readonly object Gate = new();` and wrap each PDFtoImage call in `lock (Gate)`:

```csharp
    internal static readonly object Gate = new();

    public static int PageCount(byte[] pdf)
    {
        try { lock (Gate) return PDFtoImage.Conversion.GetPageCount(pdf); }
        catch { return 0; }
    }
```

In `PageSizes`, change the loop source to `lock (Gate) sizes = PDFtoImage.Conversion.GetPageSizes(pdf).ToList();` (add `using System.Linq;`) and iterate `sizes`. In `RenderPage` and `RenderPageSk`, wrap the `PDFtoImage.Conversion.ToImage(...)` call: `SkiaSharp.SKBitmap skbmp; lock (Gate) skbmp = PDFtoImage.Conversion.ToImage(...);` and keep the PNG encode outside the lock.

- [ ] **Step 5: Implement** `src/Lumenotepad/Services/PdfText.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumenotepad.Editor;

namespace Lumenotepad.Services;

public static class PdfText
{
    private const string Lib = "pdfium";
    private const int Scale = 100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FsRectF { public float Left, Top, Right, Bottom; }

    [DllImport(Lib)] private static extern void FPDF_InitLibrary();
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, IntPtr password);
    [DllImport(Lib)] private static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Lib)] private static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Lib)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] private static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int deviceX, out int deviceY);
    [DllImport(Lib)] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Lib)] private static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Lib)] private static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Lib)] private static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
    [DllImport(Lib)] private static extern int FPDFText_IsGenerated(IntPtr textPage, int index);
    [DllImport(Lib)] private static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out FsRectF rect);

    private static bool _initialized;
    private static bool _failureLogged;

    public static IReadOnlyList<PdfPageText> ReadAll(byte[] pdf)
    {
        if (pdf.Length == 0) return Array.Empty<PdfPageText>();
        IntPtr buf = IntPtr.Zero, doc = IntPtr.Zero;
        try
        {
            buf = Marshal.AllocHGlobal(pdf.Length);
            Marshal.Copy(pdf, 0, buf, pdf.Length);
            int count;
            lock (PdfRenderer.Gate)
            {
                if (!_initialized) { FPDF_InitLibrary(); _initialized = true; }
                doc = FPDF_LoadMemDocument64(buf, (UIntPtr)pdf.Length, IntPtr.Zero);
                count = doc == IntPtr.Zero ? 0 : FPDF_GetPageCount(doc);
            }
            var pages = new List<PdfPageText>(count);
            for (int i = 0; i < count; i++)
                lock (PdfRenderer.Gate) pages.Add(ReadPage(doc, i));
            return pages;
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                StartupLog.Mark($"pdf text unavailable: {ex.GetType().Name}: {ex.Message}");
            }
            return Array.Empty<PdfPageText>();
        }
        finally
        {
            if (doc != IntPtr.Zero) lock (PdfRenderer.Gate) FPDF_CloseDocument(doc);
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
    }

    private static PdfPageText ReadPage(IntPtr doc, int index)
    {
        IntPtr page = FPDF_LoadPage(doc, index);
        if (page == IntPtr.Zero) return new PdfPageText(index, Array.Empty<PdfChar>());
        IntPtr text = IntPtr.Zero;
        try
        {
            text = FPDFText_LoadPage(page);
            if (text == IntPtr.Zero) return new PdfPageText(index, Array.Empty<PdfChar>());
            int n = Math.Max(0, FPDFText_CountChars(text));
            var chars = new PdfChar[n];
            for (int c = 0; c < n; c++)
            {
                int cp = (int)FPDFText_GetUnicode(text, c);
                bool generated = FPDFText_IsGenerated(text, c) == 1;
                double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                if (cp is not ('\r' or '\n') && FPDFText_GetLooseCharBox(text, c, out var r) != 0
                    && r.Right > r.Left && r.Top > r.Bottom)
                {
                    var (ax, ay) = ToUnit(page, r.Left, r.Top);
                    var (bx, by) = ToUnit(page, r.Right, r.Bottom);
                    x0 = Math.Min(ax, bx); x1 = Math.Max(ax, bx);
                    y0 = Math.Min(ay, by); y1 = Math.Max(ay, by);
                }
                chars[c] = new PdfChar(cp, x0, y0, x1, y1, generated);
            }
            return new PdfPageText(index, chars);
        }
        finally
        {
            if (text != IntPtr.Zero) FPDFText_ClosePage(text);
            FPDF_ClosePage(page);
        }
    }

    private static (double X, double Y) ToUnit(IntPtr page, double px, double py)
    {
        FPDF_PageToDevice(page, 0, 0, Scale, Scale, 0, px, py, out int dx, out int dy);
        return (dx / (double)Scale, dy / (double)Scale);
    }
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter "PdfTextTests|PdfTextSelectionTests|PdfAnnotationTests"`
Expected: all PASS. If the rotation or crop assertion fails, the ink test says which side is right: fix the mapping, never the expectation.

- [ ] **Step 7: Commit**

```bash
git add src/Lumenotepad/Services/PdfRenderer.cs src/Lumenotepad/Services/PdfText.cs tests/Lumenotepad.Tests/TestPdf.cs tests/Lumenotepad.Tests/PdfTextTests.cs
git commit -m "Read PDF character boxes through PDFium under one shared lock"
```

---

### Task 4: Viewer shows, selects, deletes and exports text highlights

**Files:**
- Modify: `src/Lumenotepad/Views/PdfViewer.axaml.cs`
- Create: `src/Lumenotepad/Views/PdfViewer.TextSelect.cs`

**Interfaces:**
- Consumes: `PdfAnnotation.IsTextHighlight`, `Rects` (Task 1); `PdfText.ReadAll` (Task 3).
- Produces: `PageView` gains `Canvas Selection` and `Canvas Bar`; partial members `_pageText`, `_textReady`, `_pageStatus`, `CharsFor(int)`, `Unit(PageView, Point)`, `PageAt(int)`, `ResetText()`, `SetPageText(...)`, `Flash(string)`, `OnTextHighlightPressed(PageView, PdfAnnotation, PointerPressedEventArgs)`.

- [ ] **Step 1: Page layers.** Change the record and `BuildPageFrame`:

```csharp
    private sealed record PageView(int Index, double WPt, double HPt, Border Frame, Canvas Overlay, Image Img,
                                   Canvas Selection, Canvas Bar);
```

```csharp
        var img = new Image { Stretch = Stretch.Fill };
        var selection = new Canvas { IsHitTestVisible = false };
        var overlay = new Canvas { Background = Brushes.Transparent };
        var bar = new Canvas();
        var panel = new Panel();
        panel.Children.Add(img);
        panel.Children.Add(selection);
        panel.Children.Add(overlay);
        panel.Children.Add(bar);
```

and construct `new PageView(index, wpt, hpt, frame, overlay, img, selection, bar)`. In `LayoutPages` also set `pv.Selection.Width/Height` and `pv.Bar.Width/Height` to `w`/`h`.

- [ ] **Step 2: Load the text.** In `Load`, call `ResetText();` after `_selected = null;`. In `LoadAsync`, set `_pageStatus = count == 1 ? "1 page" : $"{count} pages"; StatusLabel.Text = _pageStatus;` in place of the current status line, and after the page render loop append:

```csharp
        var text = await Task.Run(() => PdfText.ReadAll(bytes));
        if (gen != _loadGen) return;
        SetPageText(text);
```

- [ ] **Step 3: Create** `Views/PdfViewer.TextSelect.cs` with the shared state and helpers:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

public partial class PdfViewer
{
    private IReadOnlyList<PdfPageText>? _pageText;
    private bool _textReady;
    private string _pageStatus = "";

    private IReadOnlyList<PdfChar>? CharsFor(int page) =>
        _pageText is { } t && page >= 0 && page < t.Count ? t[page].Chars : null;

    private static Point Unit(PageView pv, Point px) =>
        new(px.X / Math.Max(1, pv.Overlay.Width), px.Y / Math.Max(1, pv.Overlay.Height));

    private PageView? PageAt(int index) => index >= 0 && index < _pages.Count ? _pages[index] : null;

    private void ResetText()
    {
        _pageText = null;
        _textReady = false;
    }

    private void SetPageText(IReadOnlyList<PdfPageText> text)
    {
        _pageText = text;
        _textReady = true;
    }

    private void Flash(string message)
    {
        StatusLabel.Text = message;
        DispatcherTimer.RunOnce(() =>
        {
            if (StatusLabel.Text == message) StatusLabel.Text = _pageStatus;
        }, TimeSpan.FromSeconds(3));
    }

    private void OnTextHighlightPressed(PageView pv, PdfAnnotation a, PointerPressedEventArgs e)
    {
        if (!Left(e, pv)) return;
        Select(a, focusEditor: false);
        e.Handled = true;
    }
}
```

- [ ] **Step 4: Draw text highlights.** At the top of `DrawRectAnno` add `if (a.IsTextHighlight) { DrawTextHighlight(pv, a); return; }` and add to `PdfViewer.axaml.cs`:

```csharp
    private void DrawTextHighlight(PageView pv, PdfAnnotation a)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        bool selected = ReferenceEquals(a, _selected);
        var brush = new SolidColorBrush(Color.Parse(a.Color));
        double[]? lastRect = null;
        foreach (var r in a.Rects!)
        {
            if (r is not { Length: 4 }) continue;
            var box = new Border
            {
                Background = brush, CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(selected ? 2 : 0), BorderBrush = NoteFocusBrush,
                Tag = a,
            };
            Canvas.SetLeft(box, r[0] * w); Canvas.SetTop(box, r[1] * h);
            box.Width = r[2] * w; box.Height = r[3] * h;
            box.PointerPressed += (_, e) => OnTextHighlightPressed(pv, a, e);
            pv.Overlay.Children.Add(box);
            lastRect = r;
        }
        if (selected && lastRect is { } l) AddDeleteButton(pv, (l[0] + l[2]) * w, l[1] * h, a);
    }
```

- [ ] **Step 5: Export.** Replace `FlattenHighlight`:

```csharp
    private static void FlattenHighlight(SkiaSharp.SKCanvas c, PdfAnnotation a, float wpt, float hpt)
    {
        using var p = new SkiaSharp.SKPaint { Color = SkColor(a.Color), IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
        if (a.IsTextHighlight)
        {
            foreach (var r in a.Rects!)
            {
                if (r is not { Length: 4 }) continue;
                c.DrawRoundRect(new SkiaSharp.SKRect((float)r[0] * wpt, (float)r[1] * hpt,
                    (float)(r[0] + r[2]) * wpt, (float)(r[1] + r[3]) * hpt), 2f, 2f, p);
            }
            return;
        }
        var box = new SkiaSharp.SKRect((float)a.X * wpt, (float)a.Y * hpt, (float)(a.X + a.W) * wpt, (float)(a.Y + a.H) * hpt);
        c.DrawRoundRect(box, 3.5f, 3.5f, p);
    }
```

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 errors, all tests PASS. On-screen behaviour is verified in Task 8.

- [ ] **Step 7: Commit**

```bash
git add src/Lumenotepad/Views/PdfViewer.axaml.cs src/Lumenotepad/Views/PdfViewer.TextSelect.cs
git commit -m "Draw, select, delete and export multi-line text highlights"
```

---

### Task 5: Select text, then Highlight or Copy

**Files:**
- Modify: `src/Lumenotepad/Views/PdfViewer.TextSelect.cs`, `src/Lumenotepad/Views/PdfViewer.axaml.cs`

**Interfaces:**
- Consumes: Tasks 2 and 4.
- Produces: `HasTextSelection`, `ClearTextSelection()`, `RedrawTextLayers()`, `TrySelectPress`, `BeginTextPress`, `TextMove`, `TextRelease`, `AddTextHighlight(int page, int a, int b)`, `CopySelectionAsync()`, `UpdateTextCursor`. Task 6 adds `_pendPage/_pendAnchor/_pendFocus/_pendDragging` which `RedrawTextLayers` and `TextMove` already read, so declare them here.

- [ ] **Step 1: Selection state and drawing.** Add to the partial:

```csharp
    private int _selPage = -1, _selAnchor, _selFocus;
    private bool _selDragging;
    private PdfAnnotation? _pressedHighlight;
    private Point _selPressPx;

    private int _pendPage = -1, _pendAnchor, _pendFocus;
    private bool _pendDragging;
    private Point _pendPressPx;

    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);

    private bool HasTextSelection => _selPage >= 0 && _selAnchor != _selFocus;

    private void SetTextSelection(int page, int anchor, int focus)
    {
        _selPage = page; _selAnchor = anchor; _selFocus = focus;
        RedrawTextLayers();
    }

    private void ClearTextSelection()
    {
        if (_selPage < 0 && !_selDragging) return;
        _selPage = -1; _selDragging = false; _pressedHighlight = null;
        RedrawTextLayers();
    }

    private void RedrawTextLayers()
    {
        foreach (var p in _pages) { p.Selection.Children.Clear(); p.Bar.Children.Clear(); }
        if (PageAt(_pendPage) is { } pp && CharsFor(_pendPage) is { } pc)
        {
            if (_pendFocus != _pendAnchor)
                DrawTint(pp, PdfTextSelection.Rects(pc, Math.Min(_pendAnchor, _pendFocus), Math.Max(_pendAnchor, _pendFocus)),
                    new SolidColorBrush(Color.Parse(HighlightHex)));
            if (PdfTextSelection.CaretBox(pc, _pendAnchor) is { } mark)
            {
                var bar = new Border
                {
                    Width = 2, Height = mark.Height * pp.Overlay.Height, Background = AccentBrush,
                    CornerRadius = new CornerRadius(1), IsHitTestVisible = false,
                };
                Canvas.SetLeft(bar, mark.X * pp.Overlay.Width - 1); Canvas.SetTop(bar, mark.Y * pp.Overlay.Height);
                pp.Selection.Children.Add(bar);
            }
        }
        if (!HasTextSelection || PageAt(_selPage) is not { } pv || CharsFor(_selPage) is not { } chars) return;
        var rects = PdfTextSelection.Rects(chars, Math.Min(_selAnchor, _selFocus), Math.Max(_selAnchor, _selFocus));
        DrawTint(pv, rects, new SolidColorBrush(Color.Parse(ThemeManager.Current.FieldSelection)));
        if (!_selDragging && rects.Count > 0) ShowSelectionBar(pv, rects);
    }

    private static void DrawTint(PageView pv, IReadOnlyList<Rect> rects, IBrush brush)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        foreach (var r in rects)
        {
            var b = new Border { Background = brush, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
            Canvas.SetLeft(b, r.X * w); Canvas.SetTop(b, r.Y * h);
            b.Width = r.Width * w; b.Height = r.Height * h;
            pv.Selection.Children.Add(b);
        }
    }
```

- [ ] **Step 2: The pop-up bar, Highlight and Copy.**

```csharp
    private void ShowSelectionBar(PageView pv, IReadOnlyList<Rect> rects)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        var hl = new Button { Content = BarLabel("Highlight", SolidHex(_color)) };
        hl.Classes.Add("pill");
        var copy = new Button { Content = "Copy" };
        copy.Classes.Add("pill");
        ToolTip.SetTip(hl, "Highlight the selected text");
        ToolTip.SetTip(copy, "Copy the selected text");
        hl.Click += (_, _) => HighlightSelection();
        copy.Click += (_, _) => _ = CopySelectionAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(hl);
        row.Children.Add(copy);
        var bar = new Border
        {
            Background = this.FindResource("MenuBackgroundBrush") as IBrush ?? Brushes.DimGray,
            BorderBrush = this.FindResource("MenuBorderBrush") as IBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(5),
            BoxShadow = BoxShadows.Parse("0 6 18 0 #55000000"), Child = row,
        };
        pv.Bar.Children.Add(bar);
        bar.Measure(Size.Infinity);
        var size = bar.DesiredSize;
        var first = rects[0];
        var last = rects[^1];
        double x = Math.Clamp(first.X * w, 4, Math.Max(4, w - size.Width - 4));
        double y = first.Y * h - size.Height - 8;
        if (y < 4) y = Math.Min(h - size.Height - 4, (last.Y + last.Height) * h + 8);
        Canvas.SetLeft(bar, x); Canvas.SetTop(bar, y);
    }

    private static Control BarLabel(string text, string hex)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse(hex)), VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void HighlightSelection()
    {
        if (!HasTextSelection) return;
        int page = _selPage, a = _selAnchor, b = _selFocus;
        ClearTextSelection();
        AddTextHighlight(page, a, b);
    }

    private void AddTextHighlight(int page, int a, int b)
    {
        if (PageAt(page) is not { } pv || CharsFor(page) is not { } chars) return;
        var rects = PdfTextSelection.Rects(chars, Math.Min(a, b), Math.Max(a, b));
        if (rects.Count == 0) return;
        PushUndo();
        _annos.Items.Add(PdfAnnotation.TextHighlight(page, rects, HighlightHex));
        RedrawPage(pv);
        SaveNow();
    }

    private async Task CopySelectionAsync()
    {
        if (!HasTextSelection || CharsFor(_selPage) is not { } chars) return;
        string text = PdfTextSelection.TextOf(chars, Math.Min(_selAnchor, _selFocus), Math.Max(_selAnchor, _selFocus));
        if (text.Length == 0) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                await cb.SetTextAsync(text);
                Flash("Copied.");
            }
        }
        catch { }
    }
```

- [ ] **Step 3: Pointer handling.** Add to the partial:

```csharp
    private void UpdateTextCursor(PageView pv, Point px)
    {
        bool textTool = _tool == Tool.Select || (_tool == Tool.Highlight && HighlightByTextPref);
        var chars = textTool ? CharsFor(pv.Index) : null;
        bool over = chars is not null && PdfTextSelection.CharAt(chars, Unit(pv, px)) >= 0;
        var want = over ? TextCursor : null;
        if (!ReferenceEquals(pv.Overlay.Cursor, want)) pv.Overlay.Cursor = want;
    }

    private bool TrySelectPress(PageView pv, PointerPressedEventArgs e)
    {
        var chars = CharsFor(pv.Index);
        if (chars is null)
        {
            if (!_textReady) Flash("Reading the page text…");
            return false;
        }
        var px = e.GetPosition(pv.Overlay);
        var u = Unit(pv, px);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selPage == pv.Index)
        {
            int c = PdfTextSelection.CaretAt(chars, u);
            if (c < 0) return false;
            _selFocus = c;
            RedrawTextLayers();
            Focus();
            e.Handled = true;
            return true;
        }
        int hit = PdfTextSelection.CharAt(chars, u);
        if (hit < 0) return false;
        BeginTextPress(pv, chars, hit, u, px, e);
        return true;
    }

    private void BeginTextPress(PageView pv, IReadOnlyList<PdfChar> chars, int hit, Point u, Point px, PointerPressedEventArgs e)
    {
        if (_selected is not null) Select(null, focusEditor: false);
        if (e.ClickCount == 2)
        {
            var (a, b) = PdfTextSelection.WordAt(chars, hit);
            _selDragging = false;
            SetTextSelection(pv.Index, a, b);
        }
        else if (e.ClickCount >= 3)
        {
            var (a, b) = PdfTextSelection.LineAt(chars, hit);
            _selDragging = false;
            SetTextSelection(pv.Index, a, b);
        }
        else
        {
            int c = PdfTextSelection.CaretAt(chars, u);
            _selDragging = true;
            _selPressPx = px;
            SetTextSelection(pv.Index, c, c);
            e.Pointer.Capture(pv.Overlay);
        }
        Focus();
        e.Handled = true;
    }

    private bool TextMove(PageView pv, Point px)
    {
        UpdateTextCursor(pv, px);
        if (_selDragging && _selPage == pv.Index && CharsFor(pv.Index) is { } chars)
        {
            int c = PdfTextSelection.CaretAt(chars, Unit(pv, px));
            if (c >= 0 && c != _selFocus) { _selFocus = c; RedrawTextLayers(); }
            return true;
        }
        if (_pendPage == pv.Index && CharsFor(pv.Index) is { } pc)
        {
            int c = PdfTextSelection.CaretAt(pc, Unit(pv, px));
            if (c >= 0 && c != _pendFocus) { _pendFocus = c; RedrawTextLayers(); }
            return _pendDragging;
        }
        return false;
    }

    private bool TextRelease(PageView pv, PointerReleasedEventArgs e)
    {
        if (_selDragging)
        {
            _selDragging = false;
            e.Pointer.Capture(null);
            var pressed = _pressedHighlight;
            _pressedHighlight = null;
            if (Dist(e.GetPosition(pv.Overlay), _selPressPx) < 4 || _selAnchor == _selFocus)
            {
                ClearTextSelection();
                if (pressed is not null && _annos.Items.Contains(pressed)) Select(pressed, focusEditor: false);
            }
            else RedrawTextLayers();
            return true;
        }
        return false;
    }
```

Replace `OnTextHighlightPressed` with the select-mode version (Task 6 inserts the highlight-mode line at its top):

```csharp
    private void OnTextHighlightPressed(PageView pv, PdfAnnotation a, PointerPressedEventArgs e)
    {
        if (!Left(e, pv)) return;
        if (_tool == Tool.Select && CharsFor(pv.Index) is { } chars)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selPage == pv.Index && TrySelectPress(pv, e)) return;
            var px = e.GetPosition(pv.Overlay);
            var u = Unit(pv, px);
            int hit = PdfTextSelection.CharAt(chars, u);
            if (hit >= 0)
            {
                if (e.ClickCount == 1) _pressedHighlight = a;
                BeginTextPress(pv, chars, hit, u, px, e);
                return;
            }
        }
        ClearTextSelection();
        Select(a, focusEditor: false);
        e.Handled = true;
    }
```

- [ ] **Step 4: Wire into the viewer** (`PdfViewer.axaml.cs`):

In `OnOverlayPressed`, after `var p = e.GetPosition(pv.Overlay);`:

```csharp
        if (_tool == Tool.Select && TrySelectPress(pv, e)) return;
        ClearTextSelection();
```

At the top of `OnOverlayMoved`, after `var p = e.GetPosition(pv.Overlay);`:

```csharp
        if (_drag is null && _dragPreview is null && _arrowPreview is null && TextMove(pv, p)) return;
```

At the top of `OnOverlayReleased`: `if (TextRelease(pv, e)) return;`

In `OnKey`, after the `TextBox or RichTextEditor` check and the `ctrl`/`shift` locals:

```csharp
        if (e.Key == Key.Escape && (HasTextSelection || _pendPage >= 0))
        {
            _pendPage = -1; _pendDragging = false;
            ClearTextSelection();
            RedrawTextLayers();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.C && HasTextSelection) { _ = CopySelectionAsync(); e.Handled = true; return; }
```

In `SetZoom` and `RefreshChrome`, call `RedrawTextLayers();` after the page redraw loop. In `PickColor`, after `RefreshSwatchRings();` add `RedrawTextLayers();`. In `SetTool`, first line: `_pendPage = -1; _pendDragging = false; ClearTextSelection(); RedrawTextLayers();`. In `ResetText`, also reset `_selPage = -1; _selDragging = false; _pressedHighlight = null; _pendPage = -1; _pendDragging = false;`.

`HighlightByTextPref` is referenced by `UpdateTextCursor`; declare it now in the partial: `public static bool HighlightByTextPref = true;`.

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 errors, all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Lumenotepad/Views/PdfViewer.axaml.cs src/Lumenotepad/Views/PdfViewer.TextSelect.cs
git commit -m "Select PDF text and highlight or copy it from a pop-up bar"
```

---

### Task 6: Highlight tool: Text and Area modes, two-click highlighting

**Files:**
- Modify: `src/Lumenotepad/Views/PdfViewer.axaml`, `src/Lumenotepad/Views/PdfViewer.axaml.cs`, `src/Lumenotepad/Views/PdfViewer.TextSelect.cs`

**Interfaces:**
- Consumes: Task 5.
- Produces: `public static event Action<bool>? PdfViewer.HighlightModeChanged`; `TryHighlightTextPress(PageView, PointerPressedEventArgs)`; `SetHighlightMode(bool)`.

- [ ] **Step 1: XAML.** After `HighlightTool` add:

```xml
                <Button x:Name="HighlightOptsBtn" Classes="mini" Content="&#x25BE;"
                        ToolTip.Tip="Highlight by text, or by drawing a box"/>
```

- [ ] **Step 2: Two-click flow and mode menu.** Add to the partial:

```csharp
    public static event Action<bool>? HighlightModeChanged;

    private void CancelPending()
    {
        if (_pendPage < 0 && !_pendDragging) return;
        _pendPage = -1; _pendDragging = false;
        RedrawTextLayers();
    }

    private bool TryHighlightTextPress(PageView pv, PointerPressedEventArgs e)
    {
        var chars = CharsFor(pv.Index);
        if (chars is null)
        {
            if (_textReady) return false;
            Flash("Reading the page text…");
            e.Handled = true;
            return true;
        }
        var px = e.GetPosition(pv.Overlay);
        var u = Unit(pv, px);
        if (_pendPage >= 0 && _pendPage != pv.Index) CancelPending();
        if (_pendPage == pv.Index)
        {
            int word = e.ClickCount == 2 ? PdfTextSelection.CharAt(chars, u) : -1;
            if (word >= 0)
            {
                var (wa, wb) = PdfTextSelection.WordAt(chars, word);
                CancelPending();
                AddTextHighlight(pv.Index, wa, wb);
                e.Handled = true;
                return true;
            }
            int end = PdfTextSelection.CaretAt(chars, u);
            int start = _pendAnchor;
            CancelPending();
            if (end < 0) return false;
            if (end != start) AddTextHighlight(pv.Index, start, end);
            e.Handled = true;
            return true;
        }
        if (PdfTextSelection.CharAt(chars, u) < 0)
        {
            if (!chars.Any(c => c.HasBox))
                Flash("This page is a picture, so there's no text to select. Drag to highlight an area.");
            return false;
        }
        ClearTextSelection();
        if (_selected is not null) Select(null, focusEditor: false);
        int caret = PdfTextSelection.CaretAt(chars, u);
        _pendPage = pv.Index; _pendAnchor = caret; _pendFocus = caret;
        _pendDragging = true; _pendPressPx = px;
        e.Pointer.Capture(pv.Overlay);
        RedrawTextLayers();
        Focus();
        e.Handled = true;
        return true;
    }

    private bool PendingRelease(PageView pv, PointerReleasedEventArgs e)
    {
        if (!_pendDragging) return false;
        _pendDragging = false;
        e.Pointer.Capture(null);
        if (_pendPage >= 0 && _pendFocus != _pendAnchor && Dist(e.GetPosition(pv.Overlay), _pendPressPx) >= 6)
        {
            int page = _pendPage, a = _pendAnchor, b = _pendFocus;
            CancelPending();
            AddTextHighlight(page, a, b);
        }
        else Flash("Now click where the highlight should end.");
        return true;
    }

    private void UpdateHighlightTip() => ToolTip.SetTip(HighlightTool, HighlightByTextPref
        ? "Click where the text starts, then where it ends. Double-click a word to highlight just that word."
        : "Drag over the page to highlight an area");

    private void SetHighlightMode(bool byText)
    {
        CancelPending();
        if (HighlightByTextPref != byText)
        {
            HighlightByTextPref = byText;
            HighlightModeChanged?.Invoke(byText);
        }
        UpdateHighlightTip();
        if (_tool != Tool.Highlight) SetTool(Tool.Highlight);
    }

    private void ShowHighlightOptions()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(6), Width = 250 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        panel.Children.Add(ModeButton(true, "Text", "Click where the text starts, then where it ends."));
        panel.Children.Add(ModeButton(false, "Area", "Drag a box over any part of the page."));
        MenuFx.AttachFlyout(flyout);
        flyout.ShowAt(HighlightOptsBtn);

        Button ModeButton(bool byText, string title, string hint)
        {
            bool on = HighlightByTextPref == byText;
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7), BorderThickness = new Thickness(on ? 2 : 1),
                BorderBrush = (on ? this.FindResource("AccentBrush") : this.FindResource("FrameBorderBrush")) as IBrush,
                Content = content,
            };
            b.Click += (_, _) => { SetHighlightMode(byText); flyout.Hide(); };
            return b;
        }
    }
```

- [ ] **Step 3: Wire it.** In the constructor: `HighlightOptsBtn.Click += (_, _) => ShowHighlightOptions(); UpdateHighlightTip();`. In `OnAttachedToVisualTree`: `UpdateHighlightTip();`. In `OnOverlayPressed`, before the `Tool.Select` line: `if (_tool == Tool.Highlight && HighlightByTextPref && TryHighlightTextPress(pv, e)) return;`. In `TextRelease`, before `return false;`: `if (PendingRelease(pv, e)) return true;`. At the top of `OnTextHighlightPressed`, after the `Left` check: `if (_tool == Tool.Highlight && HighlightByTextPref && TryHighlightTextPress(pv, e)) return;`. Replace the Escape branch's body in `OnKey` with `CancelPending(); ClearTextSelection(); e.Handled = true; return;`.

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 errors, all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Views/PdfViewer.axaml src/Lumenotepad/Views/PdfViewer.axaml.cs src/Lumenotepad/Views/PdfViewer.TextSelect.cs
git commit -m "Add Text and Area highlight modes with two-click text highlighting"
```

---

### Task 7: Remember the highlight mode

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`, `src/Lumenotepad/Views/MainView.axaml.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `PdfViewer.HighlightByTextPref`, `PdfViewer.HighlightModeChanged` (Tasks 5 and 6).
- Produces: `AppSettings.PdfHighlightText`, `MainViewModel.PdfHighlightText`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void PdfHighlightText_defaultsTrue_andRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.True(new AppSettings().PdfHighlightText);
            new AppSettings { PdfHighlightText = false }.Save(dir);
            Assert.False(AppSettings.Load(dir).PdfHighlightText);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

(in `AppSettingsTests`), and in `MainViewModelTests`:

```csharp
    [Fact]
    public void PdfHighlightText_persists_andResetRestoresIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.PdfHighlightText = false;
            Assert.False(AppSettings.Load(dir).PdfHighlightText);
            vm.ResetSettingsToDefaults();
            Assert.True(vm.PdfHighlightText);
            Assert.True(AppSettings.Load(dir).PdfHighlightText);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PdfHighlightText`
Expected: build error, no `PdfHighlightText`.

- [ ] **Step 3: Implement.**
  - `AppSettings`: `public bool PdfHighlightText { get; set; } = true;` next to `RoundedPdfCorners`.
  - `MainViewModel`: `[ObservableProperty] private bool _pdfHighlightText = true;` next to `_roundedPdfCorners`; in the settings load block after `RoundedPdfCorners = _settings.RoundedPdfCorners;` add `PdfHighlightText = _settings.PdfHighlightText;`; after `OnRoundedPdfCornersChanged` add

    ```csharp
    partial void OnPdfHighlightTextChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PdfHighlightText = value;
        _settings.Save(_settingsDir);
    }
    ```

    and in `ResetSettingsToDefaults` after `RoundedPdfCorners = d.RoundedPdfCorners;` add `PdfHighlightText = d.PdfHighlightText;`.
  - `MainView.axaml.cs`: in `ApplyCanvasPrefs` after `PdfViewer.RoundedPagePref = vm.RoundedPdfCorners;` add `PdfViewer.HighlightByTextPref = vm.PdfHighlightText;`; add `or nameof(MainViewModel.PdfHighlightText)` to the `ApplyCanvasPrefs` property list; in the constructor add
    `PdfViewer.HighlightModeChanged += v => { if (Vm is { } m && m.PdfHighlightText != v) m.PdfHighlightText = v; };`

- [ ] **Step 4: Run to verify they pass, then the full suite**

Run: `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Services/AppSettings.cs src/Lumenotepad/ViewModels/MainViewModel.cs src/Lumenotepad/Views/MainView.axaml.cs tests/Lumenotepad.Tests/AppSettingsTests.cs tests/Lumenotepad.Tests/MainViewModelTests.cs
git commit -m "Remember the PDF highlight mode"
```

---

### Task 8: Drive the real viewer, then prepare 1.2.14

**Files:**
- Temporarily replace: `tools/CardRepro/Program.cs` (restore with `git checkout -- tools/CardRepro/Program.cs`)
- Modify: `src/Lumenotepad/Lumenotepad.csproj` (`<Version>1.2.14</Version>`)
- Create: `docs/release-notes-1.2.14.md`

- [ ] **Step 1: Probe.** Replace `tools/CardRepro/Program.cs` with a headless probe that: configures the app with the embedded font collection; writes a two-page PDF with SkiaSharp (`SKDocument.CreatePdf`), page 1 holding three lines of 12 pt text drawn with a system typeface at known positions, page 2 holding only a filled rectangle; hosts a `PdfViewer` in a 1000 × 900 `Window`, calls `Load(path, false)` and pumps the dispatcher until `_textReady` (reflection) or 10 s; then checks, reading `_annos`, `_selPage`, `_selAnchor`, `_selFocus`, `_pendPage` and `_tool` by reflection and pressing with `window.MouseDown/MouseMove/MouseUp` at points translated from 0 to 1 character boxes via `pv.Overlay.TranslatePoint`:
  1. Double-click a word in Select mode: the selection equals `WordAt`, a bar with two buttons exists on page 1's `Bar` layer; clicking its first button adds one annotation with `IsTextHighlight` and one rect.
  2. Click on line 1, Shift-click on line 3, click Highlight: one annotation with three rects.
  3. Highlight tool (Text): click a start caret, move, check a preview exists on the `Selection` layer, click the end: a text highlight is added; `_pendPage` is back to -1.
  4. Highlight tool (Text) on page 2: the status line shows the picture message; a drag adds a plain box highlight.
  5. Click an existing text highlight without moving: `_selected` is that annotation. Delete key: gone. Ctrl+Z: back.
  6. `BuildFlattenedPdf()` (reflection), render its page 1 through `PdfRenderer.RenderPageSk`: the pixel at the centre of each stored rect is tinted, not white.
  7. Save `CaptureRenderedFrame()` PNGs after 1, 2 and 3 and look at them: tints sit exactly on the words.

  Print PASS/FAIL per check. Run: `cd tools/CardRepro && dotnet run -c Release`. Expected: every check PASS. Fix the viewer, not the probe, on any FAIL.

- [ ] **Step 2: Restore the harness**

Run: `git checkout -- tools/CardRepro/Program.cs`

- [ ] **Step 3: Version and notes.** Set `<Version>1.2.14</Version>`. Write `docs/release-notes-1.2.14.md` starting with `<!-- summary: Highlight PDF text by selecting it, or with two clicks, instead of dragging a box. -->`, describing: select then Highlight/Copy; Shift-click, double-click and triple-click; the Highlight ▾ Text and Area modes and two-click highlighting; one highlight across several lines; scanned pages fall back to a box; the footer paragraph about notes being untouched used by every release note.

- [ ] **Step 4: Full verification**

Run: `dotnet build src/Lumenotepad -c Release` and `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 warnings introduced, all PASS.

- [ ] **Step 5: Commit, and stop before publishing**

```bash
git add src/Lumenotepad/Lumenotepad.csproj docs/release-notes-1.2.14.md
git commit -m "Prepare 1.2.14"
```

Publishing waits for the owner's go-ahead.
