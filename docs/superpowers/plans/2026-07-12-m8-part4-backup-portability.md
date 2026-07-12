# M8 Part 4 — Backup & Portability

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatic (and on-demand) zip backups of the userdata folder, notebook→Markdown export of
the whole workspace, and .txt/.md import as a new page.

**Architecture:** Two pure, unit-tested services — `MarkdownExport` (CanvasDocument → Markdown string)
and `BackupService` (due-check + prune math, plus zip/prune I/O on real folders) — plus VM
orchestration methods (`RunBackupNow`, `ExportAllNotebooks`, `ImportTextAsPage`) that the prefs
Data & tools panel drives via folder/file pickers. Backups run off-thread; a startup hook kicks one
off when due. Settings follow the established AppSettings→VM guard-save pattern; `LastBackupUtc` is
bookkeeping (like `LastPageId`, not a pref, not reset).

**Tech Stack:** Avalonia 12.0.4 (StorageProvider pickers), .NET 10 (`System.IO.Compression.ZipFile`
is in the shared framework — NO new package), xUnit.

**Verified facts (do not re-derive):**
- `CanvasDocument`: `List<NoteBox> Boxes`; `NoteBox` has `double X/Y/Width/H`, `RichDocument Doc`,
  `bool IsEmpty`, `AddBox(x, y, width = DefaultWidth, RichDocument? doc = null)`. `NoteBox.DefaultWidth = 360`.
- `RichDocument`: `List<Paragraph> Paragraphs` (always ≥1). `Paragraph`: `List<RichRun> Runs` (public
  field), `string Text`, `string? Bullet` (null | "dot"|"arrow"|"star"|"heart"|"flower"|"spark" |
  "num" | "check"), `bool Checked`. `RichRun`: `string Text`, `bool Bold/Italic/Underline/Strike`.
- `WorkspaceStore`: `LoadPageDoc(Notebook, pageId)` → `CanvasDocument?`; `SavePageDoc`; `PageDocTime`.
  Page content lives at `<notebook>/pages/<id>.page.json`.
- `MainViewModel`: `_store` (WorkspaceStore), `_workspace`, `Notebooks`, `SelectedSection`,
  `SelectedNotebook`, `SelectedPage`, `SettingsDir`, `DocumentFor(page)`, `FlushDirtyDocs()`,
  `Save()`, private `_docs`/`_dirty` machinery, `_settings`/`_settingsDir` (guarded like every hook).
- The VM ctor settings-load fires every `OnXChanged` hook whose persisted value differs from the field
  default; hooks touching workspace/UI must guard `_workspace`. The three new backup hooks are
  save-only → the standard `_settings`/`_settingsDir` guard suffices.
- Prefs Data & tools panel (`DataPanel` in PreferencesWindow.axaml) currently has STORAGE + MAINTENANCE
  sections; `RefreshDataPanel()` fills folder path + workspace size when the panel shows. Data & tools
  is a GATED (advanced) category — correct home for backup/export tools.
- `TopLevel.GetTopLevel(this).StorageProvider` gives `OpenFilePickerAsync` /
  `OpenFolderPickerAsync`; results expose `TryGetLocalPath()`. `PickCover` in MainView.axaml.cs is the
  existing OpenFilePickerAsync reference.
- Build gotcha: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test.
  `cd /e/CLAUDE/Lumenotepad` in every Bash call. Never launch the GUI from a subagent.

---

### Task 1: MarkdownExport (pure, TDD)

**Files:**
- Create: `src/Lumenotepad/Services/MarkdownExport.cs`
- Create: `tests/Lumenotepad.Tests/MarkdownExportTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Lumenotepad.Tests/MarkdownExportTests.cs`:

