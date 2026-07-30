using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Services;

public static class UpdateService
{

    public static string ManifestUrl =>
        Environment.GetEnvironmentVariable("LUMENOTEPAD_UPDATE_URL") is { Length: > 0 } custom
            ? custom
            : "https://github.com/unl0cks/Lumenotepad/releases/latest/download/latest.json";

    public const string ReleasesPage = "https://github.com/unl0cks/Lumenotepad/releases/latest";

    public sealed record Build(string Url, string Sha256, long Size);

    public sealed record Manifest(string Version, string? Notes, Dictionary<string, Build> Builds);

    public static async Task<(string Version, string? Notes, Build Build)?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            if (!CanCheck) return null;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lumenotepad/{AppVersion.Current}");
            var json = await http.GetStringAsync(ManifestUrl, ct);
            var manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOpts);
            if (manifest?.Version is not { Length: > 0 } || manifest.Builds is null) return null;
            if (!AppVersion.IsNewerThanCurrent(manifest.Version)) return null;
            if (!manifest.Builds.TryGetValue(PlatformKey, out var build) || build.Url is not { Length: > 0 })
                return null;
            return (manifest.Version, manifest.Notes, build);
        }
        catch { return null; }
    }

    public static async Task<bool> DownloadAndApplyAsync(Build build, string version,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanApply || InstallRoot is not { } target) return false;
        string work = Path.Combine(Path.GetTempPath(), "lumenotepad-update-" + version);
        try
        {
            Directory.Delete(work, true);
        }
        catch {  }
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        if (!await DownloadAsync(build, zip, progress, ct)) return false;

        string unpacked = Path.Combine(work, "unpacked");
        Directory.CreateDirectory(unpacked);
        try { ZipFile.ExtractToDirectory(zip, unpacked); }
        catch { return false; }

        string? staged = OperatingSystem.IsMacOS() ? FindDir(unpacked, "Lumenotepad.app") : FindExeDir(unpacked);
        if (staged is null) return false;
        string exe = OperatingSystem.IsMacOS()
            ? Path.Combine(staged, "Contents", "MacOS", "Lumenotepad")
            : Path.Combine(staged, "Lumenotepad.exe");
        if (!File.Exists(exe) || new FileInfo(exe).Length == 0) return false;
        if (OperatingSystem.IsMacOS()) RestoreExecutableBits(staged);

        if (OperatingSystem.IsMacOS()) LaunchMacSwap(target, staged);
        else LaunchWindowsSwap(target, staged);
        return true;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static bool CanCheck => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public static bool CanApply
    {
        get
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return false;
            if (InstallRoot is not { } root) return false;
            if (Environment.GetEnvironmentVariable("LUMENOTEPAD_UPDATE_FORCE") == "1") return true;
            if (IsDevTree(root)) return false;

            return CanWrite(OperatingSystem.IsMacOS() ? Directory.GetParent(root)?.FullName : root);
        }
    }

    public static string? InstallRoot => OperatingSystem.IsMacOS()
        ? BundlePath
        : OperatingSystem.IsWindows() ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) : null;

    private static bool IsDevTree(string path) =>
        path.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase);

    public static string? BundlePath
    {
        get
        {
            try
            {
                var macos = new DirectoryInfo(AppContext.BaseDirectory);
                var app = macos.Parent?.Parent;
                return app is not null && app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? app.FullName
                    : null;
            }
            catch { return null; }
        }
    }

    public static string PlatformKey
    {
        get
        {
            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return OperatingSystem.IsMacOS() ? $"macos-{arch}" : $"win-{arch}";
        }
    }

    private static bool CanWrite(string? dir)
    {
        if (dir is null) return false;
        try
        {
            string probe = Path.Combine(dir, ".lumenotepad-write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> DownloadAsync(Build build, string dest, IProgress<double>? progress,
                                                  CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lumenotepad/{AppVersion.Current}");
            using var resp = await http.GetAsync(build.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? build.Size;

            await using (var net = await resp.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(dest))
            {
                var buffer = new byte[128 * 1024];
                long done = 0;
                int read;
                while ((read = await net.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            return VerifySha256(dest, build.Sha256);
        }
        catch { return false; }
    }

    private static bool VerifySha256(string path, string expected)
    {
        if (expected is not { Length: 64 }) return false;
        using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return actual == expected.Trim().ToLowerInvariant();
    }

    private static string? FindDir(string root, string name)
    {
        string direct = Path.Combine(root, name);
        if (Directory.Exists(direct)) return direct;
        foreach (var dir in Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories))
            return dir;
        return null;
    }

    private static string? FindExeDir(string root)
    {
        if (File.Exists(Path.Combine(root, "Lumenotepad.exe"))) return root;
        foreach (var exe in Directory.EnumerateFiles(root, "Lumenotepad.exe", SearchOption.AllDirectories))
            return Path.GetDirectoryName(exe);
        return null;
    }

    private static void RestoreExecutableBits(string bundle)
    {
        const UnixFileMode exec = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                  UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        string macos = Path.Combine(bundle, "Contents", "MacOS");
        if (!Directory.Exists(macos)) return;
        foreach (var file in Directory.EnumerateFiles(macos, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name == "Lumenotepad" || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                try { File.SetUnixFileMode(file, exec); } catch {  }
        }
    }

    private static void LaunchMacSwap(string currentBundle, string newBundle)
    {
        string script = Path.Combine(Path.GetTempPath(), "lumenotepad-swap-" + Guid.NewGuid().ToString("N") + ".sh");
        int pid = Environment.ProcessId;
        File.WriteAllText(script, $"""
            #!/bin/bash
            # Replace the installed Lumenotepad bundle once the running copy has exited.
            for _ in $(seq 1 100); do kill -0 {pid} 2>/dev/null || break; sleep 0.2; done
            TARGET={Quote(currentBundle)}
            NEW={Quote(newBundle)}
            OLD="$TARGET.old-$$"
            # Move the old bundle aside rather than deleting it, so a failed install can be put back.
            mv "$TARGET" "$OLD" 2>/dev/null || true
            if mv "$NEW" "$TARGET" 2>/dev/null; then
              rm -rf "$OLD"
            else
              mv "$OLD" "$TARGET" 2>/dev/null || true
            fi
            xattr -cr "$TARGET" 2>/dev/null || true
            open "$TARGET"
            rm -f {Quote(script)}
            """);
        try { File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch {  }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script })
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        proc.Start();
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static void LaunchWindowsSwap(string target, string staged)
    {
        string script = Path.Combine(Path.GetTempPath(), "lumenotepad-swap-" + Guid.NewGuid().ToString("N") + ".ps1");
        string exe = Path.Combine(target, "Lumenotepad.exe");
        int pid = Environment.ProcessId;
        File.WriteAllText(script, $"""
            # Replace the installed Lumenotepad files once the running copy has exited.
            Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue
            # /E copies all subdirectories. NO /MIR on purpose - anything already in the target and absent
            # from the new build (the userdata folder) has to survive.
            robocopy "{staged}" "{target}" /E /NFL /NDL /NJH /NJS /NP | Out-Null
            Start-Process -FilePath "{exe}" -WorkingDirectory "{target}"
            Remove-Item -LiteralPath "{script}" -Force -ErrorAction SilentlyContinue
            """);

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        proc.Start();
    }
}
