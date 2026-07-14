using System.IO;
using System.Linq;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;
using Xunit;

namespace Lumenotepad.Tests;

public class MainViewModelTests
{
    private static MainViewModel NewVm(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        return new MainViewModel(new WorkspaceStore(dir));
    }

    [Fact]
    public void Ctor_seedsAndSelectsFirstNotebookSectionPage()
    {
        var vm = NewVm(out var dir);
        try
        {
            Assert.Single(vm.Notebooks);
            Assert.NotNull(vm.SelectedNotebook);
            Assert.NotNull(vm.SelectedSection);
            Assert.Equal("Welcome", vm.SelectedPage?.Title);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AddNotebook_addsSelectsAndCascadesToPage()
    {
        var vm = NewVm(out var dir);
        try
        {
            vm.AddNotebookCommand.Execute(null);
            Assert.Equal(2, vm.Notebooks.Count);
            Assert.Same(vm.Notebooks[1], vm.SelectedNotebook);
            Assert.NotNull(vm.SelectedSection);   // cascaded into the new notebook's first section
            Assert.NotNull(vm.SelectedPage);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Homepage_launchesVisible_openAndReturnNavigate()
    {
        var vm = NewVm(out var dir);
        try
        {
            Assert.True(vm.IsHomeVisible);                   // launch lands on the gallery

            var nb = vm.Notebooks[0];
            vm.OpenNotebookCommand.Execute(nb);
            Assert.False(vm.IsHomeVisible);
            Assert.Same(nb, vm.SelectedNotebook);

            vm.GoHomeCommand.Execute(null);
            Assert.True(vm.IsHomeVisible);

            vm.AddNotebookCommand.Execute(null);             // a fresh notebook opens right away
            Assert.False(vm.IsHomeVisible);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetAndClearNotebookCover_copiesFileAndPersists()
    {
        var vm = NewVm(out var dir);
        try
        {
            var src = Path.Combine(dir, "photo.png");
            File.WriteAllBytes(src, new byte[] { 1, 2, 3 });

            vm.SetNotebookCover(vm.Notebooks[0], src);
            Assert.Equal("cover.png", vm.Notebooks[0].Cover);
            Assert.True(File.Exists(vm.Notebooks[0].CoverPath));

            var vm2 = new MainViewModel(new WorkspaceStore(dir));
            Assert.Equal("cover.png", vm2.Notebooks[0].Cover);
            Assert.True(File.Exists(vm2.Notebooks[0].CoverPath));   // hydrated on load

            vm2.ClearNotebookCover(vm2.Notebooks[0]);
            Assert.Equal("", vm2.Notebooks[0].Cover);
            Assert.Null(vm2.Notebooks[0].CoverPath);
            var vm3 = new MainViewModel(new WorkspaceStore(dir));
            Assert.Null(vm3.Notebooks[0].CoverPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetNotebookColor_updatesAndPersists()
    {
        var vm = NewVm(out var dir);
        try
        {
            vm.SetNotebookColor(vm.Notebooks[0], "#E27BA6");
            var vm2 = new MainViewModel(new WorkspaceStore(dir));
            Assert.Equal("#E27BA6", vm2.Notebooks[0].Color);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeletePage_selectsNeighbor()
    {
        var vm = NewVm(out var dir);
        try
        {
            vm.AddPageCommand.Execute(null);                 // 2 pages; selected = the new one
            var first = vm.SelectedSection!.Pages[0];
            var second = vm.SelectedPage!;
            vm.DeletePageCommand.Execute(second);
            Assert.Single(vm.SelectedSection.Pages);
            Assert.Same(first, vm.SelectedPage);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StartVisiblePrefs_SeedLiveState_AndMirrorOnChange()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { StartRailVisible = false, StartPagesVisible = false }.Save(dir);

            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.False(vm.IsRailVisible);      // ctor seeds live state from the persisted pref
            Assert.False(vm.IsPagesVisible);

            vm.StartRailVisible = true;          // flipping the pref mirrors onto the live state
            Assert.True(vm.IsRailVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResetSettingsToDefaults_RestoresDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.Theme = "Pink";
            vm.FlatCovers = true;
            vm.AdvancedUnlocked = true;
            vm.StartRailVisible = false;

            vm.ResetSettingsToDefaults();

            Assert.Equal("Lumen", vm.Theme);
            Assert.False(vm.FlatCovers);
            Assert.False(vm.AdvancedUnlocked);
            Assert.True(vm.StartRailVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void BulletColor_SetPersistClearAndReset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Null(vm.BulletColorFor("star"));

            vm.SetBulletColor("star", "#FF0000");
            Assert.Equal("#FF0000", vm.BulletColorFor("star"));
            Assert.Equal("#FF0000", AppSettings.Load(dir).BulletColors["star"]);   // persisted

            vm.SetBulletColor("star", null);
            Assert.Null(vm.BulletColorFor("star"));

            vm.SetBulletColor("heart", "#00FF00");
            vm.NumBoldDefault = true;
            vm.ResetSettingsToDefaults();
            Assert.Null(vm.BulletColorFor("heart"));
            Assert.Null(vm.NumBoldDefault);
            Assert.Empty(AppSettings.Load(dir).BulletColors);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FontEnablement_PersistsAndResets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.True(vm.IsFontEnabled("Impact"));

            vm.SetFontEnabled("Impact", false);
            Assert.False(vm.IsFontEnabled("impact"));                        // case-insensitive
            Assert.Contains("Impact", AppSettings.Load(dir).DisabledFonts);  // persisted

            vm.SetFontEnabled("IMPACT", true);
            Assert.True(vm.IsFontEnabled("Impact"));
            Assert.Empty(AppSettings.Load(dir).DisabledFonts);

            vm.SetFontEnabled("Georgia", false);
            vm.ResetSettingsToDefaults();
            Assert.True(vm.IsFontEnabled("Georgia"));
            Assert.Empty(AppSettings.Load(dir).DisabledFonts);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LaunchTarget_LastPage_LandsInEditorOnMatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var seed = new MainViewModel(new WorkspaceStore(dir), dir);
            var page = seed.SelectedPage!;
            new AppSettings { LaunchTarget = "LastPage", LastPageId = page.Id }.Save(dir);

            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.False(vm.IsHomeVisible);
            Assert.Equal(page.Id, vm.SelectedPage?.Id);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LaunchTarget_LastPage_FallsBackToHomeWhenGone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { LaunchTarget = "LastPage", LastPageId = "no-such-page" }.Save(dir);
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.True(vm.IsHomeVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void SelectingAPage_TracksLastPageId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var id = vm.SelectedPage!.Id;
            Assert.Equal(id, AppSettings.Load(dir).LastPageId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PersistedNonDefaultRecentCount_DoesNotCrashConstruction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { RecentCount = 8 }.Save(dir);
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);   // must not throw
            Assert.Equal(8, vm.RecentCount);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Greeting_PersonalizesAndRefreshes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { UserName = "Sam", ShowHomeStats = false }.Save(dir);
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);   // must not crash (ctor gotcha)
            Assert.Contains(", Sam —", vm.Greeting);
            Assert.DoesNotContain("notebook", vm.HomeSubtitle);

            vm.UserName = "";
            Assert.DoesNotContain(",", vm.Greeting.Split('—')[0]);      // plain greeting again

            vm.ShowHomeStats = true;
            Assert.Contains("notebook", vm.HomeSubtitle);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Palettes_SeedEditResetRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var builtIns = new[] { "#AAA111", "#BBB222" };
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Equal(builtIns, vm.PaletteFor(highlight: false, builtIns));

            vm.AddPaletteColor(false, "#CCC333", builtIns);
            Assert.Equal(new[] { "#AAA111", "#BBB222", "#CCC333" }, vm.PaletteFor(false, builtIns));
            Assert.Equal(3, AppSettings.Load(dir).TextPalette.Count);      // seeded + persisted

            vm.RemovePaletteColor(false, "#aaa111", builtIns);             // case-insensitive
            Assert.Equal(new[] { "#BBB222", "#CCC333" }, vm.PaletteFor(false, builtIns));

            vm.RemovePaletteColor(false, "#BBB222", builtIns);
            vm.RemovePaletteColor(false, "#CCC333", builtIns);             // last chip survives
            Assert.Single(vm.PaletteFor(false, builtIns));

            vm.ResetPalette(false);
            Assert.Equal(builtIns, vm.PaletteFor(false, builtIns));
            Assert.Empty(AppSettings.Load(dir).TextPalette);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResetSettingsToDefaults_restoresPaperGridPrefs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.PageGrid = "Lines";
            vm.GridSnap = true;

            vm.ResetSettingsToDefaults();

            Assert.Equal("None", vm.PageGrid);
            Assert.False(vm.GridSnap);
            var persisted = AppSettings.Load(dir);
            Assert.Equal("None", persisted.PageGrid);
            Assert.False(persisted.GridSnap);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetNotebookPaperTint_setsAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var nb = vm.Notebooks[0];

            vm.SetNotebookPaperTint(nb, "#8FC2EC");

            Assert.Equal("#8FC2EC", nb.PaperTint);
            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Equal("#8FC2EC", reloaded.Notebooks[0].PaperTint);   // Save() ran

            vm.SetNotebookPaperTint(nb, null);
            Assert.Null(nb.PaperTint);
        }
        finally { Directory.Delete(dir, true); }
    }

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

    [Fact]
    public void ResetSettingsToDefaults_restoresTrayPrefs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.CloseToTray = true;
            vm.MinimizeToTray = true;
            vm.SummonHotkey = true;

            vm.ResetSettingsToDefaults();

            Assert.False(vm.CloseToTray);
            Assert.False(vm.MinimizeToTray);
            Assert.False(vm.SummonHotkey);
            var persisted = AppSettings.Load(dir);
            Assert.False(persisted.CloseToTray);
            Assert.False(persisted.MinimizeToTray);
            Assert.False(persisted.SummonHotkey);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResetSettingsToDefaults_restoresPagesPanelWidth()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.PagesPanelWidth = 320;
            vm.ResetSettingsToDefaults();
            Assert.Equal(224, vm.PagesPanelWidth);
            Assert.Equal(224, AppSettings.Load(dir).PagesPanelWidth, 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResetSettingsToDefaults_restoresDoubleClickCreate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.DoubleClickCreate = true;
            vm.ResetSettingsToDefaults();
            Assert.False(vm.DoubleClickCreate);
            Assert.False(AppSettings.Load(dir).DoubleClickCreate);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AddPage_stampsStarters_whenNotebookDefaultsToAStyle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.SelectedNotebook!.DefaultPageStyle = "Cornell";

            vm.AddPageCommand.Execute(null);

            var doc = vm.DocumentFor(vm.SelectedPage!);
            Assert.Equal(3, doc.Boxes.Count);                   // Cue / Notes / Summary
            Assert.Equal("Cue", doc.Boxes[0].Doc.GetText());

            vm.SelectedNotebook!.DefaultPageStyle = "Freeform";
            vm.AddPageCommand.Execute(null);
            Assert.Empty(vm.DocumentFor(vm.SelectedPage!).Boxes);   // Freeform stamps nothing
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetPageStyleChoice_persists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var pg = vm.SelectedPage!;

            vm.SetPageStyleChoice(pg, "Boxing", 2);
            vm.SetPageGridStyle(pg, "Ruled");

            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);
            var rp = reloaded.Notebooks[0].Sections[0].Pages.First(p => p.Id == pg.Id);
            Assert.Equal("Boxing", rp.PageStyle);
            Assert.Equal(2, rp.PageStyleMode);
            Assert.Equal("Ruled", rp.GridStyle);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CreateNotebook_buildsTree_stampsStyledPages_andOpens()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var draft = NotebookDraft.New();
            draft.Name = "Biology";
            draft.Color = "#3E9C6B";
            draft.DefaultPageStyle = "Cornell";
            draft.DefaultFont = "Caveat";
            draft.DefaultFontSize = 18;
            draft.Sections.Clear();
            draft.Sections.Add(new SectionDraft { Name = "Cells", PageTitles = { "Structure", "Mitosis" } });
            draft.Sections.Add(new SectionDraft { Name = "Genetics", PageTitles = { "Mendel" } });

            var nb = vm.CreateNotebook(draft);

            Assert.Equal("Biology", nb.Name);
            Assert.Equal("#3E9C6B", nb.Color);
            Assert.Equal("Cornell", nb.DefaultPageStyle);
            Assert.Equal("Caveat", nb.DefaultFont);
            Assert.Equal(18, nb.DefaultFontSize);
            Assert.Equal(2, nb.Sections.Count);
            Assert.Equal(new[] { "Structure", "Mitosis" }, nb.Sections[0].Pages.Select(p => p.Title).ToArray());
            Assert.Single(nb.Sections[1].Pages);
            Assert.Equal(3, vm.DocumentFor(nb.Sections[0].Pages[0]).Boxes.Count);   // Cornell starters
            Assert.Same(nb, vm.SelectedNotebook);
            Assert.False(vm.IsHomeVisible);

            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);         // persisted
            var rnb = reloaded.Notebooks.First(n => n.Name == "Biology");
            Assert.Equal("Cornell", rnb.DefaultPageStyle);
            Assert.Equal(2, rnb.Sections.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CreateNotebook_guardsBlanks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var draft = NotebookDraft.New();
            draft.Name = "  ";
            draft.Sections.Clear();                                     // no sections at all

            var nb = vm.CreateNotebook(draft);

            Assert.Equal("New notebook", nb.Name);
            var sec = Assert.Single(nb.Sections);                       // seeded fallback section
            Assert.Equal("Notes", sec.Name);
            Assert.Empty(vm.DocumentFor(sec.Pages[0]).Boxes);           // Freeform default: no starters
        }
        finally { Directory.Delete(dir, true); }
    }
}