```csharp
using Lumenotepad.Editor;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class MarkdownExportTests
{
    private static Paragraph P(string text, string? bullet = null, bool chk = false) =>
        new() { Runs = { new RichRun { Text = text } }, Bullet = bullet, Checked = chk };

    private static NoteBox Box(double x, double y, params Paragraph[] paras)
    {
        var doc = new RichDocument();
        doc.Paragraphs.Clear();
        foreach (var p in paras) doc.Paragraphs.Add(p);
        return new NoteBox(doc) { X = x, Y = y };
    }

    [Fact]
    public void TitleOnly_whenNoBoxes()
    {
        var md = MarkdownExport.PageToMarkdown("My Page", new CanvasDocument());
        Assert.Equal("# My Page\n", md);
    }

    [Fact]
    public void BlankTitle_fallsBackToUntitled()
    {
        var md = MarkdownExport.PageToMarkdown("  ", new CanvasDocument());
        Assert.Equal("# Untitled\n", md);
    }

    [Fact]
    public void PlainParagraph_afterHeading()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 0, P("Hello world")));
        Assert.Equal("# T\n\nHello world\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void Lists_bulletNumberedChecklist()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 0,
            P("A", "dot"),
            P("One", "num"),
            P("Two", "num"),
            P("todo", "check", chk: false),
            P("done", "check", chk: true)));
        Assert.Equal("# T\n\n- A\n1. One\n2. Two\n- [ ] todo\n- [x] done\n",
            MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void InlineEmphasis_boldItalicStrike()
    {
        var doc = new CanvasDocument();
        var rich = new RichDocument();
        rich.Paragraphs.Clear();
        rich.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new RichRun { Text = "b", Bold = true },
                new RichRun { Text = "i", Italic = true },
                new RichRun { Text = "x", Bold = true, Italic = true },
                new RichRun { Text = "s", Strike = true },
            },
        });
        doc.Boxes.Add(new NoteBox(rich) { X = 0, Y = 0 });
        Assert.Equal("# T\n\n**b***i****x***~~s~~\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void EmphasisKeepsSurroundingSpacesOutsideMarkers()
    {
        var doc = new CanvasDocument();
        var rich = new RichDocument();
        rich.Paragraphs.Clear();
        rich.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = " hi ", Bold = true } } });
        doc.Boxes.Add(new NoteBox(rich) { X = 0, Y = 0 });
        Assert.Equal("# T\n\n **hi** \n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Fact]
    public void Boxes_orderedByYThenX_emptySkipped()
    {
        var doc = new CanvasDocument();
        doc.Boxes.Add(Box(0, 100, P("second")));   // added first, lower on the page
        doc.Boxes.Add(Box(0, 10, P("first")));     // higher up → comes first
        doc.Boxes.Add(new NoteBox() { X = 0, Y = 5 });   // empty → skipped
        Assert.Equal("# T\n\nfirst\n\nsecond\n", MarkdownExport.PageToMarkdown("T", doc));
    }

    [Theory]
    [InlineData("Photosynthesis", "Photosynthesis")]
    [InlineData("A/B: c?", "A-B- c")]
    [InlineData("   ", "Untitled")]
    [InlineData("...", "Untitled")]
    public void SafeName_stripsIllegalChars(string raw, string expected) =>
        Assert.Equal(expected, MarkdownExport.SafeName(raw));
}
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "MarkdownExportTests" 2>&1 | tail -15`
Expected: compile errors (`MarkdownExport` doesn't exist).

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Services/MarkdownExport.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumenotepad.Editor;

namespace Lumenotepad.Services;

/// <summary>Assembles a page's freeform canvas into plain, readable Markdown (UTF-8, portable — not a
/// perfect round-trip). Pure and unit-tested; the store/UI layer handles files. Boxes are emitted in
/// reading order (top-to-bottom, then left-to-right); empty boxes are skipped.</summary>
public static class MarkdownExport
{
    public static string PageToMarkdown(string title, CanvasDocument doc)
    {
        var blocks = new List<string>
        {
            "# " + (string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim()),
        };
        foreach (var box in doc.Boxes.Where(b => !b.IsEmpty).OrderBy(b => b.Y).ThenBy(b => b.X))
            blocks.Add(BoxToMarkdown(box));
        return string.Join("\n\n", blocks) + "\n";
    }

    private static string BoxToMarkdown(NoteBox box)
    {
        var lines = new List<string>();
        int num = 0;                                     // running count within a "num" block
        foreach (var p in box.Doc.Paragraphs)
        {
            if (p.Bullet == "num") num++; else num = 0;
            lines.Add(Prefix(p, num) + Inline(p));
        }
        return string.Join("\n", lines);
    }

    private static string Prefix(Paragraph p, int num) => p.Bullet switch
    {
        null => "",
        "num" => $"{num}. ",
        "check" => p.Checked ? "- [x] " : "- [ ] ",
        _ => "- ",                                       // every cute glyph bullet → a Markdown bullet
    };

    /// <summary>Concatenate the paragraph's runs, wrapping bold/italic/strike in Markdown markers.
    /// Emphasis markers hug the non-space core so " hi " stays " **hi** " (renderers reject "** hi **").</summary>
    private static string Inline(Paragraph p) => string.Concat(p.Runs.Select(Run));

    private static string Run(RichRun r)
    {
        var text = r.Text;
        if (text.Length == 0) return "";
        string open = "", close = "";
        if (r.Bold) { open += "**"; close = "**" + close; }
        if (r.Italic) { open += "*"; close = "*" + close; }
        if (r.Strike) { open = "~~" + open; close += "~~"; }
        if (open.Length == 0 && close.Length == 0) return text;

        int a = 0; while (a < text.Length && char.IsWhiteSpace(text[a])) a++;
        int b = text.Length; while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
        if (a >= b) return text;                         // all whitespace: never wrap
        return text[..a] + open + text[a..b] + close + text[b..];
    }

    /// <summary>A readable, filesystem-safe file/folder name: keep letters/digits/space/(-_.), turn any
    /// other run into a single dash, trim, collapse repeats; empty/symbol-only → "Untitled".</summary>
    public static string SafeName(string raw)
    {
        var sb = new StringBuilder();
        bool lastDash = false;
        foreach (char c in (raw ?? "").Trim())
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') { sb.Append(c); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        var s = sb.ToString().Trim().Trim('-', '.').Trim();
        return s.Length == 0 ? "Untitled" : s;
    }
}
```

Note on the `A/B: c?` → `A-B- c` case: `/` and `:` each become a dash, the space before `c` survives,
`?` at the end becomes a dash then is trimmed. `.` -only input trims to empty → "Untitled".

- [ ] **Step 4: Run — verify green (full suite)**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -5`
Expected: all pass (116 + 9 new = 125). If a SafeName theory case mismatches, fix `SafeName` to satisfy the exact strings — do not change the tests.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): MarkdownExport — CanvasDocument to portable Markdown (pure, tested)"
```

---

### Task 2: BackupService + backup settings (TDD)

**Files:**
- Create: `src/Lumenotepad/Services/BackupService.cs`
- Create: `tests/Lumenotepad.Tests/BackupServiceTests.cs`
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Lumenotepad.Tests/BackupServiceTests.cs`:

