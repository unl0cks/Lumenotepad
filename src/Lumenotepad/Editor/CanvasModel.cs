using System;
using System.Collections.Generic;

namespace Lumenotepad.Editor;

/// <summary>One movable note container on a page canvas: a rich document with a position and width.
/// Height always follows content, so it is not stored. Pure model — no Avalonia dependencies.</summary>
public sealed class NoteBox
{
    public const double DefaultWidth = 360;
    public const double MinWidth = 140;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = DefaultWidth;
    public RichDocument Doc { get; }

    public NoteBox(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    /// <summary>True while the box holds nothing but a single bare paragraph — such boxes
    /// evaporate when focus settles elsewhere (OneNote behavior).</summary>
    public bool IsEmpty => IsBlank(Doc);

    public static bool IsBlank(RichDocument doc) =>
        doc.Paragraphs.Count == 1 && doc.Paragraphs[0].Runs.Count == 0 && doc.Paragraphs[0].Bullet is null;
}

/// <summary>A page's freeform canvas: any number of note boxes. <see cref="Changed"/> fires on
/// add/remove, geometry commits, and any edit inside any box's document — one hook drives the
/// page's dirty-tracking/autosave.</summary>
public sealed class CanvasDocument
{
    public List<NoteBox> Boxes { get; } = new();

    public event Action? Changed;

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

    public void RemoveBox(NoteBox box)
    {
        if (!Boxes.Remove(box)) return;
        box.Doc.Changed -= OnBoxDocChanged;
        Changed?.Invoke();
    }

    /// <summary>Call once a move/resize drag completes (not per pointer-move) so the page persists.</summary>
    public void CommitGeometry() => Changed?.Invoke();

    private void OnBoxDocChanged() => Changed?.Invoke();
}
