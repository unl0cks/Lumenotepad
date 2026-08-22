using Lumenotepad.Setup.Services;
using Xunit;

namespace Lumenotepad.Setup.Tests;

public class InstalledVersionTests
{
    [Theory]
    [InlineData("1.2.8", null, "1.2.8")]
    [InlineData("1.2.8", "", "1.2.8")]
    [InlineData("1.2.8", "1.2.9", "1.2.9")]
    public void InstalledVersion_prefersWhatWasDownloaded(string carried, string? downloaded, string expected) =>
        Assert.Equal(expected, InstallEngine.InstalledVersion(carried, downloaded));

    [Theory]
    [InlineData("1.2.8+abc123", "1.2.8")]
    [InlineData("  1.2.8  ", "1.2.8")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeVersion_dropsBuildMetadata(string? raw, string? expected) =>
        Assert.Equal(expected, InstallEngine.NormalizeVersion(raw));

    [Theory]
    [InlineData("1.2.9", "1.2.8", "1.2.9")]
    [InlineData(null, "1.2.8", "1.2.8")]
    [InlineData("", "1.2.8", "1.2.8")]
    [InlineData("1.2.9", null, "1.2.9")]
    public void PreferBinaryVersion_trustsTheFileOverTheRecord(string? binary, string? recorded, string? expected) =>
        Assert.Equal(expected, InstallEngine.PreferBinaryVersion(binary, recorded));
}