```csharp
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
    [InlineData(0, null, false)]                         // every=0 → never
    [InlineData(-3, null, false)]
    [InlineData(7, null, true)]                          // never backed up → due
    public void IsDue_offAndNeverCases(int everyDays, object? _, bool expected)
    {
        var now = new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, BackupService.IsDue(null, everyDays, now));
    }

    [Fact]
    public void IsDue_respectsInterval()
    {
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(BackupService.IsDue(now.AddDays(-3), 7, now));   // 3 days < 7
        Assert.True(BackupService.IsDue(now.AddDays(-8), 7, now));    // 8 days ≥ 7
        Assert.True(BackupService.IsDue(now.AddDays(-7), 7, now));    // exactly due
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

            // Seed 6 more so there are 7 total, keep 5 → 2 oldest pruned.
            for (int i = 0; i < 6; i++)
                File.WriteAllText(Path.Combine(dest, $"lumenotepad-backup-2026010{i}-000000.zip"), "x");
            BackupService.PruneBackups(dest, 5);
            Assert.Equal(5, Directory.GetFiles(dest, "lumenotepad-backup-*.zip").Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
```

Append to `tests/Lumenotepad.Tests/AppSettingsTests.cs`:

```csharp
    [Fact]
    public void BackupSettings_defaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Null(new AppSettings().BackupFolder);
            Assert.Equal(0, new AppSettings().BackupEveryDays);
            Assert.Equal(5, new AppSettings().BackupKeep);
            Assert.Null(new AppSettings().LastBackupUtc);

            var when = new System.DateTime(2026, 7, 1, 8, 0, 0, System.DateTimeKind.Utc);
            var s = new AppSettings { BackupFolder = @"C:\bk", BackupEveryDays = 7, BackupKeep = 3, LastBackupUtc = when };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal(@"C:\bk", loaded.BackupFolder);
            Assert.Equal(7, loaded.BackupEveryDays);
            Assert.Equal(3, loaded.BackupKeep);
            Assert.Equal(when, loaded.LastBackupUtc);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Append to `tests/Lumenotepad.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public void ResetSettingsToDefaults_restoresBackupPrefs_butNotLastBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.BackupFolder = @"C:\bk";
            vm.BackupEveryDays = 7;
            vm.BackupKeep = 9;

            vm.ResetSettingsToDefaults();

            Assert.Null(vm.BackupFolder);
            Assert.Equal(0, vm.BackupEveryDays);
            Assert.Equal(5, vm.BackupKeep);
            var persisted = AppSettings.Load(dir);
            Assert.Null(persisted.BackupFolder);
            Assert.Equal(0, persisted.BackupEveryDays);
            Assert.Equal(5, persisted.BackupKeep);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "BackupServiceTests|BackupSettings_defaultsAndRoundTrip|ResetSettingsToDefaults_restoresBackupPrefs_butNotLastBackup" 2>&1 | tail -15`
Expected: compile errors (`BackupService`, `BackupFolder`, etc. don't exist).

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Services/BackupService.cs`:

