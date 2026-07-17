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
    public void DeleteToTrash_and_Restore_moveBoxesAndRehookEdits()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddBox(50, 60);
        box.Doc.InsertText(new DocPos(0, 0), "keep me");

        canvas.DeleteToTrash(box);
        Assert.Empty(canvas.Boxes);
        Assert.Same(box, Assert.Single(canvas.Trash));

        int n = 0;
        canvas.Changed += () => n++;
        box.Doc.InsertText(box.Doc.End, "!");           // edits while trashed don't bubble
        Assert.Equal(0, n);

        canvas.RestoreFromTrash(box, 200, 300);
        Assert.Equal(1, n);                             // the restore itself raises Changed
        Assert.Empty(canvas.Trash);
        Assert.Equal(200, Assert.Single(canvas.Boxes).X);

        box.Doc.InsertText(box.Doc.End, "?");           // edits bubble again after restore
        Assert.Equal(2, n);
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
    public void V2_roundTrip_preservesTrashAndHeightFloor()
    {
        var canvas = new CanvasDocument();
        var live = canvas.AddBox(0, 0, 300);
        live.H = 250;
        live.Doc.InsertText(new DocPos(0, 0), "alive");
        var dead = canvas.AddBox(40, 40);
        dead.Doc.InsertText(new DocPos(0, 0), "deleted");
        canvas.DeleteToTrash(dead);

        var restored = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));

        Assert.Equal(250, Assert.Single(restored.Boxes).H);
        var trashed = Assert.Single(restored.Trash);
        Assert.Equal("deleted", trashed.Doc.GetText());

        restored.RestoreFromTrash(trashed);              // reloaded trash restores cleanly
        Assert.Equal(2, restored.Boxes.Count);
        Assert.Empty(restored.Trash);
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

    // ---- mindmap links (M9 Part 5) ----

    [Fact]
    public void ToggleLink_linksThenUnlinks_firesChanged_ignoresSelf()
    {
        var canvas = new CanvasDocument();
        var a = canvas.AddBox(0, 0);
        var b = canvas.AddBox(400, 0);
        int changed = 0;
        canvas.Changed += () => changed++;

        Assert.False(canvas.ToggleLink(a, a));           // self-link refused
        Assert.Empty(canvas.Links);

        Assert.True(canvas.ToggleLink(a, b));
        Assert.Single(canvas.Links);
        Assert.False(canvas.ToggleLink(b, a));           // same pair, either order → unlink
        Assert.Empty(canvas.Links);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void RemoveOrTrashBox_dropsItsLinks()
    {
        var canvas = new CanvasDocument();
        var a = canvas.AddBox(0, 0);
        var b = canvas.AddBox(400, 0);
        var c = canvas.AddBox(0, 400);
        canvas.ToggleLink(a, b);
        canvas.ToggleLink(b, c);
        canvas.ToggleLink(a, c);

        canvas.DeleteToTrash(b);
        Assert.Single(canvas.Links);                     // only a—c survives
        Assert.True(ReferenceEquals(canvas.Links[0].A, a) && ReferenceEquals(canvas.Links[0].B, c));

        canvas.RemoveBox(a);
        Assert.Empty(canvas.Links);
    }

    [Fact]
    public void Links_roundTripAsIndexPairs()
    {
        var canvas = new CanvasDocument();
        var a = canvas.AddBox(0, 0);
        a.Doc.InsertText(new DocPos(0, 0), "center");
        var b = canvas.AddBox(400, 0);
        b.Doc.InsertText(new DocPos(0, 0), "branch");
        canvas.AddBox(0, 400);                           // unlinked third box
        canvas.ToggleLink(a, b);

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));

        var link = Assert.Single(reloaded.Links);
        Assert.Equal("center", link.A.Doc.GetText());
        Assert.Equal("branch", link.B.Doc.GetText());
    }

    [Fact]
    public void ImageBox_notEmpty_roundTrips()
    {
        var canvas = new CanvasDocument();
        var img = canvas.AddBox(20, 30, 300);
        img.ImagePath = "images/pic.png";
        Assert.False(img.IsEmpty);                       // an image box never evaporates

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));
        var box = Assert.Single(reloaded.Boxes);
        Assert.Equal("images/pic.png", box.ImagePath);
        Assert.Equal(20, box.X);
        Assert.Equal(300, box.Width);
    }

    [Fact]
    public void DividerBox_notEmpty_roundTripsUnderMinWidth()
    {
        var canvas = new CanvasDocument();
        var div = canvas.AddBox(50, 80);
        div.Divider = "v";
        div.Width = 22;                                  // vertical strips sit under NoteBox.MinWidth
        div.H = 240;
        Assert.False(div.IsEmpty);                       // a divider box never evaporates

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));
        var box = Assert.Single(reloaded.Boxes);
        Assert.Equal("v", box.Divider);
        Assert.Equal(22, box.Width);                     // load must not clamp back to MinWidth
        Assert.Equal(240, box.H);
    }

    [Fact]
    public void AttachmentBox_notEmpty_roundTrips()
    {
        var canvas = new CanvasDocument();
        var att = canvas.AddBox(10, 20, 260);
        att.AttachPath = "assets/report (2).pdf";
        Assert.False(att.IsEmpty);                       // an attachment box never evaporates

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));
        var box = Assert.Single(reloaded.Boxes);
        Assert.Equal("assets/report (2).pdf", box.AttachPath);
    }

    [Fact]
    public void Table_create_insertRemove_respectsBounds()
    {
        var t = NoteTable.Create(2, 3);
        Assert.Equal(2, t.RowCount);
        Assert.Equal(3, t.ColCount);

        t.InsertRow(-1);
        Assert.Equal(3, t.RowCount);
        Assert.Equal(3, t.Rows[2].Count);       // new row matches column count

        t.InsertColumn(0);
        Assert.Equal(4, t.ColCount);
        Assert.All(t.Rows, r => Assert.Equal(4, r.Count));

        t.RemoveColumn(0);
        Assert.Equal(3, t.ColCount);

        // Can't empty the table.
        var single = NoteTable.Create(1, 1);
        single.RemoveRow(0);
        single.RemoveColumn(0);
        Assert.Equal(1, single.RowCount);
        Assert.Equal(1, single.ColCount);
    }

    [Fact]
    public void TableBox_cellEdit_andStructuralChange_fireCanvasChanged()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddTableBox(0, 0, 2, 2);
        int changes = 0;
        canvas.Changed += () => changes++;

        box.Table!.Rows[0][0].InsertText(new DocPos(0, 0), "hi");   // a cell edit
        Assert.True(changes >= 1);

        int before = changes;
        canvas.TableInsertRow(box, -1);                            // a structural edit
        Assert.True(changes > before);

        // A cell in the newly added row is also wired.
        int mid = changes;
        box.Table.Rows[2][0].InsertText(new DocPos(0, 0), "x");
        Assert.True(changes > mid);
    }

    [Fact]
    public void TableBox_notEmpty_roundTripsThroughJson()
    {
        var canvas = new CanvasDocument();
        var box = canvas.AddTableBox(30, 40, 2, 2, 300);
        box.Table!.Rows[0][0].InsertText(new DocPos(0, 0), "A1");
        box.Table.Rows[1][1].InsertText(new DocPos(0, 0), "B2");
        Assert.False(box.IsEmpty);

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));
        var r = Assert.Single(reloaded.Boxes);
        Assert.NotNull(r.Table);
        Assert.Equal(2, r.Table!.RowCount);
        Assert.Equal(2, r.Table.ColCount);
        Assert.Equal("A1", r.Table.Rows[0][0].GetText());
        Assert.Equal("B2", r.Table.Rows[1][1].GetText());

        // The reloaded table's cells must be wired for autosave too.
        int changes = 0;
        reloaded.Changed += () => changes++;
        r.Table.Rows[0][1].InsertText(new DocPos(0, 0), "edit");
        Assert.True(changes >= 1);
    }

    [Fact]
    public void Links_absentWhenNone_badPairsIgnored()
    {
        var canvas = new CanvasDocument();
        canvas.AddBox(0, 0);
        Assert.DoesNotContain("\"links\"", CanvasDocJson.ToJson(canvas));

        var json = "{\"v\":2,\"boxes\":[{\"x\":0,\"y\":0,\"w\":360,\"paras\":[]}]," +
                   "\"links\":[[0,5],[0,0],[1],[0,-1]]}";
        Assert.Empty(CanvasDocJson.FromJson(json).Links); // out-of-range/self/short pairs all dropped
    }
}
