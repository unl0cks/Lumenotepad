using System.IO;
using Lumenotepad.Models;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class WorkspaceStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "lnp-ws-" + Path.GetRandomFileName());

    [Fact]
    public void SaveThenLoad_roundTripsTreeAndOrder()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            var bio = new Notebook { Name = "Biology", Color = "#3E9C6B" };
            var cells = new Section { Name = "Cells" };
            cells.Pages.Add(new Page { Title = "Photosynthesis" });
            cells.Pages.Add(new Page { Title = "Mitosis" });
            bio.Sections.Add(cells);
            var work = new Notebook { Name = "Work", Color = "#4DA6FF" };
            work.Sections.Add(new Section { Name = "Cases" });
            ws.Notebooks.Add(bio);
            ws.Notebooks.Add(work);

            store.Save(ws);
            var loaded = new WorkspaceStore(dir).Load();

            Assert.Equal(2, loaded.Notebooks.Count);
            Assert.Equal("Biology", loaded.Notebooks[0].Name);
            Assert.Equal("Work", loaded.Notebooks[1].Name);          // order preserved
            Assert.Equal("#3E9C6B", loaded.Notebooks[0].Color);
            Assert.Equal("Cells", loaded.Notebooks[0].Sections[0].Name);
            Assert.Equal(2, loaded.Notebooks[0].Sections[0].Pages.Count);
            Assert.Equal("Photosynthesis", loaded.Notebooks[0].Sections[0].Pages[0].Title);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadOrSeed_onEmpty_createsDefault_andPersists()
    {
        var dir = TempDir();
        try
        {
            var seeded = new WorkspaceStore(dir).LoadOrSeed();
            Assert.Single(seeded.Notebooks);
            Assert.Equal("My Notebook", seeded.Notebooks[0].Name);
            Assert.Equal("Welcome", seeded.Notebooks[0].Sections[0].Pages[0].Title);

            var reloaded = new WorkspaceStore(dir).Load();   // persisted, not re-seeded
            Assert.Single(reloaded.Notebooks);
            Assert.Equal("My Notebook", reloaded.Notebooks[0].Name);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeleteNotebook_removesFolder_andPersists()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            var a = new Notebook { Name = "Alpha" };
            var b = new Notebook { Name = "Beta" };
            ws.Notebooks.Add(a);
            ws.Notebooks.Add(b);
            store.Save(ws);

            store.DeleteNotebook(a);
            ws.Notebooks.Remove(a);
            store.Save(ws);

            var loaded = new WorkspaceStore(dir).Load();
            Assert.Single(loaded.Notebooks);
            Assert.Equal("Beta", loaded.Notebooks[0].Name);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
