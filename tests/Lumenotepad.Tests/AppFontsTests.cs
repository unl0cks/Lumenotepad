using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class AppFontsTests
{
    [Fact]
    public void WithoutDisabled_DropsDisabled_CaseInsensitive()
    {
        var names = new[] { "Arial", "Georgia", "Impact" };
        var result = AppFonts.WithoutDisabled(names, new[] { "georgia", "IMPACT" }).ToList();
        Assert.Equal(new[] { "Arial" }, result);
    }

    [Fact]
    public void WithoutDisabled_NeverHidesBundledFaces()
    {
        var names = new[] { "Caveat", "Arial", "Yuyu" };
        var result = AppFonts.WithoutDisabled(names, new[] { "Caveat", "Yuyu", "Arial" }).ToList();
        Assert.Equal(new[] { "Caveat", "Yuyu" }, result);
    }

    [Fact]
    public void WithoutDisabled_NullOrEmpty_PassesThrough()
    {
        var names = new[] { "Arial", "Georgia" };
        Assert.Equal(names, AppFonts.WithoutDisabled(names, null).ToList());
        Assert.Equal(names, AppFonts.WithoutDisabled(names, System.Array.Empty<string>()).ToList());
    }
}
