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
}
