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

    public const double RuleSpacing = 28;   // Sentence/Ruled line pitch
    public const double RuleTop = 48;       // first Sentence rule
    public const double HeaderY = 64;       // Charting header underline
    public const double BoxMargin = 24;     // Boxing outer margin
    public const double BoxGap = 16;        // Boxing gap between boxes

    public static GuideSet For(string pageStyle, Size viewport, Size canvas)
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
                double cue = System.Math.Round(vw * 0.28), sum = System.Math.Round(vh * 0.80);
                lines.Add((new Point(cue, 0), new Point(cue, sum)));
                lines.Add((new Point(0, sum), new Point(cw, sum)));
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
}
