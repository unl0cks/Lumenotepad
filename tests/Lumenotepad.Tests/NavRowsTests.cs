using System.Collections.Generic;
using System.Linq;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class NavRowsTests
{
    private static Notebook Nb()
    {
        var nb = new Notebook { Name = "Bio" };
        var a = new Section { Name = "Notes" };
        a.Pages.Add(new Page { Title = "P1" });
        a.Pages.Add(new Page { Title = "P1a", Level = 1 });
        var b = new Section { Name = "Lab" };
        b.Pages.Add(new Page { Title = "L1" });
        nb.Sections.Add(a);
        nb.Sections.Add(b);
        return nb;
    }

    private static string Shape(List<object> rows) => string.Join(" ", rows.Select(r => r switch
    {
        SectionRow s => (s.Folded ? "+" : "-") + s.Section.Name,
        Page p => p.Title,
        _ => "?",
    }));

    [Fact]
    public void SectionsThenTheirPages() =>
        Assert.Equal("-Notes P1 P1a -Lab L1", Shape(NavRows.Build(Nb(), false, new HashSet<string>())));

    [Fact]
    public void FoldedSectionsHideTheirPages()
    {
        var nb = Nb();
        Assert.Equal("+Notes -Lab L1", Shape(NavRows.Build(nb, false, new HashSet<string> { nb.Sections[0].Id })));
    }

    [Fact]
    public void SingleModeListsPagesOnly() =>
        Assert.Equal("P1 P1a L1", Shape(NavRows.Build(Nb(), true, new HashSet<string>())));

    [Fact]
    public void BuildRefreshesThreadFlags_andEmptyIsEmpty()
    {
        var nb = Nb();
        NavRows.Build(nb, false, new HashSet<string>());
        Assert.True(nb.Sections[0].Pages[0].HasSubPages);
        Assert.Empty(NavRows.Build(null, false, new HashSet<string>()));
        Assert.Same(nb.Sections[1], NavRows.SectionOf(nb, nb.Sections[1].Pages[0]));
    }
}