```csharp
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
```

In `src/Lumenotepad/Services/AppSettings.cs`, after the `GridSnap` line (added in Part 3), add:

```csharp
    public string? BackupFolder { get; set; }               // null = auto-backup off
    public int BackupEveryDays { get; set; }                // 0 = off; else backup every N days on startup
    public int BackupKeep { get; set; } = 5;                // how many zips to retain
    public DateTime? LastBackupUtc { get; set; }            // bookkeeping (like LastPageId); not a pref, not reset
```

Add `using System;` at the top of AppSettings.cs if it is not already present (needed for `DateTime`).

In `src/Lumenotepad/ViewModels/MainViewModel.cs`:

1. After the `_gridSnap` field (Part 3), add:

```csharp
    [ObservableProperty] private string? _backupFolder;    // prefs: auto-backup destination (null = off)
    [ObservableProperty] private int _backupEveryDays;     // prefs: 0 = off
    [ObservableProperty] private int _backupKeep = 5;      // prefs: retained zip count
```

2. In the ctor settings-load block, after `GridSnap = _settings.GridSnap;`, add:

```csharp
            BackupFolder = _settings.BackupFolder;
            BackupEveryDays = _settings.BackupEveryDays;
            BackupKeep = _settings.BackupKeep;
```

3. After `OnGridSnapChanged` (Part 3), add the three save hooks:

```csharp
    partial void OnBackupFolderChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupFolder = value;
        _settings.Save(_settingsDir);
    }

    partial void OnBackupEveryDaysChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupEveryDays = value;
        _settings.Save(_settingsDir);
    }

    partial void OnBackupKeepChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupKeep = value;
        _settings.Save(_settingsDir);
    }
```

