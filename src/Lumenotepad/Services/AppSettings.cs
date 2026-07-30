using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumenotepad.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Lumen";
    public bool FullTheme { get; set; }
    public bool PaperLight { get; set; }
    public bool FlatCovers { get; set; }
    public bool GlossyAccents { get; set; } = true;
    public bool ExtendedFonts { get; set; }
    public string? CustomAccent { get; set; }
    public double GlassTint { get; set; }
    public int MacGlassMaterial { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public string MotionSpeed { get; set; } = "Normal";
    public Dictionary<string, string> BulletColors { get; set; } = new();
    public bool? NumBoldDefault { get; set; }
    public bool? NumItalicDefault { get; set; }
    public bool? NumUnderlineDefault { get; set; }
    public bool? NumStrikeDefault { get; set; }
    public List<string> DisabledFonts { get; set; } = new();
    public string LaunchTarget { get; set; } = "Home";
    public string? LastPageId { get; set; }
    public int AutosaveMs { get; set; } = 900;
    public bool ConfirmDeleteNotebook { get; set; } = true;
    public bool ConfirmDeleteSection { get; set; } = true;
    public bool ConfirmDeletePage { get; set; } = true;
    public bool ConfirmDeleteContainer { get; set; } = true;
    public int RecentCount { get; set; } = 5;
    public bool AlwaysOnTop { get; set; }
    public string? CaretColor { get; set; }
    public double CaretWidth { get; set; } = 1.6;
    public bool CaretBlink { get; set; } = true;
    public string DefaultHighlight { get; set; } = "#66FFD666";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public double NewNoteWidth { get; set; } = 360;
    public bool AccentFollowsNotebook { get; set; }
    public string UserName { get; set; } = "";
    public bool ShowHomeStats { get; set; } = true;
    public string CardSize { get; set; } = "Medium";
    public string? EditorFont { get; set; }
    public double EditorFontSize { get; set; } = 15;
    public double LineSpacingScale { get; set; } = 1.0;
    public double ParagraphSpacingScale { get; set; } = 1.0;
    public double IndentScale { get; set; } = 1.0;
    public bool SmartLists { get; set; } = true;
    public List<string> HighlightPalette { get; set; } = new();
    public List<string> TextPalette { get; set; } = new();
    public string PageGrid { get; set; } = "None";
    public bool GridSnap { get; set; }
    public string? BackupFolder { get; set; }
    public int BackupEveryDays { get; set; }
    public int BackupKeep { get; set; } = 5;
    public DateTime? LastBackupUtc { get; set; }
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool SummonHotkey { get; set; }
    public double PagesPanelWidth { get; set; } = 224;
    public bool DoubleClickCreate { get; set; }
    public bool RoundedPdfCorners { get; set; } = true;
    public string ToolbarPosition { get; set; } = "Top";
    public string ToolbarScope { get; set; } = "Window";
    public bool ResizablePages { get; set; } = true;
    public bool AlwaysShowBorders { get; set; }
    public bool DeletedHistory { get; set; } = true;
    public string MindmapTidyLayout { get; set; } = "Radial";
    public bool StartRailVisible { get; set; } = true;
    public bool StartPagesVisible { get; set; } = true;
    public bool AdvancedUnlocked { get; set; }
    public double CornerRoundness { get; set; } = 1.0;
    public Dictionary<string, string> KeyOverrides { get; set; } = new();
    public bool SectionsSidebar { get; set; }
    public bool SingleMode { get; set; }

    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string userDataDir)
    {
        Directory.CreateDirectory(userDataDir);
        File.WriteAllText(Path.Combine(userDataDir, FileName), JsonSerializer.Serialize(this, Options));
    }

    public static AppSettings Load(string userDataDir)
    {
        var path = Path.Combine(userDataDir, FileName);
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public static string DefaultDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(System.AppContext.BaseDirectory, "userdata");
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
            {

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? ".";
                root = OperatingSystem.IsMacOS()
                    ? Path.Combine(home, "Library", "Application Support")
                    : Path.Combine(home, ".config");
            }
            return Path.Combine(root, "Lumenotepad");
        }
    }
}
