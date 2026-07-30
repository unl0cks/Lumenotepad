using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Lumenotepad.Services;

/// <summary>Identity and build facts for the About page, and the one block of text worth pasting into a
/// bug report.</summary>
public static class AppInfo
{
    public const string Name = "Lumenotepad";
    public const string Tagline = "A freeform note organizer — pages you drop containers onto, anywhere you like.";

    public static string Version => AppVersion.Current;

    /// <summary>A human date-stamp for the build, so two people on "1.2.0" can tell whose is older.</summary>
    public static string Build { get; } = ReadBuild();

    /// <summary>Short commit sha when the build came from a git checkout; null otherwise. A full sha is
    /// noise in an About box.</summary>
    public static string? Commit { get; } = ReadCommit();

    /// <summary>Which packaging this is running as — it changes what updating even means.</summary>
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
        catch { /* single-file or restricted environment */ }
        return "local";
    }

    private static string? ReadCommit()
    {
        // The SDK writes "<version>+<sha>" into InformationalVersion inside a git repo.
        string? meta = Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        int plus = meta?.IndexOf('+') ?? -1;
        if (meta is null || plus < 0 || plus + 1 >= meta.Length) return null;
        string sha = meta[(plus + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }

    /// <summary>Version, packaging, runtime and OS as one pasteable block — the useful part of a bug
    /// report, so nobody has to be walked through finding it.</summary>
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