4. In `ResetSettingsToDefaults`, after the `PageGrid = d.PageGrid; GridSnap = d.GridSnap;` line, add:

```csharp
        BackupFolder = d.BackupFolder; BackupEveryDays = d.BackupEveryDays; BackupKeep = d.BackupKeep;
```

(`LastBackupUtc` is bookkeeping — NOT reset, like `LastPageId`.)

5. Add the last-backup accessor + run-now method after `SetNotebookPaperTint` (Part 3):

```csharp
    /// <summary>When the last successful auto/manual backup ran (UTC), or null.</summary>
    public System.DateTime? LastBackupUtc => _settings?.LastBackupUtc;

    /// <summary>True when a backup folder is set and the interval has elapsed (startup checks this).</summary>
    public bool BackupDue() =>
        _settings is { BackupFolder: { Length: > 0 } folder } s &&
        BackupService.IsDue(s.LastBackupUtc, s.BackupEveryDays, System.DateTime.UtcNow);

    /// <summary>Zip userdata to the backup folder, prune to BackupKeep, stamp LastBackupUtc. Returns the
    /// zip path, or null if no folder is set. I/O — call from a background task. Flushes dirty docs
    /// first so the backup captures the latest edits.</summary>
    public string? RunBackupNow()
    {
        if (_settings is not { BackupFolder: { Length: > 0 } folder } s || _settingsDir is null) return null;
        FlushDirtyDocs();
        var now = System.DateTime.UtcNow;
        var zip = BackupService.CreateBackup(_settingsDir, folder, now);
        BackupService.PruneBackups(folder, s.BackupKeep);
        s.LastBackupUtc = now;
        s.Save(_settingsDir);
        return zip;
    }
```

- [ ] **Step 4: Run — verify green (full suite)**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -5`
Expected: 125 + 5 new = 130 pass.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): BackupService (zip + prune) + backup settings/VM"
```

---

### Task 3: Import / Export VM methods + startup auto-backup (TDD)

