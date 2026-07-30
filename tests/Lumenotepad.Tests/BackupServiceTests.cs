using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class BackupServiceTests
{
    [Theory]
    [InlineData(0, null, false)]
    [InlineData(-3, null, false)]
    [InlineData(7, null, true)]
    public void IsDue_offAndNeverCases(int everyDays, object? _, bool expected)
    {
        var now = new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, BackupService.IsDue(null, everyDays, now));
    }

    [Fact]
    public void IsDue_respectsInterval()
    {
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(BackupService.IsDue(now.AddDays(-3), 7, now));
        Assert.True(BackupService.IsDue(now.AddDays(-8), 7, now));
        Assert.True(BackupService.IsDue(now.AddDays(-7), 7, now));
    }

    [Fact]
    public void ToPrune_keepsNewestK()
    {
        var newestFirst = new[] { "g", "f", "e", "d", "c", "b", "a" };
        Assert.Equal(new[] { "b", "a" }, BackupService.ToPrune(newestFirst, 5).ToArray());
        Assert.Empty(BackupService.ToPrune(newestFirst, 10));
    }

    [Fact]
    public void CreateBackup_zipsUserdata_thenPrune()
    {
        var root = Path.Combine(Path.GetTempPath(), "lnp-bk-" + Path.GetRandomFileName());
        var data = Path.Combine(root, "userdata");
        var dest = Path.Combine(root, "backups");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
        try
        {
            var now = new DateTime(2026, 7, 12, 9, 30, 0, DateTimeKind.Utc);
            var zip = BackupService.CreateBackup(data, dest, now);

            Assert.True(File.Exists(zip));
            Assert.EndsWith(".zip", zip);
            using (var z = ZipFile.OpenRead(zip))
                Assert.Contains(z.Entries, e => e.FullName.EndsWith("settings.json"));

            for (int i = 0; i < 6; i++)
                File.WriteAllText(Path.Combine(dest, $"lumenotepad-backup-2026010{i}-000000.zip"), "x");
            BackupService.PruneBackups(dest, 5);
            Assert.Equal(5, Directory.GetFiles(dest, "lumenotepad-backup-*.zip").Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
