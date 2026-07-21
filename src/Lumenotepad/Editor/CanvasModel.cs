using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Editor;

/// <summary>A grid of rich-text cells (M11 tables). Row-major: <c>Rows[r][c]</c> is one cell's
/// document. Pure model — the view builds a mini editor per cell. Structural edits go through the
/// owning <see cref="CanvasDocument"/> so cell-document change subscriptions stay in sync.</summary>
public sealed class NoteTable
{
    public const int MaxRows = 40, MaxCols = 12;

    public List<List<RichDocument>> Rows { get; } = new();

    public int RowCount => Rows.Count;
    public int ColCount => Rows.Count == 0 ? 0 : Rows[0].Count;

    /// <summary>Every cell document, row-major (what the canvas subscribes for autosave).</summary>
    public IEnumerable<RichDocument> AllCells => Rows.SelectMany(r => r);

    public static NoteTable Create(int rows, int cols)
    {
        var t = new NoteTable();
        for (int r = 0; r < Math.Clamp(rows, 1, MaxRows); r++)
        {
            var row = new List<RichDocument>();
            for (int c = 0; c < Math.Clamp(cols, 1, MaxCols); c++) row.Add(new RichDocument());
            t.Rows.Add(row);
        }
        return t;
    }

    /// <summary>Insert a blank row at <paramref name="at"/> (clamped; -1 = append).</summary>
    public void InsertRow(int at)
    {
        if (RowCount >= MaxRows) return;
        int cols = Math.Max(1, ColCount);
        at = at < 0 || at > RowCount ? RowCount : at;
        Rows.Insert(at, Enumerable.Range(0, cols).Select(_ => new RichDocument()).ToList());
    }

    /// <summary>Insert a blank column at <paramref name="at"/> (clamped; -1 = append).</summary>
    public void InsertColumn(int at)
    {
        if (ColCount >= MaxCols) return;
        int c = at < 0 || at > ColCount ? ColCount : at;
        foreach (var row in Rows) row.Insert(c, new RichDocument());
    }

    /// <summary>Remove a row (no-op if it would empty the table).</summary>
    public void RemoveRow(int r)
    {
        if (RowCount <= 1 || r < 0 || r >= RowCount) return;
        Rows.RemoveAt(r);
    }

    /// <summary>Remove a column (no-op if it would empty the table).</summary>
    public void RemoveColumn(int c)
    {
        if (ColCount <= 1 || c < 0 || c >= ColCount) return;
        foreach (var row in Rows) row.RemoveAt(c);
    }
}

/// <summary>One movable note container on a page canvas: a rich document with a position, a width,
/// and an optional height floor (<see cref="H"/> = 0 means the height simply follows content;
/// dragging the bottom/corner handle sets a floor the box won't shrink under).
/// Pure model — no Avalonia dependencies.</summary>
public sealed class NoteBox
{
    public const double DefaultWidth = 360;
    public const double MinWidth = 140;
    public const double MinHeight = 42;
    /// <summary>Shortest a line-divider box can be stretched along its axis.</summary>
    public const double MinDividerLength = 60;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = DefaultWidth;
    public double H { get; set; }                    // 0 = auto (content height)

    /// <summary>Rigid page-style starters: a locked box cannot be moved, resized, or deleted, and
    /// never evaporates when empty (M9). Persisted.</summary>
    public bool Locked { get; set; }

    /// <summary>A DOCKED page-style region ("cue"/"notes"/"summary" for Cornell): the canvas keeps
    /// this box glued to the live guide geometry, so a structured page's boxes and its guide lines
    /// scale together and never drift apart on resize. Null for ordinary free boxes. Persisted.</summary>
    public string? Region { get; set; }

    /// <summary>A container's own accent colour ("#RRGGBB"), used to tint the card — the mind-map
    /// bubble colour, and the "colour category" that organizes a map. Null = the theme's default
    /// note look. Persisted.</summary>
    public string? Color { get; set; }

    /// <summary>An IMAGE box (M10): a path relative to the notebook folder ("images/xxx.png").
    /// When set the box renders the image instead of a text editor. Persisted.</summary>
    public string? ImagePath { get; set; }

    /// <summary>A LINE-DIVIDER box: "h" (horizontal rule) or "v" (vertical rule). When set the box
    /// renders a draggable line instead of a text editor, and only the along-axis resize handle
    /// shows — dragging it stretches the line. Persisted.</summary>
    public string? Divider { get; set; }

    /// <summary>A FILE-ATTACHMENT box (M11): a path relative to the notebook folder
    /// ("assets/report.pdf"). When set the box renders a file chip instead of a text editor;
    /// double-click opens the file with its default app. Persisted.</summary>
    public string? AttachPath { get; set; }

    /// <summary>A TABLE box (M11): a grid of rich-text cells rendered instead of the editor. Persisted.</summary>
    public NoteTable? Table { get; set; }

    public RichDocument Doc { get; }

