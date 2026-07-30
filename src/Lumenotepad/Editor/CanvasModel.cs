using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Editor;

public sealed class NoteTable
{
    public const int MaxRows = 40, MaxCols = 12;

    public List<List<RichDocument>> Rows { get; } = new();

    public int RowCount => Rows.Count;
    public int ColCount => Rows.Count == 0 ? 0 : Rows[0].Count;

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

    public void InsertRow(int at)
    {
        if (RowCount >= MaxRows) return;
        int cols = Math.Max(1, ColCount);
        at = at < 0 || at > RowCount ? RowCount : at;
        Rows.Insert(at, Enumerable.Range(0, cols).Select(_ => new RichDocument()).ToList());
    }

    public void InsertColumn(int at)
    {
        if (ColCount >= MaxCols) return;
        int c = at < 0 || at > ColCount ? ColCount : at;
        foreach (var row in Rows) row.Insert(c, new RichDocument());
    }

    public void RemoveRow(int r)
    {
        if (RowCount <= 1 || r < 0 || r >= RowCount) return;
        Rows.RemoveAt(r);
    }

    public void RemoveColumn(int c)
    {
        if (ColCount <= 1 || c < 0 || c >= ColCount) return;
        foreach (var row in Rows) row.RemoveAt(c);
    }
}

public enum BubbleKind { Title, Info, Callout }

public sealed class NoteBox
{
    public const double DefaultWidth = 360;
    public const double MinWidth = 140;
    public const double MinHeight = 42;

    public const double MinDividerLength = 60;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = DefaultWidth;
    public double H { get; set; }

    public bool Locked { get; set; }

    public string? Region { get; set; }

    public string? Color { get; set; }

    public double FontScale { get; set; } = 1.0;

    public bool Central { get; set; }

    public BubbleKind Kind { get; set; } = BubbleKind.Title;

    public string? ImagePath { get; set; }

    public string? Divider { get; set; }

    public string? AttachPath { get; set; }

    public NoteTable? Table { get; set; }

    public RichDocument Doc { get; }

    public NoteBox(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    public IEnumerable<RichDocument> AllDocs => Table is null ? new[] { Doc } : Table.AllCells;

    public bool IsEmpty =>
        ImagePath is null && Divider is null && AttachPath is null && Table is null && IsBlank(Doc);

    public static bool IsBlank(RichDocument doc) =>
        doc.Paragraphs.Count == 1 && doc.Paragraphs[0].Runs.Count == 0 && doc.Paragraphs[0].Bullet is null;
}

public sealed class MindLink
{
    public NoteBox A;
    public NoteBox B;
    public string DirA;
    public string DirB;

    public string? Label;
    public MindLink(NoteBox a, NoteBox b, string dirA, string dirB) { A = a; B = b; DirA = dirA; DirB = dirB; }
}

public sealed class CanvasDocument
{
    public List<NoteBox> Boxes { get; } = new();
    public List<NoteBox> Trash { get; } = new();

    public List<MindLink> Links { get; } = new();

    private readonly List<MindLink> _heldLinks = new();

    public event Action? Changed;

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

    public NoteBox Adopt(NoteBox box)
    {
        Subscribe(box);
        Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    public void TableInsertRow(NoteBox box, int at) => MutateTable(box, t => t.InsertRow(at));
    public void TableInsertColumn(NoteBox box, int at) => MutateTable(box, t => t.InsertColumn(at));
    public void TableRemoveRow(NoteBox box, int r) => MutateTable(box, t => t.RemoveRow(r));
    public void TableRemoveColumn(NoteBox box, int c) => MutateTable(box, t => t.RemoveColumn(c));

    private void MutateTable(NoteBox box, Action<NoteTable> op)
    {
        if (box.Table is null) return;
        Unsubscribe(box);
        op(box.Table);
        Subscribe(box);
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

    public void RemoveBox(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        DropLinks(box);
        _heldLinks.RemoveAll(l => ReferenceEquals(l.A, box) || ReferenceEquals(l.B, box));
        Unsubscribe(box);
        Changed?.Invoke();
    }

    public void DeleteToTrash(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        for (int i = Links.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Links[i].A, box) || ReferenceEquals(Links[i].B, box))
            {
                _heldLinks.Add(Links[i]);
                Links.RemoveAt(i);
            }
        Unsubscribe(box);
        Trash.Insert(0, box);
        Changed?.Invoke();
    }

    public void RestoreFromTrash(NoteBox box, double? x = null, double? y = null)
    {
        if (!Trash.Remove(box)) return;
        if (x is not null) box.X = Math.Max(0, x.Value);
        if (y is not null) box.Y = Math.Max(0, y.Value);
        Subscribe(box);
        Boxes.Add(box);
        for (int i = _heldLinks.Count - 1; i >= 0; i--)
        {
            var l = _heldLinks[i];
            if ((ReferenceEquals(l.A, box) || ReferenceEquals(l.B, box)) && Boxes.Contains(l.A) && Boxes.Contains(l.B))
            {
                Links.Add(l);
                _heldLinks.RemoveAt(i);
            }
        }
        Changed?.Invoke();
    }

    public void CommitGeometry() => Changed?.Invoke();

    private void OnBoxDocChanged() => Changed?.Invoke();
}