**Files:**
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Lumenotepad.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public void ImportTextAsPage_addsPageWithTextBox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            int before = vm.SelectedSection!.Pages.Count;

            var page = vm.ImportTextAsPage("Notes", "line one\nline two");

            Assert.NotNull(page);
            Assert.Equal(before + 1, vm.SelectedSection!.Pages.Count);
            Assert.Same(page, vm.SelectedPage);
            var doc = vm.DocumentFor(page!);
            Assert.Single(doc.Boxes);
            Assert.Equal("line one\nline two", doc.Boxes[0].Doc.GetText());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExportAllNotebooks_writesMarkdownTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        var dest = Path.Combine(Path.GetTempPath(), "lnp-exp-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.ImportTextAsPage("Hello", "world");    // gives a page with real content, saved to disk

            int pages = vm.ExportAllNotebooks(dest);

            Assert.True(pages >= 1);
            var files = Directory.GetFiles(dest, "*.md", SearchOption.AllDirectories);
            Assert.Contains(files, f => Path.GetFileName(f) == "Hello.md");
            var body = File.ReadAllText(files.First(f => Path.GetFileName(f) == "Hello.md"));
            Assert.Contains("# Hello", body);
            Assert.Contains("world", body);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
        }
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "ImportTextAsPage_addsPageWithTextBox|ExportAllNotebooks_writesMarkdownTree" 2>&1 | tail -12`
Expected: compile errors (`ImportTextAsPage`, `ExportAllNotebooks` don't exist).

- [ ] **Step 3: Implement**

In `src/Lumenotepad/ViewModels/MainViewModel.cs`, add these methods after `RunBackupNow` (Task 2). Add
`using System.Text;` and `using System.IO;` at the top if not already present (check the existing usings).

```csharp
    /// <summary>Import plain text as a new page in the current section: one note box holding the text,
    /// one paragraph per line. Returns the new page (null if no section is selected).</summary>
    public Page? ImportTextAsPage(string title, string text)
    {
        if (SelectedSection is not { } sec) return null;
        var page = new Page { Title = string.IsNullOrWhiteSpace(title) ? "Imported" : title.Trim() };
        sec.Pages.Add(page);
        var doc = DocumentFor(page);                       // new empty doc, Changed wired for autosave
        doc.AddBox(40, 40, NoteBox.DefaultWidth, PlainTextDoc(text));   // fires Changed → dirty
        SelectedPage = page;
        Save();                                            // the tree
        FlushDirtyDocs();                                  // the page content
        return page;
    }

    private static Editor.RichDocument PlainTextDoc(string text)
    {
        var d = new Editor.RichDocument();
        d.Paragraphs.Clear();
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            d.Paragraphs.Add(new Editor.Paragraph { Runs = { new Editor.RichRun { Text = line } } });
        if (d.Paragraphs.Count == 0) d.Paragraphs.Add(new Editor.Paragraph());
        return d;
    }

    /// <summary>Export every notebook to <paramref name="destFolder"/> as
    /// &lt;notebook&gt;/&lt;section&gt;/&lt;page&gt;.md (UTF-8, no BOM). Flushes dirty docs first so
    /// the export sees the latest edits, then reads each page from disk. Returns the page count written.</summary>
    public int ExportAllNotebooks(string destFolder)
    {
        FlushDirtyDocs();
        int count = 0;
        var utf8 = new UTF8Encoding(false);
        foreach (var nb in Notebooks)
        {
            var nbDir = Path.Combine(destFolder, MarkdownExport.SafeName(nb.Name));
            foreach (var sec in nb.Sections)
            {
                var secDir = Path.Combine(nbDir, MarkdownExport.SafeName(sec.Name));
                Directory.CreateDirectory(secDir);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var page in sec.Pages)
                {
                    var doc = _store.LoadPageDoc(nb, page.Id) ?? new Editor.CanvasDocument();
                    var file = UniqueFile(used, MarkdownExport.SafeName(page.Title));
                    File.WriteAllText(Path.Combine(secDir, file + ".md"),
                        MarkdownExport.PageToMarkdown(page.Title, doc), utf8);
                    count++;
                }
            }
        }
        return count;
    }

    private static string UniqueFile(HashSet<string> used, string name)
    {
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }
```

Then wire the startup auto-backup: in the ctor, at the very end (after `RefreshHome();`), add:

```csharp
        if (BackupDue())
            System.Threading.Tasks.Task.Run(RunBackupNow);   // off the UI thread; no-op when no folder/not due
```

- [ ] **Step 4: Run — verify green (full suite)**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -5`
Expected: 130 + 2 new = 132 pass.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): notebook Markdown export + text import + startup auto-backup hook"
```

---

### Task 4: Prefs Data & tools — Backup + Portability UI

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` (DataPanel)
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`

- [ ] **Step 1: Add the XAML sections**

In `src/Lumenotepad/Views/PreferencesWindow.axaml`, inside `DataPanel`, AFTER the STORAGE section
(after the `WorkspaceSizeText` Grid) and BEFORE the `<TextBlock Classes="section" Text="MAINTENANCE"/>`
line, insert:

```xml
                        <TextBlock Classes="section" Text="AUTOMATIC BACKUP"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Backup folder"/>
                                <TextBlock x:Name="BackupFolderText" Classes="hint" Text="Not set — automatic backups are off."/>
                            </StackPanel>
                            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                                <Button x:Name="BackupFolderBtn" Content="Choose…"/>
                                <Button x:Name="BackupClearBtn" Content="Clear"/>
                            </StackPanel>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="How often"/>
                                <TextBlock Classes="hint" Text="Checked once each time Lumenotepad starts."/>
                            </StackPanel>
                            <ComboBox x:Name="BackupEveryBox" Grid.Column="1" Width="140" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Keep the newest"/>
                            <TextBlock x:Name="BackupKeepValue" Grid.Column="1" Classes="label" Text="5"/>
                        </Grid>
                        <Slider x:Name="BackupKeepSlider" Minimum="1" Maximum="20"
                                TickFrequency="1" IsSnapToTickEnabled="True"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Back up now"/>
                                <TextBlock x:Name="LastBackupText" Classes="hint" Text="Never backed up."/>
                            </StackPanel>
                            <Button x:Name="BackupNowBtn" Grid.Column="1" Content="Back up now" VerticalAlignment="Center"/>
                        </Grid>

                        <TextBlock Classes="section" Text="IMPORT &amp; EXPORT"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Export all notebooks to Markdown"/>
                                <TextBlock Classes="hint" Text="A folder of &#8249;notebook&#8250;/&#8249;section&#8250;/&#8249;page&#8250;.md files (UTF-8)."/>
                            </StackPanel>
                            <Button x:Name="ExportBtn" Grid.Column="1" Content="Export…" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Import a text file"/>
                                <TextBlock Classes="hint" Text="A .txt or .md file becomes a new page in the current section."/>
                            </StackPanel>
                            <Button x:Name="ImportBtn" Grid.Column="1" Content="Import…" VerticalAlignment="Center"/>
                        </Grid>
                        <TextBlock x:Name="DataStatusText" Classes="hint" Margin="0,8,0,0" Text=""/>
