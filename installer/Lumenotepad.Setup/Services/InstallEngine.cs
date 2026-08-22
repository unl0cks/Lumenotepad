using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Lumenotepad.Setup.Services;

public static class InstallEngine
{
    public const string AppName = "Lumenotepad";
    public const string ExeName = "Lumenotepad.exe";
    public const string ProcessName = "Lumenotepad";
    public const string UninstallerName = "uninstall.exe";
    public const string UserDataDirName = "userdata";
    private const string ArpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Lumenotepad";

    public sealed record Progress(string Stage, double Fraction);

    public enum FileSource
    {
        None,
        Embedded,
        Download,
    }

    public static FileSource SourceOfFiles =>
        Payload.Exists ? FileSource.Embedded
        : SetupInfo.IsLauncher ? FileSource.Download
        : FileSource.None;

    public static string StagedArchivePath(string version) =>
        Path.Combine(Path.GetTempPath(), $"Lumenotepad-{version}-win-x64-portable.zip");

    public static string InstalledVersion(string carried, string? downloaded) =>
        string.IsNullOrWhiteSpace(downloaded) ? carried : downloaded;

    public static async Task InstallAsync(InstallOptions o, string version, IProgress<Progress>? progress,
                                          Action<string> log, CancellationToken ct)
    {
        if (o.Validate() is { } why) throw new InvalidOperationException(why);

        progress?.Report(new Progress("Closing Lumenotepad", 0));
        await CloseAppAsync(log, ct);

        string? downloaded = null;
        if (SourceOfFiles == FileSource.Download)
        {
            downloaded = await DownloadAndExtractAsync(o, progress, log, ct);
            EnsureUninstaller(o.InstallDir, log);
        }
        else
        {
            progress?.Report(new Progress("Copying files", 0));
            var inner = new Progress<double>(f => progress?.Report(new Progress("Copying files", f * 0.85)));
            await Payload.ExtractAsync(o.InstallDir, inner, log, ct);
        }

        progress?.Report(new Progress("Creating shortcuts", 0.9));
        Shortcuts(o, log);

        progress?.Report(new Progress("Registering with Windows", 0.95));
        RegisterUninstall(o,
            PreferBinaryVersion(BinaryVersion(o.InstallDir), InstalledVersion(version, downloaded))
                ?? InstalledVersion(version, downloaded),
            log);

        progress?.Report(new Progress("Done", 1));
        log("Install complete.");
    }

    private static async Task<string> DownloadAndExtractAsync(InstallOptions o, IProgress<Progress>? progress,
                                                              Action<string> log, CancellationToken ct)
    {
        progress?.Report(new Progress("Checking for the latest version", 0.05));
        using var fetcher = new ReleaseFetcher();

        var release = await fetcher.LatestAsync(ct)
            ?? throw new InvalidOperationException(
                "Could not reach the Lumenotepad releases page. Check your connection and try again, or " +
                "download the portable version instead.");

        log($"Latest release is {release.Version}.");
        string staged = StagedArchivePath(release.Version);

        try
        {
            progress?.Report(new Progress($"Downloading Lumenotepad {release.Version}", 0.1));
            var downloading = new Progress<double>(f =>
                progress?.Report(new Progress($"Downloading Lumenotepad {release.Version}", 0.1 + f * 0.6)));
            await fetcher.DownloadClientAsync(release, staged, downloading, ct);
            log($"Downloaded and verified {Path.GetFileName(staged)}.");

            progress?.Report(new Progress("Copying files", 0.7));
            var extracting = new Progress<double>(f => progress?.Report(new Progress("Copying files", 0.7 + f * 0.15)));
            await Payload.ExtractZipAsync(staged, o.InstallDir, extracting, log, ct);

            return release.Version;
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch
            {
            }
        }
    }

    private static void EnsureUninstaller(string installDir, Action<string> log)
    {
        try
        {
            string dest = Path.Combine(installDir, UninstallerName);
            string self = SelfPath();
            if (File.Exists(dest) || self.Length == 0 || !File.Exists(self)) return;
            File.Copy(self, dest);
            log("Added the uninstaller.");
        }
        catch (Exception ex) { log("Couldn't add an uninstaller: " + ex.Message); }
    }

    public static async Task UninstallAsync(string installDir, bool keepNotes, IProgress<Progress>? progress,
                                            Action<string> log, CancellationToken ct)
    {
        progress?.Report(new Progress("Closing Lumenotepad", 0));
        await CloseAppAsync(log, ct);

        progress?.Report(new Progress("Removing Windows entries", 0.15));
        if (OperatingSystem.IsWindows())
            try { Registry.CurrentUser.DeleteSubKeyTree(ArpKey, throwOnMissingSubKey: false); } catch { }

        progress?.Report(new Progress("Removing shortcuts", 0.25));
        foreach (string lnk in new[] { StartMenuLink(), DesktopLink() })
            try { if (File.Exists(lnk)) { File.Delete(lnk); log($"Removed {lnk}"); } } catch { }

        progress?.Report(new Progress("Removing files", 0.35));
        RemoveInstallDir(installDir, keepNotes, log, progress, ct);

        if (keepNotes)
        {
            log($"Kept your notes: the {UserDataDirName} folder stays in {installDir}.");
        }
        else
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            try { if (Directory.Exists(appData)) { Directory.Delete(appData, recursive: true); log($"Removed {appData}"); } }
            catch (Exception ex) { log("Couldn't remove the log folder: " + ex.Message); }
        }

