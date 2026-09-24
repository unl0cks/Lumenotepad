using System.Collections.ObjectModel;
using System.Linq;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class PageTreeTests
{
    private static ObservableCollection<Page> List(params (string T, int L)[] items) =>
        new(items.Select(i => new Page { Title = i.T, Level = i.L }));

    private static string Shape(ObservableCollection<Page> p) => string.Join(" ", p.Select(x => x.Title + x.Level));

    [Fact]
    public void SubtreeEnd_coversDescendantsOnly()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("a3", 1), ("B", 0));
        Assert.Equal(4, PageTree.SubtreeEnd(p, 0));
        Assert.Equal(3, PageTree.SubtreeEnd(p, 1));
        Assert.Equal(5, PageTree.SubtreeEnd(p, 4));
    }

    [Fact]
    public void InsertSubPage_goesAfterExistingChildren_andUnfoldsTheParent()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0));
        p[0].Collapsed = true;
        int at = PageTree.InsertSubPage(p, 0, new Page { Title = "n" });
        Assert.Equal(2, at);
        Assert.Equal("A0 a11 n1 B0", Shape(p));
        Assert.False(p[0].Collapsed);
    }

    [Fact]
    public void InsertAfter_skipsTheSubtree_atTheSameLevel()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0));
        PageTree.InsertAfter(p, 0, new Page { Title = "n" });
        Assert.Equal("A0 a11 n0 B0", Shape(p));
    }

    [Fact]
    public void Indent_needsAPageAbove_andCarriesDescendants()
    {
        var p = List(("A", 0), ("B", 0), ("b1", 1), ("C", 0));
        Assert.False(PageTree.CanIndent(p, 0));
        PageTree.Indent(p, 1);
        Assert.Equal("A0 B1 b12 C0", Shape(p));
        Assert.False(PageTree.CanIndent(p, 1));
        Assert.False(PageTree.CanIndent(p, 2));
    }

    [Fact]
    public void Indent_refusesToPassTheMaximumDepth()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("a3", 2));
        Assert.False(PageTree.CanIndent(p, 3));
        Assert.False(PageTree.CanAddSubPage(p, 2));
        Assert.True(PageTree.CanAddSubPage(p, 1));
    }

    [Fact]
    public void Outdent_carriesDescendants()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0));
        Assert.False(PageTree.CanOutdent(p, 0));
        PageTree.Outdent(p, 1);
        Assert.Equal("A0 a10 a21 B0", Shape(p));
    }

    [Fact]
    public void RemovePromoting_movesChildrenUpALevel()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0));
        PageTree.RemovePromoting(p, 0);
        Assert.Equal("a10 a21 B0", Shape(p));
    }

    [Fact]
    public void Refresh_marksParentsAndFoldedPages()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0), ("b1", 1));
        p[0].Collapsed = true;
        PageTree.Refresh(p);
        Assert.True(p[0].HasSubPages);
        Assert.True(p[1].HasSubPages);
        Assert.False(p[2].HasSubPages);
        Assert.True(p[1].IsFoldedAway);
        Assert.True(p[2].IsFoldedAway);
        Assert.False(p[3].IsFoldedAway);
        Assert.False(p[4].IsFoldedAway);
        Assert.Same(p[0], PageTree.VisibleAncestor(p, p[2]));
        Assert.Same(p[4], PageTree.VisibleAncestor(p, p[4]));
    }

    [Fact]
    public void MoveGroup_movesAPageWithItsSubPages_keepingTheSameObjects()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0), ("C", 0), ("c1", 1));
        var a1 = p[1];
        PageTree.MoveGroup(p, 0, 2);
        Assert.Equal("B0 C0 c11 A0 a11", Shape(p));
        Assert.Same(a1, p[4]);
        PageTree.MoveGroup(p, 2, 0);
        Assert.Equal("A0 a11 B0 C0 c11", Shape(p));
        PageTree.MoveGroup(p, 0, 1);
        Assert.Equal("B0 A0 a11 C0 c11", Shape(p));
        Assert.Equal(3, PageTree.Groups(p).Count);
    }

    [Fact]
    public void Normalize_repairsImpossibleLevels()
    {
        var p = List(("A", 2), ("B", 3), ("C", -1), ("D", 2));
        PageTree.Normalize(p);
        Assert.Equal("A0 B1 C0 D1", Shape(p));
    }
}
