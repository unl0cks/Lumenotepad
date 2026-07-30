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

/// <summary>In-app updates for macOS and Windows.
///
/// On macOS the point of this is Gatekeeper, not convenience. Lumenotepad is not notarised, so ANY build macOS
/// sees arrive from a browser or a chat client is quarantined, and the first launch costs a trip through
/// System Settings → Privacy &amp; Security → "Open Anyway". That toll is per-download, so it used to be
/// payable on every single release.
///
/// The quarantine flag is applied by the DOWNLOADING application, not by the network. A build this app
/// fetches itself over HTTP is never marked, so it installs and relaunches with no Gatekeeper prompt at
/// all. Pay the toll once on the very first install and never again.
///
/// On Windows the reason is different but the shape is the same: the build is PORTABLE, keeping its
/// notebooks in a `userdata` folder beside the executable, so updating means replacing the program files
/// around that folder and leaving it alone. A running .exe cannot overwrite itself, so both platforms hand
/// the actual swap to a detached script that waits for this process to exit.</summary>
public static class UpdateService
{
    /// <summary>Where the update manifest lives. Anything that serves the JSON below over HTTPS works;
    /// GitHub Releases is convenient because "latest" is a stable URL that always points at the newest
    /// release. Overridable via the LUMENOTEPAD_UPDATE_URL environment variable for testing against a
    /// local file server without shipping a build.</summary>
    public static string ManifestUrl =>
        Environment.GetEnvironmentVariable("LUMENOTEPAD_UPDATE_URL") is { Length: > 0 } custom
            ? custom
            : "https://github.com/unl0cks/lumenotepad/releases/latest/download/latest.json";

    /// <summary>The human-readable release page, for the "what changed" link.</summary>
    public const string ReleasesPage = "https://github.com/unl0cks/lumenotepad/releases/latest";

    /// <summary>One downloadable build. <paramref name="Sha256"/> is verified before anything is
    /// unpacked — an interrupted or tampered download must never reach the bundle swap.</summary>
    public sealed record Build(string Url, string Sha256, long Size);

    public sealed record Manifest(string Version, string? Notes, Dictionary<string, Build> Builds);

    /// <summary>The build for THIS Mac, or null when the manifest has nothing newer (or nothing for this
    /// architecture). Never throws: a failed check is a non-event, not an error dialog.</summary>
    public static async Task<(string Version, string? Notes, Build Build)?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            if (!IsSupported) return null;
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

    /// <summary>Download, verify, and stage the new build, then hand the swap to a detached script and
    /// quit. Returns false if anything went wrong BEFORE the point of no return, in which case the
    /// running install is untouched.</summary>
    public static async Task<bool> DownloadAndApplyAsync(Build build, string version,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsSupported || InstallRoot is not { } target) return false;
        string work = Path.Combine(Path.GetTempPath(), "lumenotepad-update-" + version);
        try
        {
            Directory.Delete(work, true);
        }
        catch { /* first run, or nothing to clean */ }
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        if (!await DownloadAsync(build, zip, progress, ct)) return false;

        string unpacked = Path.Combine(work, "unpacked");
        Directory.CreateDirectory(unpacked);
        try { ZipFile.ExtractToDirectory(zip, unpacked); }
        catch { return false; }

        // Locate the payload root and prove it looks like a real build before it is allowed anywhere near
        // a working install: a truncated or wrong-platform archive must fail here, not halfway through.
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

    /// <summary>Whether this install can update itself in place. It must be a real packaged build sitting
    /// somewhere writable — a dev build out of bin/ would be clobbered by its own updater, and a copy in a
    /// read-only location would fail the swap halfway. Set LUMENOTEPAD_UPDATE_FORCE=1 to test against a
    /// dev tree deliberately.</summary>
    public static bool IsSupported
    {
        get
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return false;
            if (InstallRoot is not { } root) return false;
            if (Environment.GetEnvironmentVariable("LUMENOTEPAD_UPDATE_FORCE") == "1") return true;
            if (IsDevTree(root)) return false;
            // macOS replaces the whole bundle, so the writable thing is its PARENT; Windows writes files
            // inside the app folder itself.
            return CanWrite(OperatingSystem.IsMacOS() ? Directory.GetParent(root)?.FullName : root);
        }
    }

    /// <summary>What gets replaced: the .app bundle on macOS, the portable program folder on Windows.</summary>
    public static string? InstallRoot => OperatingSystem.IsMacOS()
        ? BundlePath
        : OperatingSystem.IsWindows() ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) : null;

    /// <summary>A build running straight out of bin/Debug or bin/Release is a checkout, not an install.</summary>
    private static bool IsDevTree(string path) =>
        path.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The /Applications/Lumenotepad.app the running process lives in, or null when this is not
    /// a bundled build. BaseDirectory is …/Lumenotepad.app/Contents/MacOS/.</summary>
    public static string? BundlePath
    {
        get
        {
            try
            {
                var macos = new DirectoryInfo(AppContext.BaseDirectory);
                var app = macos.Parent?.Parent;      // MacOS -> Contents -> Lumenotepad.app
                return app is not null && app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? app.FullName
                    : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Which build in the manifest is for this machine. Platform AND architecture, because a
    /// manifest carries every build and handing macOS a Windows zip would be worse than finding nothing.</summary>
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

    /// <summary>Find a named directory at the archive root, or anywhere below it.</summary>
    private static string? FindDir(string root, string name)
    {
        string direct = Path.Combine(root, name);
        if (Directory.Exists(direct)) return direct;
        foreach (var dir in Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories))
            return dir;
        return null;
    }

    /// <summary>Find the folder holding Lumenotepad.exe. The Windows zip wraps everything in one
    /// version-named folder so extracting never scatters 200 loose files, so this is normally one level in.</summary>
    private static string? FindExeDir(string root)
    {
        if (File.Exists(Path.Combine(root, "Lumenotepad.exe"))) return root;
        foreach (var exe in Directory.EnumerateFiles(root, "Lumenotepad.exe", SearchOption.AllDirectories))
            return Path.GetDirectoryName(exe);
        return null;
    }

    /// <summary>.NET's zip reader drops unix permissions, so a freshly extracted app host and its native
    /// libraries come out non-executable and the app would refuse to start. Put the bits back. Mode is
    /// not covered by a code signature, so this cannot invalidate the bundle's signing.</summary>
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
                try { File.SetUnixFileMode(file, exec); } catch { /* best effort */ }
        }
    }

    /// <summary>A process cannot reliably replace the bundle it is executing from, so the swap is done by
    /// a detached script that waits for us to exit first (the approach Sparkle uses). Nothing here is
    /// quarantined — we downloaded it ourselves — so the relaunch raises no Gatekeeper prompt.</summary>
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
        catch { /* handed to bash explicitly below, so the bit is a convenience */ }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script })
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        proc.Start();
    }

    /// <summary>Single-quote a path for the shell (paths can contain spaces — /Applications always could).</summary>
    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Windows equivalent. A running .exe cannot be overwritten, so wait for this process to exit
    /// first, then robocopy the new files OVER the program folder. Copy, not mirror, deliberately: the
    /// `userdata` folder is not in the archive, and not mirroring is exactly what leaves the user's
    /// notebooks in place.
    ///
    /// PowerShell and robocopy both ship with Windows, so this needs nothing installed.</summary>
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
