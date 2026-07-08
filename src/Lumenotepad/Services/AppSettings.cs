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
    public string AccentColor { get; set; } = "#4DA6FF";
    public double BlurStrength { get; set; } = 0.6;         // 0..1
    public string ToolbarPosition { get; set; } = "Top";    // "Top" | "Left" | "Right" | "Bottom"
    public string ToolbarScope { get; set; } = "Window";    // "Window" (window edge) | "Page" (inside the page box)
    public bool ResizablePages { get; set; } = true;        // note containers show resize handles
    public bool DeletedHistory { get; set; } = true;        // deleted containers kept per page, restorable

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