    public NoteBox(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    /// <summary>Every editable document this box owns — the single Doc, or all table cells.
    /// The canvas subscribes these for autosave and evaporation logic.</summary>
    public IEnumerable<RichDocument> AllDocs => Table is null ? new[] { Doc } : Table.AllCells;

    /// <summary>True while the box holds nothing but a single bare paragraph — such boxes
    /// evaporate when focus settles elsewhere (OneNote behavior). Image, divider, attachment,
    /// and table boxes are never empty.</summary>
    public bool IsEmpty =>
        ImagePath is null && Divider is null && AttachPath is null && Table is null && IsBlank(Doc);

    public static bool IsBlank(RichDocument doc) =>
        doc.Paragraphs.Count == 1 && doc.Paragraphs[0].Runs.Count == 0 && doc.Paragraphs[0].Bullet is null;
}

/// <summary>A page's freeform canvas: any number of note boxes, plus the page's deleted-container
/// history (<see cref="Trash"/>, newest first) that boxes can be restored from. <see cref="Changed"/>
/// fires on add/remove/trash/restore, geometry commits, and any edit inside any live box's document —
/// one hook drives the page's dirty-tracking/autosave.</summary>
/// <summary>One mind-map connector: the two bubbles it joins and the compass edge each end anchors to
/// (the connect port it was drawn from / dropped on), so the line leaves and arrives at the right edge.</summary>
public sealed class MindLink
{
    public NoteBox A;
    public NoteBox B;
    public string DirA;
    public string DirB;
    public MindLink(NoteBox a, NoteBox b, string dirA, string dirB) { A = a; B = b; DirA = dirA; DirB = dirB; }
}

public sealed class CanvasDocument
{
    public List<NoteBox> Boxes { get; } = new();
    public List<NoteBox> Trash { get; } = new();

    /// <summary>Mind-map links: box pairs, each end anchored to a compass edge ("N"/"S"/"E"/"W" +
    /// diagonals) — the port the connector was drawn from / dropped on. Object references — a removed
    /// or trashed box takes its links with it (a restore comes back unlinked).</summary>
    public List<MindLink> Links { get; } = new();

    public event Action? Changed;

    /// <summary>Link two boxes with the given edge anchors, or UNLINK them when a link already exists
    /// (drag onto a linked bubble to undo). Returns true when the pair is linked after the call.</summary>
    public bool ToggleLink(NoteBox a, NoteBox b, string dirA = "E", string dirB = "W")
    {
        if (ReferenceEquals(a, b)) return false;
        int i = Links.FindIndex(l =>
            (ReferenceEquals(l.A, a) && ReferenceEquals(l.B, b)) ||
            (ReferenceEquals(l.A, b) && ReferenceEquals(l.B, a)));
        if (i >= 0)
        {
            Links.RemoveAt(i);
            Changed?.Invoke();
            return false;
        }
        Links.Add(new MindLink(a, b, dirA, dirB));
        Changed?.Invoke();
        return true;
    }

    private void DropLinks(NoteBox box) =>
        Links.RemoveAll(l => ReferenceEquals(l.A, box) || ReferenceEquals(l.B, box));

    public NoteBox AddBox(double x, double y, double width = NoteBox.DefaultWidth, RichDocument? doc = null)
    {
        var box = new NoteBox(doc)
        {
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = Math.Max(NoteBox.MinWidth, width),
        };
        Subscribe(box);
        Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    /// <summary>Add a TABLE box (M11): a rows×cols grid of rich-text cells. All cell documents are
    /// subscribed for autosave.</summary>
    public NoteBox AddTableBox(double x, double y, int rows, int cols, double width = NoteBox.DefaultWidth)
    {
        var box = new NoteBox
        {
            X = Math.Max(0, x), Y = Math.Max(0, y), Width = Math.Max(NoteBox.MinWidth, width),
            Table = NoteTable.Create(rows, cols),
        };
        Subscribe(box);
        Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    /// <summary>Add an already-built box (used by the loader): subscribe its documents and place it.
    /// Preserves its exact geometry/kind rather than clamping like <see cref="AddBox"/>.</summary>
    public NoteBox Adopt(NoteBox box)
    {
        Subscribe(box);
        Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    // ---- table structural edits (re-sync cell subscriptions, then persist) ----
    public void TableInsertRow(NoteBox box, int at) => MutateTable(box, t => t.InsertRow(at));
    public void TableInsertColumn(NoteBox box, int at) => MutateTable(box, t => t.InsertColumn(at));
    public void TableRemoveRow(NoteBox box, int r) => MutateTable(box, t => t.RemoveRow(r));
    public void TableRemoveColumn(NoteBox box, int c) => MutateTable(box, t => t.RemoveColumn(c));

    private void MutateTable(NoteBox box, Action<NoteTable> op)
    {
        if (box.Table is null) return;
        Unsubscribe(box);            // drop the old cell set…
        op(box.Table);
        Subscribe(box);              // …and re-hook the new one
        Changed?.Invoke();
    }

    private void Subscribe(NoteBox box)
    {
        foreach (var d in box.AllDocs) d.Changed += OnBoxDocChanged;
    }

    private void Unsubscribe(NoteBox box)
    {
        foreach (var d in box.AllDocs) d.Changed -= OnBoxDocChanged;
    }

    /// <summary>Permanently remove a box (empty-box evaporation, or deletion with history disabled).</summary>
    public void RemoveBox(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        DropLinks(box);
        Unsubscribe(box);
        Changed?.Invoke();
    }

    /// <summary>Move a box into the page's deleted history (newest first).</summary>
    public void DeleteToTrash(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        DropLinks(box);
        Unsubscribe(box);
        Trash.Insert(0, box);
        Changed?.Invoke();
    }

    /// <summary>Bring a deleted box back, optionally at a new position (drag-drop target point).</summary>
    public void RestoreFromTrash(NoteBox box, double? x = null, double? y = null)
    {
        if (!Trash.Remove(box)) return;
        if (x is not null) box.X = Math.Max(0, x.Value);
        if (y is not null) box.Y = Math.Max(0, y.Value);
        Subscribe(box);
        Boxes.Add(box);
        Changed?.Invoke();
    }

    /// <summary>Call once a move/resize drag completes (not per pointer-move) so the page persists.</summary>
    public void CommitGeometry() => Changed?.Invoke();

    private void OnBoxDocChanged() => Changed?.Invoke();
}
