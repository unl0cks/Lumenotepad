using Lumenotepad.Setup.Services;
using Xunit;

namespace Lumenotepad.Setup.Tests;

public class PayloadTests
{
    [Fact]
    public void CommonRoot_findsTheSingleTopFolder()
    {
        var names = new[]
        {
            "Lumenotepad-1.2.8-win-x64/Lumenotepad.exe",
            "Lumenotepad-1.2.8-win-x64/av_libglesv2.dll",
            "Lumenotepad-1.2.8-win-x64/Assets/icon.png",
        };
        Assert.Equal("Lumenotepad-1.2.8-win-x64/", Payload.CommonRoot(names));
    }

    [Fact]
    public void CommonRoot_returnsNullWhenFilesSitAtTheRoot()
    {
        Assert.Null(Payload.CommonRoot(new[] { "Lumenotepad.exe", "folder/other.dll" }));
    }

    [Fact]
    public void CommonRoot_returnsNullWhenTopFoldersDiffer()
    {
        Assert.Null(Payload.CommonRoot(new[] { "a/one.txt", "b/two.txt" }));
    }

    [Fact]
    public void BareBuild_hasNoPayload() => Assert.False(Payload.Exists);
}
