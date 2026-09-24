using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class TypingRulesTests
{
    private static readonly RunFormat Plain = default;

    [Fact]
    public void KeptFont_fillsAnEmptyLine_only()
    {
        Assert.Equal("Caveat", TypingRules.Resolve(Plain, true, true, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, false, true, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, true, false, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, true, true, null).Font);
    }

    [Fact]
    public void KeptFont_neverOverridesAFontAlreadyThere_butKeepsTheRestOfTheMark()
    {
        Assert.Equal("Yuyu", TypingRules.Resolve(Plain with { Font = "Yuyu" }, true, true, "Caveat").Font);
        var r = TypingRules.Resolve(Plain with { Size = 18, Bold = true }, true, true, "Caveat");
        Assert.Equal("Caveat", r.Font);
        Assert.Equal(18, r.Size);
        Assert.True(r.Bold);
    }

    [Theory]
    [InlineData("Hello. ", true)]
    [InlineData("Wow!  ", true)]
    [InlineData("Really? ", true)]
    [InlineData("He said \"stop.\" ", true)]
    [InlineData("(done.) ", true)]
    [InlineData("chapter 3. ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Hello.", false)]
    [InlineData("Hello ", false)]
    [InlineData("see e.g. ", false)]
    [InlineData("i.e. ", false)]
    [InlineData("apples, etc. ", false)]
    [InlineData("cats vs. ", false)]
    [InlineData("Dr. ", false)]
    [InlineData("J. ", false)]
    [InlineData("wait... ", false)]
    [InlineData(". ", false)]
    public void StartsSentence(string before, bool expected) =>
        Assert.Equal(expected, TypingRules.StartsSentence(before));
}
