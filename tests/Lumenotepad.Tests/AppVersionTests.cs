using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.1", "1.2.0")]
    [InlineData("1.3.0", "1.2.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.2.0.1", "1.2.0")]
    [InlineData("1.10.0", "1.9.0")]
    public void NewerVersion_SortsAbove(string newer, string older)
    {
        Assert.True(AppVersion.Compare(newer, older) > 0);
        Assert.True(AppVersion.Compare(older, newer) < 0);
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0", "1.2.0.0")]
    public void EquivalentVersions_CompareEqual(string a, string b) =>
        Assert.Equal(0, AppVersion.Compare(a, b));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.2.x")]
    [InlineData("garbage.garbage")]
    public void UnparseableManifestVersion_IsNeverAnUpgrade(string junk)
    {

        Assert.False(AppVersion.Compare(junk, "1.2.0") > 0);
    }

    [Fact]
    public void CurrentVersion_IsAReadableDottedNumber()
    {
        Assert.Matches(@"^\d+\.\d+", AppVersion.Current);

        Assert.DoesNotContain("+", AppVersion.Current);
    }

    [Fact]
    public void CurrentVersion_IsNotNewerThanItself() =>
        Assert.False(AppVersion.IsNewerThanCurrent(AppVersion.Current));
}
