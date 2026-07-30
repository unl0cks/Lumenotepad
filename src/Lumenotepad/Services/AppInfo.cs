using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Lumenotepad.Services;

public static class AppInfo
{
    public const string Name = "Lumenotepad";
    public const string Tagline = "A freeform note organizer. Drop containers anywhere on the page.";

    public static string Version => AppVersion.Current;

    public static string Build { get; } = ReadBuild();

    public static string? Commit { get; } = ReadCommit();

    public static string Packaging => OperatingSystem.IsMacOS()
        ? (UpdateService.BundlePath is null ? "unbundled build" : "app bundle")
        : OperatingSystem.IsWindows() ? "portable" : "unpackaged";

    private static Assembly Asm => typeof(AppInfo).Assembly;

    private static string ReadBuild()
    {
        try
        {
            string path = Asm.Location;
            if (string.IsNullOrEmpty(path)) path = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (path.Length > 0 && File.Exists(path))
                return File.GetLastWriteTime(path).ToString("yyyy.MM.dd.HHmm");
        }
        catch {  }
        return "local";
    }

    private static string? ReadCommit()
    {

        string? meta = Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        int plus = meta?.IndexOf('+') ?? -1;
        if (meta is null || plus < 0 || plus + 1 >= meta.Length) return null;
        string sha = meta[(plus + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }

    public static string Details() =>
        $"""
        {Name} {Version}
        Build:     {Build}{(Commit is { } c ? $" ({c})" : "")}
        Packaging: {Packaging}
        Runtime:   .NET {Environment.Version}
        OS:        {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})
        Data:      {AppSettings.DefaultDir}
        """;
}
