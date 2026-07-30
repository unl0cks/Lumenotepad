using System.Collections.Generic;

namespace Lumenotepad.ViewModels;

public sealed class NotebookDraft
{
    public string Name = "";
    public string Color = MainViewModel.NotebookColors[0].Hex;

    public string? CoverSourcePath;
    public List<SectionDraft> Sections { get; } = new();

    public string? DefaultGridStyle;
    public string DefaultPageStyle = Editor.PageStyles.Freeform;
    public int DefaultPageStyleMode = Editor.PageStyles.ModeGuides;
    public string? DefaultFont;
    public double DefaultFontSize = 15;

    public static NotebookDraft New()
    {
        var d = new NotebookDraft();
        d.Sections.Add(new SectionDraft { Name = "Notes", PageTitles = { "Untitled page" } });
        return d;
    }

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

public sealed class SectionDraft
{
    public string Name = "";
    public List<string> PageTitles { get; } = new();

    public Models.Section? Source;

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
