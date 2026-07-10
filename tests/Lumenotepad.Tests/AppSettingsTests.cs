using System.IO;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class AppSettingsTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var s = new AppSettings { Theme = "Lumen", FullTheme = true, CustomAccent = "#E27BA6", BlurStrength = 0.7 };
            s.Save(dir);

            var loaded = AppSettings.Load(dir);

            Assert.Equal("Lumen", loaded.Theme);
            Assert.True(loaded.FullTheme);
            Assert.Equal("#E27BA6", loaded.CustomAccent);
            Assert.Equal(0.7, loaded.BlurStrength, 3);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-missing-" + Path.GetRandomFileName());
        var loaded = AppSettings.Load(dir);
        Assert.Equal("Lumen", loaded.Theme);
        Assert.False(loaded.FullTheme);
    }

    [Fact]
    public void StartVisible_DefaultsTrue_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.True(new AppSettings().StartRailVisible);
            Assert.True(new AppSettings().StartPagesVisible);

            var s = new AppSettings { StartRailVisible = false, StartPagesVisible = false };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.False(loaded.StartRailVisible);
            Assert.False(loaded.StartPagesVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void AdvancedUnlocked_DefaultsFalse_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().AdvancedUnlocked);
            var s = new AppSettings { AdvancedUnlocked = true };
            s.Save(dir);
            Assert.True(AppSettings.Load(dir).AdvancedUnlocked);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
