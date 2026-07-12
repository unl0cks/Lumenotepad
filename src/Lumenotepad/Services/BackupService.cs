using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Lumenotepad.Services;

/// <summary>Zip backups of the whole userdata folder. Due-check and prune math are pure and tested;
/// the zip/prune I/O runs on real folders (callers run it off the UI thread).</summary>
public static class BackupService
{
    private const string Prefix = "lumenotepad-backup-";

    /// <summary>A backup is due when the interval is positive AND we've never backed up, or at least
    /// that many days have elapsed.</summary>
    public static bool IsDue(DateTime? lastUtc, int everyDays, DateTime nowUtc) =>
        everyDays > 0 && (lastUtc is null || (nowUtc - lastUtc.Value).TotalDays >= everyDays);

    /// <summary>Given existing backups newest-first, the ones to delete to keep only K (K ≤ 0 keeps all).</summary>
    public static IReadOnlyList<string> ToPrune(IReadOnlyList<string> backupsNewestFirst, int keep) =>
        keep <= 0 ? Array.Empty<string>() : backupsNewestFirst.Skip(keep).ToList();

    /// <summary>Zip <paramref name="userDataDir"/> into <paramref name="destFolder"/> as
    /// lumenotepad-backup-yyyyMMdd-HHmmss.zip. Zips to a temp file first, then moves in — so a backup
    /// folder that happens to sit inside userdata never zips its own partial output. Returns the path.</summary>
    public static string CreateBackup(string userDataDir, string destFolder, DateTime nowUtc)
    {
        Directory.CreateDirectory(destFolder);
        var name = $"{Prefix}{nowUtc:yyyyMMdd-HHmmss}.zip";
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            ZipFile.CreateFromDirectory(userDataDir, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
            var dest = Path.Combine(destFolder, name);
            File.Move(temp, dest, overwrite: true);
            return dest;
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    /// <summary>Delete the oldest backups so only <paramref name="keep"/> remain (the timestamped names
    /// sort lexically == chronologically, so name-descending is newest-first).</summary>
    public static void PruneBackups(string destFolder, int keep)
    {
        if (!Directory.Exists(destFolder)) return;
        var newestFirst = Directory.GetFiles(destFolder, Prefix + "*.zip")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
        foreach (var f in ToPrune(newestFirst, keep))
            try { File.Delete(f); } catch { }
    }
}