```

`DataStatusText` is the inline result line for backup/export/import (avoids info dialogs, which the
shared `ConfirmDialog` can't do one-button).

- [ ] **Step 2: Wire the controls**

In `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`, add these using directives at the top if absent:
`using System.IO;` (already present per the file), `using Avalonia.Platform.Storage;`.

Add the backup-interval mapping near the other `static readonly` arrays (e.g. below `DateFormats`):

```csharp
    /// <summary>The "How often" choices → days (0 = off).</summary>
    private static readonly (string Label, int Days)[] BackupIntervals =
    {
        ("Off", 0), ("Daily", 1), ("Weekly", 7), ("Every 2 weeks", 14), ("Monthly", 30),
    };
```

In the ctor, after the existing `RelockBtn.Click += ...` block, add:

```csharp
        BackupEveryBox.ItemsSource = BackupIntervals.Select(b => b.Label).ToArray();
        BackupEveryBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && BackupEveryBox.SelectedIndex is >= 0 and var i && i < BackupIntervals.Length
                && vm.BackupEveryDays != BackupIntervals[i].Days) vm.BackupEveryDays = BackupIntervals[i].Days;
        };
        BackupKeepSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && vm.BackupKeep != (int)e.NewValue) vm.BackupKeep = (int)e.NewValue;
            BackupKeepValue.Text = ((int)e.NewValue).ToString();
        };
        BackupFolderBtn.Click += async (_, _) =>
        {
            if (StorageProvider is not { } sp) return;
            var picks = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a backup folder", AllowMultiple = false,
            });
            if (picks.Count > 0 && picks[0].TryGetLocalPath() is { } path && Vm is { } vm)
            {
                vm.BackupFolder = path;
                RefreshDataPanel();
            }
        };
        BackupClearBtn.Click += (_, _) => { if (Vm is { } vm) { vm.BackupFolder = null; RefreshDataPanel(); } };
        BackupNowBtn.Click += async (_, _) =>
        {
            if (Vm is not { } vm) return;
            if (string.IsNullOrEmpty(vm.BackupFolder)) { DataStatusText.Text = "Choose a backup folder first."; return; }
            BackupNowBtn.IsEnabled = false;
            DataStatusText.Text = "Backing up…";
            var path = await System.Threading.Tasks.Task.Run(vm.RunBackupNow);
            BackupNowBtn.IsEnabled = true;
            RefreshDataPanel();
            DataStatusText.Text = path is null ? "Backup failed." : $"Backed up to {path}";
        };
        ExportBtn.Click += async (_, _) =>
        {
            if (Vm is not { } vm || StorageProvider is not { } sp) return;
            var picks = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export all notebooks to…", AllowMultiple = false,
            });
            if (picks.Count == 0 || picks[0].TryGetLocalPath() is not { } dest) return;
            ExportBtn.IsEnabled = false;
            DataStatusText.Text = "Exporting…";
            int n = await System.Threading.Tasks.Task.Run(() => vm.ExportAllNotebooks(dest));
            ExportBtn.IsEnabled = true;
            DataStatusText.Text = $"Exported {n} page{(n == 1 ? "" : "s")} to {dest}";
        };
        ImportBtn.Click += async (_, _) =>
        {
            if (Vm is not { } vm || StorageProvider is not { } sp) return;
            if (vm.SelectedSection is null) { DataStatusText.Text = "Open a notebook section first."; return; }
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import a text file", AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Text & Markdown") { Patterns = new[] { "*.txt", "*.md" } },
                },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            string text; try { text = File.ReadAllText(path); } catch { DataStatusText.Text = "Could not read the file."; return; }
            var page = vm.ImportTextAsPage(Path.GetFileNameWithoutExtension(path), text);
            DataStatusText.Text = page is null ? "Import failed." : $"Imported “{page.Title}” into the current section.";
        };
