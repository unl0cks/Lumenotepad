using System.Collections.Generic;
using Avalonia;

namespace Lumenotepad.Editor;

/// <summary>Pure starter-container templates: the labelled NoteBoxes a page style stamps onto a fresh
/// page. Every structured style's starters are DOCKED regions — tagged with a region id and locked, so
/// NoteCanvas keeps them snapped to the live guide geometry (PageStyleGuides.Regions) and the boxes and
/// their guide lines scale together, never drifting apart on resize. Starters-only mode (no guides
/// drawn) is the exception: with no lines to align to, the boxes are placed free and movable.</summary>
public static class PageStyleTemplate
{
    public const double Margin = 16;

    public static IReadOnlyList<NoteBox> StartersFor(string pageStyle, int mode, Size viewport)
    {
        double vw = viewport.Width > 0 ? viewport.Width : 900;
        double vh = viewport.Height > 0 ? viewport.Height : 600;
        var list = new List<NoteBox>();

        // Mindmap: a single, always-free central bubble (branches grow by dragging bubbles onto it).
        if (pageStyle == PageStyles.Mindmap)
        {
            list.Add(new NoteBox(Label("Central idea"))
            {
                X = System.Math.Round(vw / 2) - 110, Y = System.Math.Round(vh * 0.4), Width = 220,
            });
            return list;
        }

        // Every other structured style stamps one box per guide region. With guides drawn (Guides /
        // Rigid) the boxes dock + lock to those regions; starters-only leaves them free hints.
        bool docked = mode != PageStyles.ModeStartersOnly;
        foreach (var (id, rect) in PageStyleGuides.Regions(pageStyle, new Size(vw, vh), default))
            list.Add(new NoteBox(DocFor(pageStyle, id))
            {
                X = rect.X, Y = rect.Y, Width = rect.Width, H = 0,
                Locked = docked, Region = docked ? id : null,
            });
        return list;
    }

    /// <summary>The starter document for a region: Outline/Sentence carry their example bullets;
    /// everything else gets a one-line bold label.</summary>
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

    /// <summary>The label a region's starter box shows. Numbered styles derive "Topic N"/"Column N"
    /// from the region id's trailing digit (b0 → Topic 1, c0/h0 → Column 1).</summary>
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

    /// <summary>A one-line bold label document.</summary>
    private static RichDocument Label(string text)
    {
        var d = new RichDocument();
        d.Paragraphs.Clear();
        d.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = text, Bold = true } } });
        return d;
    }
}
