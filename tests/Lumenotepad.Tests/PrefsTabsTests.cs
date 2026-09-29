using System.Linq;
using Lumenotepad.Views;
using Xunit;

namespace Lumenotepad.Tests;

public class PrefsTabsTests
{
    [Fact]
    public void EveryHeadingBelongsToExactlyOneTabOfItsPage()
    {
        foreach (var page in PrefsTabs.Pages)
        {
            var tabs = PrefsTabs.For(page)!;
            var headings = tabs.SelectMany(t => t.Headings).ToList();
            Assert.Equal(headings.Count, headings.Distinct().Count());
            Assert.Equal(tabs.Count, tabs.Select(t => t.Name).Distinct().Count());
            Assert.All(tabs, t => Assert.NotEmpty(t.Headings));
        }
    }

    [Fact]
    public void TabOf_findsTheTab_andIgnoresCase()
    {
        Assert.Equal("Text", PrefsTabs.TabOf("editor", "TEXT DEFAULTS"));
        Assert.Equal("Home screen", PrefsTabs.TabOf("general", "Gallery"));
        Assert.Null(PrefsTabs.TabOf("editor", "NOPE"));
        Assert.Null(PrefsTabs.For("shortcuts"));
    }
}
