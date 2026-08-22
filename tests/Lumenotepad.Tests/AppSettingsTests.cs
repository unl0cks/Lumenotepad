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
    public void PagesPanelWidth_defaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal(224, new AppSettings().PagesPanelWidth);
            new AppSettings { PagesPanelWidth = 300 }.Save(dir);
            Assert.Equal(300, AppSettings.Load(dir).PagesPanelWidth, 3);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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

    [Fact]
    public void PersonalTouchPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Null(d.CaretColor);
            Assert.Equal(1.6, d.CaretWidth, 3);
            Assert.True(d.CaretBlink);
            Assert.Equal("#66FFD666", d.DefaultHighlight);
            Assert.Equal("yyyy-MM-dd", d.DateFormat);
            Assert.Equal(360, d.NewNoteWidth, 3);
            Assert.False(d.AccentFollowsNotebook);
            Assert.Equal("", d.UserName);
            Assert.True(d.ShowHomeStats);
            Assert.Equal("Medium", d.CardSize);

            var s = new AppSettings
            {
                CaretColor = "#FF0000", CaretWidth = 2.5, CaretBlink = false,
                DefaultHighlight = "#6699E28A", DateFormat = "HH:mm", NewNoteWidth = 480,
                AccentFollowsNotebook = true, UserName = "Sam", ShowHomeStats = false,
                CardSize = "Large",
            };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("#FF0000", loaded.CaretColor);
            Assert.Equal(2.5, loaded.CaretWidth, 3);
            Assert.False(loaded.CaretBlink);
            Assert.Equal("#6699E28A", loaded.DefaultHighlight);
            Assert.Equal("HH:mm", loaded.DateFormat);
            Assert.Equal(480, loaded.NewNoteWidth, 3);
            Assert.True(loaded.AccentFollowsNotebook);
            Assert.Equal("Sam", loaded.UserName);
            Assert.False(loaded.ShowHomeStats);
            Assert.Equal("Large", loaded.CardSize);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void EditorDefaultPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Null(d.EditorFont);
            Assert.Equal(15, d.EditorFontSize, 3);
            Assert.Equal(1.0, d.LineSpacingScale, 3);
            Assert.Equal(1.0, d.ParagraphSpacingScale, 3);
            Assert.Equal(1.0, d.IndentScale, 3);
            Assert.True(d.SmartLists);
            Assert.Empty(d.HighlightPalette);
            Assert.Empty(d.TextPalette);

            var s = new AppSettings
            {
                EditorFont = "Caveat", EditorFontSize = 18, LineSpacingScale = 1.4,
                ParagraphSpacingScale = 2.0, IndentScale = 1.5, SmartLists = false,
            };
            s.HighlightPalette.Add("#66FF0000");
            s.TextPalette.Add("#00FF00");
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("Caveat", loaded.EditorFont);
            Assert.Equal(18, loaded.EditorFontSize, 3);
            Assert.Equal(1.4, loaded.LineSpacingScale, 3);
            Assert.Equal(2.0, loaded.ParagraphSpacingScale, 3);
            Assert.Equal(1.5, loaded.IndentScale, 3);
            Assert.False(loaded.SmartLists);
            Assert.Equal(new[] { "#66FF0000" }, loaded.HighlightPalette);
            Assert.Equal(new[] { "#00FF00" }, loaded.TextPalette);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PaperGridPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal("None", new AppSettings().PageGrid);
            Assert.False(new AppSettings().GridSnap);

            var s = new AppSettings { PageGrid = "Dots", GridSnap = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal("Dots", loaded.PageGrid);
            Assert.True(loaded.GridSnap);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void BackupSettings_defaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Null(new AppSettings().BackupFolder);
            Assert.Equal(0, new AppSettings().BackupEveryDays);
            Assert.Equal(5, new AppSettings().BackupKeep);
            Assert.Null(new AppSettings().LastBackupUtc);

            var when = new System.DateTime(2026, 7, 1, 8, 0, 0, System.DateTimeKind.Utc);
            var s = new AppSettings { BackupFolder = @"C:\bk", BackupEveryDays = 7, BackupKeep = 3, LastBackupUtc = when };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal(@"C:\bk", loaded.BackupFolder);
            Assert.Equal(7, loaded.BackupEveryDays);
            Assert.Equal(3, loaded.BackupKeep);
            Assert.Equal(when, loaded.LastBackupUtc);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TraySettings_defaultsFalse_andRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().CloseToTray);
            Assert.False(new AppSettings().MinimizeToTray);
            Assert.False(new AppSettings().SummonHotkey);

            var s = new AppSettings { CloseToTray = true, MinimizeToTray = true, SummonHotkey = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.True(loaded.CloseToTray);
            Assert.True(loaded.MinimizeToTray);
            Assert.True(loaded.SummonHotkey);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PaperAndInkToggles_defaultFalse_andRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.False(d.PaperDark);
            Assert.False(d.WhiteAccentText);
            Assert.False(d.NeutralDark);

            var s = new AppSettings { PaperDark = true, WhiteAccentText = true, NeutralDark = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.True(loaded.PaperDark);
            Assert.True(loaded.WhiteAccentText);
            Assert.True(loaded.NeutralDark);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DoubleClickCreate_defaultsFalse_andRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().DoubleClickCreate);

            var s = new AppSettings { DoubleClickCreate = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.True(loaded.DoubleClickCreate);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
