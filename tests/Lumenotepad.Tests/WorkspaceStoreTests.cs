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

    [Fact]
    public void PaperTint_roundTrips()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            ws.Notebooks.Add(new Notebook { Name = "Tinted", PaperTint = "#E8D9A8" });
            ws.Notebooks.Add(new Notebook { Name = "Plain" });

            store.Save(ws);
            var loaded = new WorkspaceStore(dir).Load();

            Assert.Equal("#E8D9A8", loaded.Notebooks[0].PaperTint);
            Assert.Null(loaded.Notebooks[1].PaperTint);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PageAndNotebookStyles_roundTrip()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            var nb = new Notebook
            {
                Name = "Styled", DefaultGridStyle = "Ruled", DefaultPageStyle = "Cornell",
                DefaultPageStyleMode = 2, DefaultFont = "Caveat", DefaultFontSize = 18,
            };
            var sec = new Section { Name = "S" };
            sec.Pages.Add(new Page { Title = "P", GridStyle = "Dots", PageStyle = "Boxing", PageStyleMode = 1 });
            sec.Pages.Add(new Page { Title = "Plain" });
            nb.Sections.Add(sec);
            ws.Notebooks.Add(nb);

            store.Save(ws);
            var loaded = new WorkspaceStore(dir).Load();

            var lnb = loaded.Notebooks[0];
            Assert.Equal("Ruled", lnb.DefaultGridStyle);
            Assert.Equal("Cornell", lnb.DefaultPageStyle);
            Assert.Equal(2, lnb.DefaultPageStyleMode);
            Assert.Equal("Caveat", lnb.DefaultFont);
            Assert.Equal(18, lnb.DefaultFontSize);
            var p0 = lnb.Sections[0].Pages[0];
            Assert.Equal("Dots", p0.GridStyle);
            Assert.Equal("Boxing", p0.PageStyle);
            Assert.Equal(1, p0.PageStyleMode);
            var p1 = lnb.Sections[0].Pages[1];
            Assert.Null(p1.GridStyle);
            Assert.Null(p1.PageStyle);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
