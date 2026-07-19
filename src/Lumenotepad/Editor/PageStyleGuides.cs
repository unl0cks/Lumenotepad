using System.Collections.Generic;
using Avalonia;

namespace Lumenotepad.Editor;

/// <summary>Pure guide geometry for the page styles: which lines/boxes a style draws, computed from
/// the VIEWPORT (divider positions — the "one screen" the method is designed around) and the CANVAS
/// (how far lines extend as the page grows). Rendered by GuideLayer; unit-tested here.</summary>
public static class PageStyleGuides
{
    public sealed record GuideSet(
        IReadOnlyList<(Point A, Point B)> Lines,
        IReadOnlyList<Rect> Boxes)
    {
        public static readonly GuideSet Empty = new(new List<(Point, Point)>(), new List<Rect>());
    }

    public const double Margin = 16;        // must match PageStyleTemplate.Margin (region insets)
    public const double RuleSpacing = 28;   // Sentence/Ruled line pitch
    public const double RuleTop = 48;       // first Sentence rule
    public const double HeaderY = 64;       // Charting header underline
    public const double BoxMargin = 24;     // Boxing outer margin
    public const double BoxGap = 16;        // Boxing gap between boxes

    public static GuideSet For(string pageStyle, Size viewport, Size canvas, double contentBottom = 0)
    {
        // Divider positions come from the viewport; a zero viewport (not yet measured) uses the canvas.
        double vw = viewport.Width > 0 ? viewport.Width : canvas.Width;
        double vh = viewport.Height > 0 ? viewport.Height : canvas.Height;
        double cw = canvas.Width, ch = canvas.Height;
        var lines = new List<(Point, Point)>();
        var boxes = new List<Rect>();

        // Positions are rounded to WHOLE PIXELS: crisper 1px lines, and fraction-of-viewport math
        // isn't exactly representable in binary floats (900 × 0.28 = 252.00000000000003) — rounding
        // keeps the geometry (and the tests) on clean values. PageStyleTemplate rounds identically
        // so starters always align with the guides.
        switch (pageStyle)
        {
            case PageStyles.Cornell:
                // The cue/notes rule and the summary rule come from CornellMetrics — the SAME geometry
                // the docked region boxes snap to (NoteCanvas), so the lines and the labelled boxes can
                // never drift apart on resize. contentBottom is the notes content foot (breathing-room
                // and the summary box excluded); the summary rule sits just below it and the cue rule
                // runs down to meet it, so both descend as the notes grow.
                var (cx, sy) = CornellMetrics(vw, vh, contentBottom);
                lines.Add((new Point(cx, 0), new Point(cx, sy)));
                lines.Add((new Point(0, sy), new Point(cw, sy)));
                break;
            case PageStyles.TwoColumn:
                double half = System.Math.Round(vw * 0.5);
                lines.Add((new Point(half, 0), new Point(half, ch)));
                break;
            case PageStyles.Outline:
                foreach (double x in new[] { 48.0, 88.0, 128.0 })
                    lines.Add((new Point(x, 0), new Point(x, ch)));
                break;
            case PageStyles.Charting:
                double c1 = System.Math.Round(vw / 3), c2 = System.Math.Round(vw * 2 / 3);
                lines.Add((new Point(c1, 0), new Point(c1, ch)));
                lines.Add((new Point(c2, 0), new Point(c2, ch)));
                lines.Add((new Point(0, HeaderY), new Point(cw, HeaderY)));
                break;
            case PageStyles.Boxing:
                double bw = System.Math.Round((vw - 2 * BoxMargin - BoxGap) / 2);
                double bh = System.Math.Round((vh - 2 * BoxMargin - BoxGap) / 2);
                boxes.Add(new Rect(BoxMargin, BoxMargin, bw, bh));
                boxes.Add(new Rect(BoxMargin + bw + BoxGap, BoxMargin, bw, bh));
                boxes.Add(new Rect(BoxMargin, BoxMargin + bh + BoxGap, bw, bh));
                boxes.Add(new Rect(BoxMargin + bw + BoxGap, BoxMargin + bh + BoxGap, bw, bh));
                break;
            case PageStyles.Sentence:
                for (double y = RuleTop; y <= ch; y += RuleSpacing)
                    lines.Add((new Point(0, y), new Point(cw, y)));
                break;
        }
        return lines.Count == 0 && boxes.Count == 0 ? GuideSet.Empty : new GuideSet(lines, boxes);
    }

