using System;
using System.Collections.Generic;

namespace Lumenotepad.Editor;

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

    public RichDocument Doc { get; }

    public NoteBox(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    /// <summary>True while the box holds nothing but a single bare paragraph — such boxes
    /// evaporate when focus settles elsewhere (OneNote behavior). Image, divider, and
    /// attachment boxes are never empty.</summary>
    public bool IsEmpty => ImagePath is null && Divider is null && AttachPath is null && IsBlank(Doc);

    public static bool IsBlank(RichDocument doc) =>
        doc.Paragraphs.Count == 1 && doc.Paragraphs[0].Runs.Count == 0 && doc.Paragraphs[0].Bullet is null;
}

/// <summary>A page's freeform canvas: any number of note boxes, plus the page's deleted-container
/// history (<see cref="Trash"/>, newest first) that boxes can be restored from. <see cref="Changed"/>
/// fires on add/remove/trash/restore, geometry commits, and any edit inside any live box's document —
/// one hook drives the page's dirty-tracking/autosave.</summary>
public sealed class CanvasDocument
{
    public List<NoteBox> Boxes { get; } = new();
    public List<NoteBox> Trash { get; } = new();

    /// <summary>Mindmap links (M9 Part 5): undirected box pairs, created by dropping one bubble
    /// onto another while the page's style is Mindmap. Object references — a removed or trashed
    /// box takes its links with it (a restore comes back unlinked).</summary>
    public List<(NoteBox A, NoteBox B)> Links { get; } = new();

    public event Action? Changed;

    /// <summary>Link two boxes, or UNLINK them when the link already exists (drop again to undo).
    /// Returns true when the pair is linked after the call.</summary>
    public bool ToggleLink(NoteBox a, NoteBox b)
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
        Links.Add((a, b));
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
        box.Doc.Changed += OnBoxDocChanged;
        Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    /// <summary>Permanently remove a box (empty-box evaporation, or deletion with history disabled).</summary>
    public void RemoveBox(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        DropLinks(box);
        box.Doc.Changed -= OnBoxDocChanged;
        Changed?.Invoke();
    }

    /// <summary>Move a box into the page's deleted history (newest first).</summary>
    public void DeleteToTrash(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        DropLinks(box);
        box.Doc.Changed -= OnBoxDocChanged;
        Trash.Insert(0, box);
        Changed?.Invoke();
    }

    /// <summary>Bring a deleted box back, optionally at a new position (drag-drop target point).</summary>
    public void RestoreFromTrash(NoteBox box, double? x = null, double? y = null)
    {
        if (!Trash.Remove(box)) return;
        if (x is not null) box.X = Math.Max(0, x.Value);
        if (y is not null) box.Y = Math.Max(0, y.Value);
        box.Doc.Changed += OnBoxDocChanged;
        Boxes.Add(box);
        Changed?.Invoke();
    }

    /// <summary>Call once a move/resize drag completes (not per pointer-move) so the page persists.</summary>
    public void CommitGeometry() => Changed?.Invoke();

    private void OnBoxDocChanged() => Changed?.Invoke();
}