```

In `RefreshDataPanel()`, at the end of the method, add:

```csharp
        BackupFolderText.Text = string.IsNullOrEmpty(Vm?.BackupFolder)
            ? "Not set — automatic backups are off." : Vm!.BackupFolder;
        BackupEveryBox.SelectedIndex = System.Math.Max(0,
            System.Array.FindIndex(BackupIntervals, b => b.Days == (Vm?.BackupEveryDays ?? 0)));
        BackupKeepSlider.Value = Vm?.BackupKeep ?? 5;
        BackupKeepValue.Text = (Vm?.BackupKeep ?? 5).ToString();
        LastBackupText.Text = Vm?.LastBackupUtc is { } t
            ? $"Last backup {t.ToLocalTime():yyyy-MM-dd HH:mm}." : "Never backed up.";
```

Results are reported through the inline `DataStatusText` label (no dialogs) — the shared
`ConfirmDialog` is a confirm+cancel primitive, not an info box, so it isn't used here. `StorageProvider`
is a `Window` member (this class derives from `Window`), so `StorageProvider` resolves directly.

- [ ] **Step 3: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 132/132 pass.

- [ ] **Step 4: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): Data & tools — auto-backup controls + Markdown export/import"
```

---

### Task 5: Final integration review + relaunch + checklist

- [ ] Dispatch a final integration reviewer (opus) over `git diff <plan-commit>..HEAD` for the 4 feature commits, against this plan + the M8 spec Part 4. Focus on seams: startup auto-backup off-thread safety (no UI-thread/observable touches from `RunBackupNow`), export/import folder handling, ConfirmDialog overload correctness, and that `LastBackupUtc` is bookkeeping (not reset).
- [ ] Fix anything Important+ inline; re-run the suite.
- [ ] Rebuild + relaunch the app for the owner.
- [ ] Update memory (`lumenotepad.md`) with the Part 4 entry.
- [ ] Hand the owner the verification checklist:
  1. Prefs → Data & tools (unlock advanced) → AUTOMATIC BACKUP → Choose a folder, set How often = Weekly, Keep = 5. "Back up now" → a `lumenotepad-backup-*.zip` appears in that folder; the "Last backup" line updates. Run it a few times → never more than 5 zips.
  2. Restart the app after the interval — a backup fires on startup (check the folder / Last backup time). Clear the folder → auto-backups stop.
  3. IMPORT & EXPORT → Export… → pick a folder → a `<notebook>/<section>/<page>.md` tree appears; open one .md and confirm the title heading, bullets/numbers/checkboxes, and bold/italic render.
  4. Import… → pick a .txt/.md → a new page appears in the current section with the file's text; the page title is the filename.
  5. Reset settings clears the backup folder/interval/keep but does NOT wipe the "Last backup" timestamp.