    /// <summary>The Cornell divider geometry both the guide lines and the docked region boxes derive
    /// from, so they can never drift apart. <c>Cue</c> = the cue|notes column split (a viewport
    /// fraction). <c>Sum</c> = the summary rule: it sits a little below the notes content
    /// (<paramref name="notesFoot"/>, the summary box excluded) but never rides above its 80%-of-screen
    /// home — so it starts on the first screen and descends as the notes grow, the cue rule running
    /// down to meet it. A zero/empty notesFoot just parks the summary at its 80% home.</summary>
    public static (double Cue, double Sum) CornellMetrics(double vw, double vh, double notesFoot)
    {
        double cue = System.Math.Round(vw * 0.28);
        double home = System.Math.Round(vh * 0.80);
        double gap = System.Math.Round(vh * 0.05);
        double sum = System.Math.Max(home, notesFoot > 0 ? notesFoot + gap : home);
        return (cue, sum);
    }

    /// <summary>The three Cornell region rects (cue / notes / summary), in canvas coords, that the
    /// docked starter boxes snap to — the same math as the guide lines. Widths are viewport-relative
    /// so the columns fill the visible page; heights are 0 (auto — the box grows with its content).</summary>
    public static (Rect Cue, Rect Notes, Rect Summary) CornellRegions(double vw, double vh, double notesFoot)
    {
        var (cue, sum) = CornellMetrics(vw, vh, notesFoot);
        const double m = Margin;
        return (
            new Rect(m, m, System.Math.Max(NoteBoxMinWidth, cue - 2 * m), 0),
            new Rect(cue + m, m, System.Math.Max(NoteBoxMinWidth, vw - cue - 2 * m), 0),
            new Rect(m, sum + 12, System.Math.Max(NoteBoxMinWidth, vw - 2 * m), 0));
    }

    /// <summary>Floor for a region box width — mirrors NoteBox.MinWidth without an Avalonia-model
    /// dependency in this pure geometry file.</summary>
    private const double NoteBoxMinWidth = 140;

    /// <summary>The docking rects for a structured style's labelled starter boxes, keyed by a stable
    /// region id, in canvas coords — the SAME viewport math the guide lines use, so a page's boxes and
    /// its guides scale together and never drift apart on resize. Rects are auto-height (Height 0): the
    /// box sits at the top-left of its region and grows with its content. Cornell's summary is the one
    /// region that also descends with the notes (<paramref name="notesFoot"/>); the rest ignore it.
    /// Styles with no docked starters (Freeform, Mindmap) return an empty list.</summary>
    public static IReadOnlyList<(string Id, Rect Rect)> Regions(
        string pageStyle, Size viewport, Size canvas, double notesFoot = 0)
    {
        double vw = viewport.Width > 0 ? viewport.Width : (canvas.Width > 0 ? canvas.Width : 900);
        double vh = viewport.Height > 0 ? viewport.Height : (canvas.Height > 0 ? canvas.Height : 600);
        const double m = Margin;
        double W(double w) => System.Math.Max(NoteBoxMinWidth, w);
        var r = new List<(string, Rect)>();
        switch (pageStyle)
        {
            case PageStyles.Cornell:
            {
                var (cue, notes, summary) = CornellRegions(vw, vh, notesFoot);
                r.Add(("cue", cue)); r.Add(("notes", notes)); r.Add(("summary", summary));
                break;
            }
            case PageStyles.TwoColumn:
            {
                double half = System.Math.Round(vw * 0.5);
                r.Add(("c0", new Rect(m, m, W(half - 2 * m), 0)));
                r.Add(("c1", new Rect(half + m, m, W(vw - half - 2 * m), 0)));
                break;
            }
            case PageStyles.Boxing:
            {
                double bw = System.Math.Round((vw - 2 * BoxMargin - BoxGap) / 2);
                double bh = System.Math.Round((vh - 2 * BoxMargin - BoxGap) / 2);
                var cells = new[]
                {
                    (BoxMargin, BoxMargin), (BoxMargin + bw + BoxGap, BoxMargin),
                    (BoxMargin, BoxMargin + bh + BoxGap), (BoxMargin + bw + BoxGap, BoxMargin + bh + BoxGap),
                };
                for (int i = 0; i < cells.Length; i++)
                    r.Add(($"b{i}", new Rect(cells[i].Item1 + 12, cells[i].Item2 + 12, W(bw - 24), 0)));
                break;
            }
            case PageStyles.Charting:
            {
                double col = System.Math.Round(vw / 3);
                for (int i = 0; i < 3; i++)
                    r.Add(($"h{i}", new Rect(i * col + m, m, W(col - 2 * m), 0)));
                break;
            }
            case PageStyles.Outline:
                r.Add(("outline", new Rect(m, m, W(vw - 2 * m), 0)));
                break;
            case PageStyles.Sentence:
                r.Add(("sentence", new Rect(m, RuleTop - 8, W(vw - 2 * m), 0)));
                break;
        }
        return r;
    }
}
