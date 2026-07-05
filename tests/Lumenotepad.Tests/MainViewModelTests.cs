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
