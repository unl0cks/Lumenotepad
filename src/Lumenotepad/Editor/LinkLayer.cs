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

    /// <summary>While a connect-port drag is in flight: the bubble the line starts at, and the live
    /// cursor point (canvas coords). NoteCanvas sets these and invalidates the layer per move.</summary>
    internal NoteBox? PendingSource;
    internal Point PendingCursor;

    private IPen _pen = new Pen(Brushes.Gray, 2);
    private IPen _pendingPen = new Pen(Brushes.Gray, 2);

    public LinkLayer() => IsHitTestVisible = false;

    /// <summary>Re-derive the theme-accent pen (theme changes arrive as a canvas Rebuild).</summary>
    public void Refresh()
    {
        var accent = Services.ThemeManager.Current.Accent;
        _pen = new Pen(
            new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(accent, 0x8C))),
            2, lineCap: PenLineCap.Round);
        _pendingPen = new Pen(
            new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(accent, 0xC0))),
            2.2, lineCap: PenLineCap.Round) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (Resolve is null) return;
        if (Doc is not null)
            foreach (var (a, b) in Doc.Links)
                if (Resolve(a) is { } ra && Resolve(b) is { } rb)
                    ctx.DrawLine(_pen, ra.Center, rb.Center);
        if (PendingSource is { } src && Resolve(src) is { } rs)
            ctx.DrawLine(_pendingPen, rs.Center, PendingCursor);   // rubber-band while connecting
    }
}
