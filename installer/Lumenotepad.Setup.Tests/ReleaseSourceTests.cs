using Lumenotepad.Setup.Services;
using Xunit;

namespace Lumenotepad.Setup.Tests;

public class ReleaseSourceTests
{
    private const string GoodManifest = """
        {
          "version": "1.2.8",
          "notes": "Fixes.",
          "builds": {
            "macos-arm64": { "url": "https://example.test/mac.zip", "sha256": "aa", "size": 1 },
            "win-x64": {
              "url": "https://example.test/Lumenotepad-1.2.8-win-x64-portable.zip",
              "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "size": 79691776
            }
          }
        }
        """;

    [Fact]
    public void ParseManifest_readsTheWindowsBuild()
    {
        var release = ReleaseSource.ParseManifest(GoodManifest);
        Assert.NotNull(release);
        Assert.Equal("1.2.8", release.Version);
        Assert.EndsWith("win-x64-portable.zip", release.Client.Url);
        Assert.Equal(64, release.Client.Sha256.Length);
        Assert.Equal(79691776, release.Client.Size);
    }

    [Fact]
    public void ParseManifest_isCaseInsensitiveAboutKeys()
    {
        string shuffled = GoodManifest.Replace("\"version\"", "\"Version\"").Replace("\"builds\"", "\"Builds\"");
        Assert.NotNull(ReleaseSource.ParseManifest(shuffled));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "version": "1.0.0" }""")]
    [InlineData("""{ "version": "1.0.0", "builds": {} }""")]
    [InlineData("""{ "version": "1.0.0", "builds": { "win-x64": { "url": "https://x", "sha256": "short" } } }""")]
    [InlineData("""{ "version": "1.0.0", "builds": { "win-x64": { "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } } }""")]
    public void ParseManifest_refusesAnythingIncomplete(string json) =>
        Assert.Null(ReleaseSource.ParseManifest(json));

    [Fact]
    public void ParseManifest_lowercasesTheHash()
    {
        string upper = GoodManifest.Replace(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
        Assert.Equal(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ReleaseSource.ParseManifest(upper)!.Client.Sha256);
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", true)]
    [InlineData("1.2.9", "1.2.10", false)]
    [InlineData("1.2.8", "1.2.8", false)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("v1.3.0", "1.2.9", true)]
    [InlineData("1.3.0-beta", "1.2.9", true)]
    [InlineData("", "1.0.0", false)]
    [InlineData("1.0.0", "", true)]
    public void IsNewer_comparesNumbersNotText(string candidate, string installed, bool expected) =>
        Assert.Equal(expected, ReleaseSource.IsNewer(candidate, installed));

    [Theory]
    [InlineData("v1.2.8", "1.2.8")]
    [InlineData("1.2.8", "1.2.8")]
    [InlineData("  V2.0 ", "2.0")]
    public void NormaliseVersion_stripsTheLeadingV(string input, string expected) =>
        Assert.Equal(expected, ReleaseSource.NormaliseVersion(input));

    [Fact]
    public void HashMatches_ignoresCase()
    {
        Assert.True(ReleaseSource.HashMatches("ABCDEF", "abcdef"));
        Assert.False(ReleaseSource.HashMatches("abcdef", "abcde0"));
    }
}
