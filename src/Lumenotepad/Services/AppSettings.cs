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
    public string ToolbarPosition { get; set; } = "Top";    // "Top" | "Left" | "Right" | "Bottom"
    public string ToolbarScope { get; set; } = "Window";    // "Window" (window edge) | "Page" (inside the page box)
    public bool ResizablePages { get; set; } = true;        // note containers show resize handles
    public bool DeletedHistory { get; set; } = true;        // deleted containers kept per page, restorable
    public bool StartRailVisible { get; set; } = true;      // notebooks rail shown at launch
    public bool StartPagesVisible { get; set; } = true;     // pages panel shown at launch
    public bool AdvancedUnlocked { get; set; }              // advanced prefs gate accepted

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

    /// <summary>The portable userdata folder beside the app's assemblies. Uses AppContext.BaseDirectory
    /// (the app's own folder) rather than ProcessPath, which points at dotnet.exe when launched via
    /// `dotnet App.dll` and would try to write into a protected install folder.</summary>
    public static string DefaultDir => Path.Combine(System.AppContext.BaseDirectory, "userdata");
}
