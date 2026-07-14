using System.Collections.Generic;

namespace Lumenotepad.ViewModels;

/// <summary>Everything the notebook wizard collects before anything real is created — plain data,
/// so Cancel is free and CreateNotebook is unit-testable. One draft = one notebook.</summary>
public sealed class NotebookDraft
{
    public string Name = "";
    public string Color = MainViewModel.NotebookColors[0].Hex;
    /// <summary>A CROPPED temp image path (CoverCropDialog output) — consumed then deleted.</summary>
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
}

/// <summary>One planned section: a name and its planned page titles (0+ allowed).</summary>
public sealed class SectionDraft
{
    public string Name = "";
    public List<string> PageTitles { get; } = new();
}
