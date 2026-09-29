using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Models;

public sealed record SectionRow(Section Section, bool Folded)
{
    public double IndentWidth => 0;
    public bool IsFoldedAway => false;
}

public static class NavRows
{
    public static List<object> Build(Notebook? nb, bool singleMode, ISet<string> foldedSectionIds)
    {
        var rows = new List<object>();
        if (nb is null) return rows;
        foreach (var s in nb.Sections)
        {
            PageTree.Refresh(s.Pages);
            bool folded = !singleMode && foldedSectionIds.Contains(s.Id);
            if (!singleMode) rows.Add(new SectionRow(s, folded));
            if (!folded) rows.AddRange(s.Pages);
        }
        return rows;
    }

    public static Section? SectionOf(Notebook nb, Page page) => nb.Sections.FirstOrDefault(s => s.Pages.Contains(page));
}
