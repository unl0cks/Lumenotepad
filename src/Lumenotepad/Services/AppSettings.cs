using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumenotepad.Services;

/// <summary>Portable app settings persisted as JSON in the beside-the-exe userdata folder.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Lumen";          // "Lumen" | "Dark" | "Light" | "Pink" | "Light blue"
    public bool FullTheme { get; set; }                     // canvas matches frame material when true
    public bool PaperLight { get; set; }                    // Lumen + FullTheme off: light paper instead of dark
    public bool FlatCovers { get; set; }                    // solid notebook covers (no gradient), shadow kept
    public bool GlossyAccents { get; set; } = true;         // top-lit gloss on chips + selected pills
    public bool ExtendedFonts { get; set; }                 // full installed-font list vs curated shortlist
    public string? CustomAccent { get; set; }               // accent override; null = theme's own
    public double GlassTint { get; set; }                   // -1..1: darken / lighten the glass; 0 = off
    public bool ReduceMotion { get; set; }                  // skip animations entirely
    public string MotionSpeed { get; set; } = "Normal";     // "Calm" | "Normal" | "Snappy"
    public Dictionary<string, string> BulletColors { get; set; } = new();  // bullet style → hex override
    public bool? NumBoldDefault { get; set; }               // numbered-list number style defaults;
    public bool? NumItalicDefault { get; set; }             // null = the number matches its line's text
    public bool? NumUnderlineDefault { get; set; }
    public bool? NumStrikeDefault { get; set; }
    public List<string> DisabledFonts { get; set; } = new();  // fonts hidden from the toolbar menu
    public string LaunchTarget { get; set; } = "Home";      // "Home" | "LastPage"
    public string? LastPageId { get; set; }                 // auto-tracked; not a pref, not reset
    public int AutosaveMs { get; set; } = 900;              // typing → save debounce (100..5000)
    public bool ConfirmDeleteNotebook { get; set; } = true; // per-kind "are you sure" prompts
    public bool ConfirmDeleteSection { get; set; } = true;
    public bool ConfirmDeletePage { get; set; } = true;
    public bool ConfirmDeleteContainer { get; set; } = true;
    public int RecentCount { get; set; } = 5;               // homepage "Jump back in" entries (0..10)
    public bool AlwaysOnTop { get; set; }
    public string? CaretColor { get; set; }                 // null = the theme accent
    public double CaretWidth { get; set; } = 1.6;           // 1..3 px
    public bool CaretBlink { get; set; } = true;
    public string DefaultHighlight { get; set; } = "#66FFD666";  // Ctrl+Shift+H color
    public string DateFormat { get; set; } = "yyyy-MM-dd";  // Ctrl+Shift+T insert format
    public double NewNoteWidth { get; set; } = 360;         // new container width (height is auto)
    public bool AccentFollowsNotebook { get; set; }         // inside a notebook, its color tints the UI
    public string UserName { get; set; } = "";              // greeting name; "" = plain greeting
    public bool ShowHomeStats { get; set; } = true;         // notebook/page counts under the greeting
    public string CardSize { get; set; } = "Medium";        // gallery covers: Small | Medium | Large
    public string? EditorFont { get; set; }                 // base note font; null = app default
    public double EditorFontSize { get; set; } = 15;        // base note size (11..24)
    public double LineSpacingScale { get; set; } = 1.0;     // 1..1.8 — lines within a paragraph
    public double ParagraphSpacingScale { get; set; } = 1.0;// 0.5..3 — the gap between paragraphs
    public double IndentScale { get; set; } = 1.0;          // 0.7..2 — bullet indent width
    public bool SmartLists { get; set; } = true;            // "1. "/"- "/"* " auto-start lists
    public List<string> HighlightPalette { get; set; } = new();  // empty = the built-in palette
    public List<string> TextPalette { get; set; } = new();       // empty = the built-in palette
    public string PageGrid { get; set; } = "None";          // canvas paper grid: None | Dots | Lines
    public bool GridSnap { get; set; }                      // move/resize lands on the 20px cell
    public string? BackupFolder { get; set; }               // null = auto-backup off
    public int BackupEveryDays { get; set; }                // 0 = off; else backup every N days on startup
    public int BackupKeep { get; set; } = 5;                // how many zips to retain
    public DateTime? LastBackupUtc { get; set; }            // bookkeeping (like LastPageId); not a pref, not reset
    public bool CloseToTray { get; set; }                   // closing hides to the tray instead of quitting
    public bool MinimizeToTray { get; set; }                // minimizing hides to the tray
    public bool SummonHotkey { get; set; }                  // global Ctrl+Alt+N brings the window forward
    public double PagesPanelWidth { get; set; } = 224;      // pages side panel width; drag-resizable
    public bool DoubleClickCreate { get; set; }              // require a double-click to start a note
    public bool RoundedPdfCorners { get; set; } = true;      // PDF pages render with gently rounded corners
    public string ToolbarPosition { get; set; } = "Top";    // "Top" | "Left" | "Right" | "Bottom"
    public string ToolbarScope { get; set; } = "Window";    // "Window" (window edge) | "Page" (inside the page box)
    public bool ResizablePages { get; set; } = true;        // note containers show resize handles
    public bool AlwaysShowBorders { get; set; }             // containers keep a visible edge, not hover-only
    public bool DeletedHistory { get; set; } = true;        // deleted containers kept per page, restorable
    public string MindmapTidyLayout { get; set; } = "Radial";  // Tidy arrangement: Radial | Hybrid | TopDown
    public bool StartRailVisible { get; set; } = true;      // notebooks rail shown at launch
    public bool StartPagesVisible { get; set; } = true;     // pages panel shown at launch
    public bool AdvancedUnlocked { get; set; }              // advanced prefs gate accepted
    public double CornerRoundness { get; set; } = 1.0;      // 0.5..1.5 — scales the UI corner radii
    public Dictionary<string, string> KeyOverrides { get; set; } = new();  // action → custom shortcut
    public bool SectionsSidebar { get; set; }               // sections as their own left sidebar
    public bool SingleMode { get; set; }                    // notebooks hold pages directly (no sections)

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

    /// <summary>Where all user data lives (settings, notebooks, fonts, backups source). On Windows this is
    /// the PORTABLE userdata folder beside the app's assemblies — AppContext.BaseDirectory (the app's own
    /// folder) rather than ProcessPath, which points at dotnet.exe under `dotnet App.dll`. On macOS the app
    /// ships as a .app bundle that gets REPLACED wholesale on every update, so data must live outside it:
    /// SpecialFolder.ApplicationData resolves to ~/Library/Application Support on .NET 8+ (and to
    /// ~/.config on Linux), giving the platform-correct home in one line.</summary>
    public static string DefaultDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(System.AppContext.BaseDirectory, "userdata");
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                // GetFolderPath yields "" when HOME is unset (some launch contexts); resolving to a
                // RELATIVE "Lumenotepad" would scatter the user's notebooks into the working
                // directory, so rebuild the platform path explicitly instead.
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
