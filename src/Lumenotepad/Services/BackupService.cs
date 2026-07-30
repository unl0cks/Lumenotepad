using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Lumenotepad.Services;

public static class BackupService
{
    private const string Prefix = "lumenotepad-backup-";

    public static bool IsDue(DateTime? lastUtc, int everyDays, DateTime nowUtc) =>
        everyDays > 0 && (lastUtc is null || (nowUtc - lastUtc.Value).TotalDays >= everyDays);

    public static IReadOnlyList<string> ToPrune(IReadOnlyList<string> backupsNewestFirst, int keep) =>
        keep <= 0 ? Array.Empty<string>() : backupsNewestFirst.Skip(keep).ToList();

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

    public static void PruneBackups(string destFolder, int keep)
    {
        if (!Directory.Exists(destFolder)) return;
        var newestFirst = Directory.GetFiles(destFolder, Prefix + "*.zip")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
        foreach (var f in ToPrune(newestFirst, keep))
            try { File.Delete(f); } catch { }
    }
}
