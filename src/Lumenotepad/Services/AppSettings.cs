using System.IO;
using System.Text.Json;

namespace Lumenotepad.Services;

/// <summary>Portable app settings persisted as JSON in the beside-the-exe userdata folder.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Lumen";          // "Light" | "Dark" | "Lumen"
    public bool FullTheme { get; set; }                     // canvas matches frame material when true
    public string AccentColor { get; set; } = "#4DA6FF";
    public double BlurStrength { get; set; } = 0.6;         // 0..1

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

    /// <summary>The portable userdata folder beside the running executable.</summary>
    public static string DefaultDir =>
        Path.Combine(Path.GetDirectoryName(System.Environment.ProcessPath) ?? ".", "userdata");
}
