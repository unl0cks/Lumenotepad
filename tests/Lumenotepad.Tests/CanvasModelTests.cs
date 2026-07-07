using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class CanvasModelTests
{
    [Fact]
    public void AddBox_CommitGeometry_RemoveBox_allRaiseChanged()
    {
        var canvas = new CanvasDocument();
        int n = 0;
        canvas.Changed += () => n++;

        var box = canvas.AddBox(10, 20);
        Assert.Equal(1, n);
        canvas.CommitGeometry();
        Assert.Equal(2, n);
        canvas.RemoveBox(box);
        Assert.Equal(3, n);
    }

    [Fact]
    public void EditsInsideABoxDoc_bubbleToCanvasChanged()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(0, 0);
        int n = 0;
        canvas.Changed += () => n++;

        box.Doc.InsertText(new DocPos(0, 0), "hi");

        Assert.Equal(1, n);
    }

    [Fact]
    public void RemovedBox_editsNoLongerBubble()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(0, 0);
        canvas.RemoveBox(box);
        int n = 0;
        canvas.Changed += () => n++;

        box.Doc.InsertText(new DocPos(0, 0), "orphan");

        Assert.Equal(0, n);
    }

    [Fact]
    public void AddBox_clampsGeometryToSaneValues()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(-50, -9, width: 10);

        Assert.Equal(0, box.X);
        Assert.Equal(0, box.Y);
        Assert.Equal(NoteBox.MinWidth, box.Width);
    }

    [Fact]
    public void IsEmpty_reflectsContent_includingBareBullets()
    {
        var box = new NoteBox();
        Assert.True(box.IsEmpty);

        var end = box.Doc.InsertText(new DocPos(0, 0), "x");
        Assert.False(box.IsEmpty);

        box.Doc.DeleteRange(new DocPos(0, 0), end);
        Assert.True(box.IsEmpty);

        box.Doc.SetBullet(new DocPos(0, 0), new DocPos(0, 0), "dot");   // a bare bullet is content too
        Assert.False(box.IsEmpty);
    }
}

public class CanvasJsonTests
{
    [Fact]
    public void V2_roundTrip_preservesGeometryAndFormatting()
    {
        var canvas = new CanvasDocument();
        var a = canvas.AddBox(12, 34, 400);
        a.Doc.InsertText(new DocPos(0, 0), "alpha", bold: true);
        var b = canvas.AddBox(300, 500, 240);
        b.Doc.InsertText(new DocPos(0, 0), "beta\ngamma");
        b.Doc.SetBullet(new DocPos(0, 0), new DocPos(1, 0), "star");

        var restored = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));

        Assert.Equal(2, restored.Boxes.Count);
        Assert.Equal(12, restored.Boxes[0].X);
        Assert.Equal(34, restored.Boxes[0].Y);
        Assert.Equal(400, restored.Boxes[0].Width);
        Assert.Equal("alpha", restored.Boxes[0].Doc.GetText());
        Assert.True(restored.Boxes[0].Doc.RangeAll(new DocPos(0, 0), restored.Boxes[0].Doc.End, r => r.Bold));
        Assert.Equal("beta\ngamma", restored.Boxes[1].Doc.GetText());
        Assert.Equal("star", restored.Boxes[1].Doc.Paragraphs[0].Bullet);
    }

    [Fact]
    public void V1_pageFile_migratesToOneWideBoxAtOrigin()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "legacy notes", bold: true);
        var v1 = RichDocJson.ToJson(doc);

        var canvas = CanvasDocJson.FromJson(v1);

        var box = Assert.Single(canvas.Boxes);
        Assert.Equal(0, box.X);
        Assert.Equal(0, box.Y);
        Assert.Equal(CanvasDocJson.MigratedBoxWidth, box.Width);
        Assert.Equal("legacy notes", box.Doc.GetText());
        Assert.True(box.Doc.RangeAll(new DocPos(0, 0), box.Doc.End, r => r.Bold));
    }

    [Fact]
    public void V1_emptyDoc_migratesToNoBoxes()
    {
        var v1 = RichDocJson.ToJson(new RichDocument());
        Assert.Empty(CanvasDocJson.FromJson(v1).Boxes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{half json")]
    public void BadInput_yieldsEmptyCanvas(string? json)
    {
        Assert.Empty(CanvasDocJson.FromJson(json).Boxes);
    }
}
