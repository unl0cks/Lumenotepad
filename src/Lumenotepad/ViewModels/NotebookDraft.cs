using System.Collections.Generic;

namespace Lumenotepad.ViewModels;

/// <summary>Everything the notebook wizard collects before anything real is created — plain data,
/// so Cancel is free and CreateNotebook is unit-testable. One draft = one notebook. In EDIT mode
/// (M9 Part 3) the draft is seeded from an existing notebook and rows carry their Source objects,
/// so ApplyNotebookCustomization can rename/keep them instead of rebuilding (content survives).</summary>
public sealed class NotebookDraft
{
    public string Name = "";
    public string Color = MainViewModel.NotebookColors[0].Hex;
    /// <summary>A CROPPED temp image path (CoverCropDialog output) — consumed then deleted. In edit
    /// mode this starts as the notebook's CURRENT CoverPath (unchanged = cover untouched;
    /// null = cover removed).</summary>
    public string? CoverSourcePath;
    public List<SectionDraft> Sections { get; } = new();

    public string? DefaultGridStyle;                 // null = inherit the global grid pref
    public string DefaultPageStyle = Editor.PageStyles.Freeform;
    public int DefaultPageStyleMode = Editor.PageStyles.ModeGuides;
    public string? DefaultFont;                      // null = the app default
    public double DefaultFontSize = 15;

    /// <summary>A fresh draft: one "Notes" section holding one "Untitled page".</summary>
    public static NotebookDraft New()
    {
        var d = new NotebookDraft();
        d.Sections.Add(new SectionDraft { Name = "Notes", PageTitles = { "Untitled page" } });
        return d;
    }

    /// <summary>Seed an EDIT draft from an existing notebook — every row remembers its Source.</summary>
    public static NotebookDraft FromNotebook(Models.Notebook nb)
    {
        var d = new NotebookDraft
        {
            Name = nb.Name,
            Color = nb.Color,
            CoverSourcePath = nb.CoverPath,
            DefaultGridStyle = nb.DefaultGridStyle,
            DefaultPageStyle = nb.DefaultPageStyle,
            DefaultPageStyleMode = nb.DefaultPageStyleMode,
            DefaultFont = nb.DefaultFont,
            DefaultFontSize = nb.DefaultFontSize,
        };
        foreach (var sec in nb.Sections)
        {
            var sd = new SectionDraft { Name = sec.Name, Source = sec };
            foreach (var pg in sec.Pages)
            {
                sd.PageTitles.Add(pg.Title);
                sd.PageSources.Add(pg);
            }
            d.Sections.Add(sd);
        }
        return d;
    }
}

/// <summary>One planned section: a name and its planned page titles (0+ allowed).</summary>
public sealed class SectionDraft
{
    public string Name = "";
    public List<string> PageTitles { get; } = new();

    /// <summary>Edit mode: the real section this row edits (null = a brand-new section).</summary>
    public Models.Section? Source;
    /// <summary>Edit mode: the real page behind each title row. Kept aligned with PageTitles by
    /// the helpers below; a missing/null entry means a brand-new page (New()'s collection
    /// initializer legitimately leaves this shorter).</summary>
    public List<Models.Page?> PageSources { get; } = new();

    public Models.Page? SourceAt(int i) => i >= 0 && i < PageSources.Count ? PageSources[i] : null;

    public void AddPage(string title)
    {
        Pad();
        PageTitles.Add(title);
        PageSources.Add(null);
    }

    public void RemovePageAt(int i)
    {
        Pad();
        PageTitles.RemoveAt(i);
        PageSources.RemoveAt(i);
    }

    private void Pad()
    {
        while (PageSources.Count < PageTitles.Count) PageSources.Add(null);
    }
}
