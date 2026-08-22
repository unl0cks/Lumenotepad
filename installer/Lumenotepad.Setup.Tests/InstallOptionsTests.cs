using System;
using System.IO;
using Lumenotepad.Setup.Services;
using Xunit;

namespace Lumenotepad.Setup.Tests;

public class InstallOptionsTests
{
    [Fact]
    public void Default_installsPerUser()
    {
        Assert.Contains("Programs", InstallOptions.DefaultInstallDir);
        Assert.EndsWith("Lumenotepad", InstallOptions.DefaultInstallDir);
    }

    [Fact]
    public void Validate_acceptsTheDefault() =>
        Assert.Null(new InstallOptions().Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejectsNothingness(string dir) =>
        Assert.NotNull(new InstallOptions { InstallDir = dir }.Validate());

    [Fact]
    public void Validate_rejectsADriveRoot() =>
        Assert.NotNull(new InstallOptions { InstallDir = "C:\\" }.Validate());

    [Fact]
    public void Validate_rejectsTheWindowsFolder()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(win)) return;
        Assert.NotNull(new InstallOptions { InstallDir = Path.Combine(win, "Lumenotepad") }.Validate());
    }

    [Fact]
    public void Validate_rejectsAFullFolderThatIsNotOurs()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lumenotepad-setup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "somebody-elses.txt"), "");
        try
        {
            Assert.NotNull(new InstallOptions { InstallDir = dir }.Validate());
            File.WriteAllText(Path.Combine(dir, InstallEngine.ExeName), "");
            Assert.Null(new InstallOptions { InstallDir = dir }.Validate());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FromArguments_defaultsEverythingOff()
    {
        var o = InstallOptions.FromArguments(new[] { "--silent" });
        Assert.False(o.StartMenuShortcut);
        Assert.False(o.DesktopShortcut);
        Assert.False(o.LaunchWhenDone);
        Assert.Equal(InstallOptions.DefaultInstallDir, o.InstallDir);
    }

    [Fact]
    public void FromArguments_readsWhatWasAsked()
    {
        var o = InstallOptions.FromArguments(new[] { "--dir", "D:\\Apps\\Lumenotepad", "--startmenu", "--desktop" });
        Assert.Equal("D:\\Apps\\Lumenotepad", o.InstallDir);
        Assert.True(o.StartMenuShortcut);
        Assert.True(o.DesktopShortcut);
    }
}
