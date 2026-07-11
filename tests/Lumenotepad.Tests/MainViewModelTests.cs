using System.IO;
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
}
