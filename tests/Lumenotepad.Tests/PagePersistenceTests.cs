using System.IO;
using Lumenotepad.Editor;
using Lumenotepad.Models;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;
using Xunit;

namespace Lumenotepad.Tests;

public class RichDocJsonTests
{
    [Fact]
    public void RoundTrip_preservesTextFormattingAndEmptyParagraphs()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "plain ");
        doc.InsertText(doc.End, "bold", bold: true);
        doc.InsertText(doc.End, "\n\nitalic tail");
        doc.ApplyFormat(new DocPos(2, 0), new DocPos(2, 6), r => r.Italic = true);

        var restored = RichDocJson.FromJson(RichDocJson.ToJson(doc));

        Assert.Equal(doc.GetText(), restored.GetText());
        Assert.Equal(3, restored.Paragraphs.Count);
        Assert.Empty(restored.Paragraphs[1].Runs);                       // empty paragraph survives
        Assert.True(restored.RangeAll(new DocPos(0, 6), new DocPos(0, 10), r => r.Bold));
        Assert.True(restored.RangeAll(new DocPos(2, 0), new DocPos(2, 6), r => r.Italic));
    }

    [Fact]
    public void RoundTrip_preservesFullFormatSet()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "styled", new RunFormat(
            Bold: true, Italic: false, Underline: true, Strike: true,
            Highlight: "#66FFD666", Color: "#FF8FAB", Size: 22, Font: "Caveat"));

        var restored = RichDocJson.FromJson(RichDocJson.ToJson(doc));
        var run = restored.Paragraphs[0].Runs[0];

        Assert.True(run.Bold);
        Assert.True(run.Underline);
        Assert.True(run.Strike);
        Assert.Equal("#66FFD666", run.Highlight);
        Assert.Equal("#FF8FAB", run.Color);
        Assert.Equal(22, run.Size);
        Assert.Equal("Caveat", run.Font);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{corrupt json!!")]
    public void FromJson_badInput_yieldsFreshEmptyDoc(string? json)
    {
        var doc = RichDocJson.FromJson(json);
        Assert.Single(doc.Paragraphs);
        Assert.Equal("", doc.GetText());
    }
}

public class PageDocStoreTests
{
    [Fact]
    public void SaveLoadDelete_pageDoc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-pd-" + Path.GetRandomFileName());
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            var nb = new Notebook { Name = "Biology" };
            ws.Notebooks.Add(nb);
            store.Save(ws);                                   // assigns nb.Folder

            var doc = new RichDocument();
            doc.InsertText(new DocPos(0, 0), "photosynthesis notes", bold: true);
            store.SavePageDoc(nb, "page1", doc);

            var loaded = store.LoadPageDoc(nb, "page1");
            Assert.NotNull(loaded);
            Assert.Equal("photosynthesis notes", loaded!.GetText());
            Assert.True(loaded.RangeAll(new DocPos(0, 0), loaded.End, r => r.Bold));

            Assert.Null(store.LoadPageDoc(nb, "missing"));

            store.DeletePageDoc(nb, "page1");
            Assert.Null(store.LoadPageDoc(nb, "page1"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class VmPersistenceTests
{
    [Fact]
    public void TypedContent_persistsAcrossVmInstances_afterFlush()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vmp-" + Path.GetRandomFileName());
        try
        {
            var vm1 = new MainViewModel(new WorkspaceStore(dir));
            var page = vm1.SelectedPage!;
            var doc = vm1.DocumentFor(page);
            doc.InsertText(new DocPos(0, 0), "remember me");
            vm1.FlushDirtyDocs();

            var vm2 = new MainViewModel(new WorkspaceStore(dir));
            var reloaded = vm2.DocumentFor(vm2.SelectedPage!);
            Assert.Equal("remember me", reloaded.GetText());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
