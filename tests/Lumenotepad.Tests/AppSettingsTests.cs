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
            var s = new AppSettings { Theme = "Lumen", FullTheme = true, AccentColor = "#4DA6FF", BlurStrength = 0.7 };
            s.Save(dir);

            var loaded = AppSettings.Load(dir);

            Assert.Equal("Lumen", loaded.Theme);
            Assert.True(loaded.FullTheme);
            Assert.Equal("#4DA6FF", loaded.AccentColor);
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
}
