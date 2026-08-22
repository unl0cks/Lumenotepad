using System;
using System.IO;
using System.Linq;

namespace Lumenotepad.Setup.Services;

public sealed class InstallOptions
{
    public string InstallDir { get; set; } = DefaultInstallDir;
    public bool DesktopShortcut { get; set; }
    public bool StartMenuShortcut { get; set; } = true;
    public bool LaunchWhenDone { get; set; } = true;

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lumenotepad");

    public string ToArguments(string progressFile) =>
        $"--silent --dir \"{InstallDir}\" --progress \"{progressFile}\"" +
        (DesktopShortcut ? " --desktop" : "") +
        (StartMenuShortcut ? " --startmenu" : "");

    public static InstallOptions FromArguments(string[] args)
    {
        var o = new InstallOptions
        {
            StartMenuShortcut = false, LaunchWhenDone = false,
        };
        for (int i = 0; i < args.Length; i++)
            switch (args[i].ToLowerInvariant())
            {
                case "--dir" when i + 1 < args.Length: o.InstallDir = args[++i]; break;
                case "--desktop": o.DesktopShortcut = true; break;
                case "--startmenu": o.StartMenuShortcut = true; break;
                case "--launch": o.LaunchWhenDone = true; break;
            }
        if (string.IsNullOrWhiteSpace(o.InstallDir)) o.InstallDir = DefaultInstallDir;
        return o;
    }

    public string? Validate()
    {
        string dir = InstallDir?.Trim() ?? "";
        if (dir.Length == 0) return "Choose a folder to install into.";
        if (dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "That path contains characters Windows won't allow.";
        try
        {
            string full = Path.GetFullPath(dir);
            if (!Path.IsPathRooted(full)) return "Use a full path, starting with a drive letter.";
            if (Path.GetPathRoot(full)!.Equals(full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                                               StringComparison.OrdinalIgnoreCase))
                return "Pick a folder inside the drive, not the drive itself.";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return "Not inside the Windows folder.";
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()
                && !File.Exists(Path.Combine(full, InstallEngine.ExeName)))
                return "That folder isn't empty and doesn't already contain Lumenotepad.";
            if (!CanWriteInto(full))
                return "Lumenotepad can't write there. Pick a folder you own, such as the default one.";
        }
        catch (Exception ex) { return ex.Message; }
        return null;
    }

    private static bool CanWriteInto(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            while (dir is not null && !dir.Exists) dir = dir.Parent;
            if (dir is null) return false;

            string probe = Path.Combine(dir.FullName, $".lumenotepad-write-test-{Environment.ProcessId}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch { return false; }
    }
}
