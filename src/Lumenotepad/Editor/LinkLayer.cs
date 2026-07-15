using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Editor;

/// <summary>Draws the mindmap link lines (M9 Part 5) — sits between the guide layer and the note
/// containers, so connectors run UNDER the bubbles. Geometry is resolved per frame through the
/// canvas-provided resolver (a box's on-screen rect), which lets the lines follow a bubble live
/// while it is dragged (NoteCanvas invalidates this layer from ArrangeOverride).</summary>
public sealed class LinkLayer : Control
{
    internal CanvasDocument? Doc;
    internal Func<NoteBox, Rect?>? Resolve;

    private IPen _pen = new Pen(Brushes.Gray, 2);

    public LinkLayer() => IsHitTestVisible = false;

    /// <summary>Re-derive the theme-accent pen (theme changes arrive as a canvas Rebuild).</summary>
    public void Refresh()
    {
        var accent = Services.ThemeManager.Current.Accent;
        _pen = new Pen(
            new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(accent, 0x8C))),
            2, lineCap: PenLineCap.Round);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (Doc is null || Doc.Links.Count == 0 || Resolve is null) return;
        foreach (var (a, b) in Doc.Links)
            if (Resolve(a) is { } ra && Resolve(b) is { } rb)
                ctx.DrawLine(_pen, ra.Center, rb.Center);
    }
}
