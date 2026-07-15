using System.Collections.Generic;
using Avalonia;

namespace Lumenotepad.Editor;

/// <summary>Pure starter-container templates: the labelled NoteBoxes a page style stamps onto a
/// fresh page, positioned to match PageStyleGuides' regions. Mode 2 (rigid) locks the boxes and
/// fixes their heights to the regions; other modes leave them movable with auto height.</summary>
public static class PageStyleTemplate
{
    public const double Margin = 16;

    public static IReadOnlyList<NoteBox> StartersFor(string pageStyle, int mode, Size viewport)
    {
        double vw = viewport.Width > 0 ? viewport.Width : 900;
        double vh = viewport.Height > 0 ? viewport.Height : 600;
        bool rigid = mode == PageStyles.ModeRigid;
        var list = new List<NoteBox>();

        void Add(double x, double y, double w, double h, RichDocument doc)
        {
            list.Add(new NoteBox(doc) { X = x, Y = y, Width = w, H = rigid ? h : 0, Locked = rigid });
        }

        switch (pageStyle)
        {
            case PageStyles.Cornell:
            {
                // Rounded exactly like PageStyleGuides, so starters sit ON the drawn regions
                // (fraction-of-viewport math isn't binary-exact: 900 × 0.28 ≠ 252 unrounded).
                double cue = System.Math.Round(vw * 0.28), sum = System.Math.Round(vh * 0.80);
                Add(Margin, Margin, cue - 2 * Margin, sum - 2 * Margin, Label("Cue"));
                Add(cue + Margin, Margin, vw - cue - 2 * Margin, sum - 2 * Margin, Label("Notes"));
                Add(Margin, sum + 12, vw - 2 * Margin, vh - sum - 28, Label("Summary"));
                break;
            }
            case PageStyles.TwoColumn:
            {
                double half = System.Math.Round(vw * 0.5);
                Add(Margin, Margin, half - 2 * Margin, vh - 2 * Margin, Label("Column 1"));
                Add(half + Margin, Margin, half - 2 * Margin, vh - 2 * Margin, Label("Column 2"));
                break;
            }
            case PageStyles.Outline:
            {
                var doc = Label("Topic");
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Main idea" } }, Bullet = "dot" });
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Supporting detail" } }, Bullet = "dot" });
                Add(Margin, Margin, vw - 200, vh - 2 * Margin, doc);
                break;
            }
            case PageStyles.Boxing:
            {
                double bw = System.Math.Round((vw - 2 * PageStyleGuides.BoxMargin - PageStyleGuides.BoxGap) / 2);
                double bh = System.Math.Round((vh - 2 * PageStyleGuides.BoxMargin - PageStyleGuides.BoxGap) / 2);
                int n = 1;
                foreach (var (rx, ry) in new[]
                {
                    (PageStyleGuides.BoxMargin, PageStyleGuides.BoxMargin),
                    (PageStyleGuides.BoxMargin + bw + PageStyleGuides.BoxGap, PageStyleGuides.BoxMargin),
                    (PageStyleGuides.BoxMargin, PageStyleGuides.BoxMargin + bh + PageStyleGuides.BoxGap),
                    (PageStyleGuides.BoxMargin + bw + PageStyleGuides.BoxGap, PageStyleGuides.BoxMargin + bh + PageStyleGuides.BoxGap),
                })
                    Add(rx + 12, ry + 12, bw - 24, bh - 24, Label($"Topic {n++}"));
                break;
            }
            case PageStyles.Charting:
            {
                double col = System.Math.Round(vw / 3);
                for (int i = 0; i < 3; i++)
                    Add(i * col + Margin, Margin, col - 2 * Margin, 40, Label($"Column {i + 1}"));
                break;
            }
            case PageStyles.Sentence:
            {
                var doc = new RichDocument();
                doc.Paragraphs.Clear();
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "First point" } }, Bullet = "num" });
                Add(Margin, 40, vw - 2 * Margin, vh - 80, doc);
                break;
            }
            case PageStyles.Mindmap:
            {
                // One central bubble; branches grow by dragging new bubbles onto it. Never rigid —
                // a mindmap's whole point is moving bubbles, so the lock flag is ignored here.
                var box = new NoteBox(Label("Central idea"))
                {
                    X = System.Math.Round(vw / 2) - 110,
                    Y = System.Math.Round(vh * 0.4),
                    Width = 220,
                };
                list.Add(box);
                break;
            }
        }
        return list;
    }

    /// <summary>A one-line bold label document.</summary>
    private static RichDocument Label(string text)
    {
        var d = new RichDocument();
        d.Paragraphs.Clear();
        d.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = text, Bold = true } } });
        return d;
    }
}
