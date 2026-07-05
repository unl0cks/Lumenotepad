using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("My Notebook!", "my-notebook")]
    [InlineData("  Spaces  ", "spaces")]
    [InlineData("Work / Cases", "work-cases")]
    [InlineData("!!!", "notebook")]
    [InlineData("", "notebook")]
    public void Make_producesExpectedSlug(string input, string expected)
        => Assert.Equal(expected, Slug.Make(input));

    [Fact]
    public void Unique_appendsSuffixOnCollision()
    {
        var existing = new[] { "notes", "notes-2" };
        Assert.Equal("notes-3", Slug.Unique("Notes", existing));
        Assert.Equal("fresh", Slug.Unique("Fresh", existing));
    }
}
