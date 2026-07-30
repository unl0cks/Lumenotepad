using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Lumenotepad.Editor;

public static class PageStyleTemplate
{
    public const double Margin = 16;

    public static IReadOnlyList<NoteBox> StartersFor(string pageStyle, int mode, Size viewport)
    {
        double vw = viewport.Width > 0 ? viewport.Width : 900;
        double vh = viewport.Height > 0 ? viewport.Height : 600;
        var list = new List<NoteBox>();

        if (pageStyle == PageStyles.Mindmap)
        {
            list.Add(new NoteBox(Label("Central idea"))
            {
                X = System.Math.Round(vw / 2) - 110, Y = System.Math.Round(vh * 0.4), Width = 220,
            });
            return list;
        }

        bool docked = mode != PageStyles.ModeStartersOnly;
        foreach (var (id, rect) in PageStyleGuides.Regions(pageStyle, new Size(vw, vh), default))
            list.Add(new NoteBox(DocFor(pageStyle, id))
            {
                X = rect.X, Y = rect.Y, Width = rect.Width, H = 0,
                Locked = docked, Region = docked ? id : null,
            });
        return list;
    }

    private static RichDocument DocFor(string pageStyle, string id)
    {
        if (pageStyle == PageStyles.Outline)
        {
            var doc = Label("Topic");
            doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Main idea" } }, Bullet = "dot" });
            doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Supporting detail" } }, Bullet = "dot" });
            return doc;
        }
        if (pageStyle == PageStyles.Sentence)
        {
            var doc = new RichDocument();
            doc.Paragraphs.Clear();
            doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "First point" } }, Bullet = "num" });
            return doc;
        }
        return Label(LabelFor(pageStyle, id));
    }

    public static int RetagLegacyStarters(IReadOnlyList<NoteBox> boxes, string pageStyle, Size viewport)
    {
        int tagged = 0;
        foreach (var (id, _) in PageStyleGuides.Regions(pageStyle, viewport, default))
        {
            if (boxes.Any(b => b.Region == id)) continue;
            string want = RegionLabel(pageStyle, id);
            if (string.IsNullOrEmpty(want)) continue;
            var box = boxes.FirstOrDefault(b => b.Region is null
                && b.ImagePath is null && b.Table is null && b.Divider is null
                && (b.Doc.Paragraphs.Count > 0 ? b.Doc.Paragraphs[0].Text.Trim() : "") == want);
            if (box is null) continue;
            box.Region = id; box.Locked = true; box.H = 0; tagged++;
        }
        return tagged;
    }

    public static string RegionLabel(string pageStyle, string id) => pageStyle switch
    {
        PageStyles.Outline => "Topic",
        PageStyles.Sentence => "First point",
        _ => LabelFor(pageStyle, id),
    };

    private static string LabelFor(string pageStyle, string id)
    {
        if (pageStyle == PageStyles.Cornell)
            return id switch { "cue" => "Cue", "notes" => "Notes", "summary" => "Summary", _ => "" };
        if (id.Length == 2 && char.IsDigit(id[1]))
        {
            int n = id[1] - '0' + 1;
            return pageStyle == PageStyles.Boxing ? $"Topic {n}" : $"Column {n}";
        }
        return "";
    }

    private static RichDocument Label(string text)
    {
        var d = new RichDocument();
        d.Paragraphs.Clear();
        d.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = text, Bold = true } } });
        return d;
    }
}