        progress?.Report(new Progress("Done", 1));
    }

    public static async Task CloseAppAsync(Action<string> log, CancellationToken ct)
    {
        var procs = Running();
        if (procs.Length == 0) return;
        log($"Lumenotepad is running ({procs.Length} process) — asking it to close.");
        foreach (var p in procs)
            try { p.CloseMainWindow(); } catch { }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (Running().Length == 0) { await Task.Delay(400, ct); return; }
            await Task.Delay(200, ct);
        }
        throw new InvalidOperationException("Lumenotepad is still running. Close it and try again.");
    }

    public static Process[] Running()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName)
                .Where(p =>
                {
                    try { return !PathEquals(p.MainModule?.FileName ?? "", SelfPath()); }
                    catch { return true; }
                })
                .ToArray();
        }
        catch { return Array.Empty<Process>(); }
    }

    public static string? ExistingInstall()
        => ReadOurArp("InstallLocation") is { } a && Directory.Exists(a) ? a : null;

    public static string? ExistingVersion()
    {
        string? recorded = ReadOurArp("DisplayVersion");
        string? onDisk = BinaryVersion(ExistingInstall());
        return PreferBinaryVersion(onDisk, recorded);
    }

    public static string? BinaryVersion(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;
        try
        {
            string exe = Path.Combine(installDir, ExeName);
            if (!File.Exists(exe)) return null;
            return NormalizeVersion(FileVersionInfo.GetVersionInfo(exe).ProductVersion);
        }
        catch { return null; }
    }

    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string v = raw.Trim();
        int plus = v.IndexOf('+');
        if (plus > 0) v = v[..plus];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public static string? PreferBinaryVersion(string? fromBinary, string? recorded) =>
        string.IsNullOrWhiteSpace(fromBinary) ? recorded : fromBinary;

    private static string? ReadOurArp(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ArpKey);
            if (k?.GetValue(name) is string s && s.Length > 0) return s;
        }
        catch
        {
        }
        return null;
    }

    private static void RemoveInstallDir(string dir, bool keepNotes, Action<string> log,
                                         IProgress<Progress>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(dir)) return;
        string self = SelfPath();
        string userData = Path.GetFullPath(Path.Combine(dir, UserDataDirName));
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PathEquals(files[i], self)) continue;
            if (keepNotes && Path.GetFullPath(files[i]).StartsWith(userData + Path.DirectorySeparatorChar,
                                                                   StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(files[i]); } catch { }
            if (i % 40 == 0) progress?.Report(new Progress("Removing files", 0.35 + 0.55 * i / Math.Max(1, files.Length)));
        }
        foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (keepNotes && (PathEquals(sub, userData) ||
                              Path.GetFullPath(sub).StartsWith(userData + Path.DirectorySeparatorChar,
                                                               StringComparison.OrdinalIgnoreCase))) continue;
            try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
        }
        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        if (File.Exists(self) && PathEquals(Path.GetDirectoryName(self) ?? "", dir))
        {
            log("The uninstaller removes itself on the next restart.");
            ScheduleDeleteOnReboot(self);
        }
    }

    private static void Shortcuts(InstallOptions o, Action<string> log)
    {
        string exe = Path.Combine(o.InstallDir, ExeName);
        if (o.StartMenuShortcut) MakeLink(StartMenuLink(), exe, o.InstallDir, log); else Remove(StartMenuLink());
        if (o.DesktopShortcut) MakeLink(DesktopLink(), exe, o.InstallDir, log); else Remove(DesktopLink());

        void Remove(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }

    private static string StartMenuLink() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Lumenotepad.lnk");
    private static string DesktopLink() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lumenotepad.lnk");

    private static void MakeLink(string linkPath, string target, string workingDir, Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) { log("Shell scripting host unavailable — skipped a shortcut."); return; }
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = target;
            link.WorkingDirectory = workingDir;
            link.IconLocation = target + ",0";
            link.Description = "Lumenotepad";
            link.Save();
            log($"Shortcut: {linkPath}");
        }
        catch (Exception ex) { log($"Couldn't create {Path.GetFileName(linkPath)}: {ex.Message}"); }
    }

    private static void RegisterUninstall(InstallOptions o, string version, Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            string exe = Path.Combine(o.InstallDir, ExeName);
            string uninst = Path.Combine(o.InstallDir, UninstallerName);
            long size = 0;
            try { size = new DirectoryInfo(o.InstallDir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch
            {
            }

            using var k = Registry.CurrentUser.CreateSubKey(ArpKey);
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", version);
            k.SetValue("Publisher", AppName);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", o.InstallDir);
            k.SetValue("UninstallString", $"\"{uninst}\" --uninstall");
            k.SetValue("QuietUninstallString", $"\"{uninst}\" --uninstall --silent");
            k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            log("Registered in Add or remove programs.");
        }
        catch (Exception ex) { log("Couldn't register the uninstall entry: " + ex.Message); }
    }

    private static void ScheduleDeleteOnReboot(string file)
    {
        try { MoveFileEx(file, null, 4); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? newName, int flags);

    private static string SelfPath()
    {
        try { return Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    private static bool PathEquals(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
