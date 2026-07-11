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
            var s = new AppSettings { Theme = "Lumen", FullTheme = true, CustomAccent = "#E27BA6", GlassTint = 0.4 };
            s.Save(dir);

            var loaded = AppSettings.Load(dir);

            Assert.Equal("Lumen", loaded.Theme);
            Assert.True(loaded.FullTheme);
            Assert.Equal("#E27BA6", loaded.CustomAccent);
            Assert.Equal(0.4, loaded.GlassTint, 3);
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

    [Fact]
    public void MotionPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().ReduceMotion);
            Assert.Equal("Normal", new AppSettings().MotionSpeed);
            var s = new AppSettings { ReduceMotion = true, MotionSpeed = "Snappy" };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.True(loaded.ReduceMotion);
            Assert.Equal("Snappy", loaded.MotionSpeed);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void BulletPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Empty(new AppSettings().BulletColors);
            Assert.Null(new AppSettings().NumBoldDefault);

            var s = new AppSettings { NumBoldDefault = true, NumStrikeDefault = false };
            s.BulletColors["star"] = "#FF0000";
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal("#FF0000", loaded.BulletColors["star"]);
            Assert.True(loaded.NumBoldDefault);
            Assert.False(loaded.NumStrikeDefault);
            Assert.Null(loaded.NumItalicDefault);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DisabledFonts_DefaultEmpty_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Empty(new AppSettings().DisabledFonts);
            var s = new AppSettings();
            s.DisabledFonts.Add("Impact");
            s.Save(dir);
            Assert.Equal(new[] { "Impact" }, AppSettings.Load(dir).DisabledFonts);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void BehaviorPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Equal("Home", d.LaunchTarget);
            Assert.Null(d.LastPageId);
            Assert.Equal(900, d.AutosaveMs);
            Assert.True(d.ConfirmDeleteNotebook);
            Assert.True(d.ConfirmDeleteSection);
            Assert.True(d.ConfirmDeletePage);
            Assert.True(d.ConfirmDeleteContainer);
            Assert.Equal(5, d.RecentCount);
            Assert.False(d.AlwaysOnTop);

            var s = new AppSettings
            {
                LaunchTarget = "LastPage", LastPageId = "p1", AutosaveMs = 2000,
                ConfirmDeletePage = false, RecentCount = 8, AlwaysOnTop = true,
            };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("LastPage", loaded.LaunchTarget);
            Assert.Equal("p1", loaded.LastPageId);
            Assert.Equal(2000, loaded.AutosaveMs);
            Assert.False(loaded.ConfirmDeletePage);
            Assert.True(loaded.ConfirmDeleteNotebook);
            Assert.Equal(8, loaded.RecentCount);
            Assert.True(loaded.AlwaysOnTop);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
