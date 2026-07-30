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
        Assert.Empty(restored.Paragraphs[1].Runs);
        Assert.True(restored.RangeAll(new DocPos(0, 6), new DocPos(0, 10), r => r.Bold));
        Assert.True(restored.RangeAll(new DocPos(2, 0), new DocPos(2, 6), r => r.Italic));
    }

    [Fact]
    public void RoundTrip_preservesTags()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "urgent\nnormal");
        doc.SetTag(new DocPos(0, 0), new DocPos(0, 0), "important");

        var restored = RichDocJson.FromJson(RichDocJson.ToJson(doc));

        Assert.Equal("important", restored.Paragraphs[0].Tag);
        Assert.Null(restored.Paragraphs[1].Tag);
    }

    [Fact]
    public void RoundTrip_preservesBulletsAndCheckedState()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "buy milk\nwalk dog");
        doc.SetBullet(new DocPos(0, 0), new DocPos(1, 0), "check");
        doc.ToggleChecked(0);

        var restored = RichDocJson.FromJson(RichDocJson.ToJson(doc));

        Assert.Equal("check", restored.Paragraphs[0].Bullet);
        Assert.True(restored.Paragraphs[0].Checked);
        Assert.Equal("check", restored.Paragraphs[1].Bullet);
        Assert.False(restored.Paragraphs[1].Checked);
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
            store.Save(ws);

            var canvas = new CanvasDocument();
            var box = canvas.AddBox(24, 48, 400);
            box.Doc.InsertText(new DocPos(0, 0), "photosynthesis notes", bold: true);
            store.SavePageDoc(nb, "page1", canvas);

            var loaded = store.LoadPageDoc(nb, "page1");
            Assert.NotNull(loaded);
            var lb = Assert.Single(loaded!.Boxes);
            Assert.Equal(24, lb.X);
            Assert.Equal(48, lb.Y);
            Assert.Equal(400, lb.Width);
            Assert.Equal("photosynthesis notes", lb.Doc.GetText());
            Assert.True(lb.Doc.RangeAll(new DocPos(0, 0), lb.Doc.End, r => r.Bold));

            Assert.Null(store.LoadPageDoc(nb, "missing"));

            store.DeletePageDoc(nb, "page1");
            Assert.Null(store.LoadPageDoc(nb, "page1"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LockedBox_roundTripsThroughJson()
    {
        var canvas = new CanvasDocument();
        canvas.AddBox(10, 20, 300).Locked = true;
        canvas.AddBox(40, 400, 300);

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));

        Assert.Equal(2, reloaded.Boxes.Count);
        Assert.True(reloaded.Boxes[0].Locked);
        Assert.False(reloaded.Boxes[1].Locked);

        Assert.DoesNotContain("\"lk\":false", CanvasDocJson.ToJson(canvas));
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
            var canvas = vm1.DocumentFor(page);
            canvas.AddBox(10, 20).Doc.InsertText(new DocPos(0, 0), "remember me");
            vm1.FlushDirtyDocs();

            var vm2 = new MainViewModel(new WorkspaceStore(dir));
            var reloaded = vm2.DocumentFor(vm2.SelectedPage!);
            var box = Assert.Single(reloaded.Boxes);
            Assert.Equal(10, box.X);
            Assert.Equal("remember me", box.Doc.GetText());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
