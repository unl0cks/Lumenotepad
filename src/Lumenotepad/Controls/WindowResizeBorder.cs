using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lumenotepad.Controls;

/// <summary>
/// A transparent overlay of edge + corner grips that makes a chromeless (WindowDecorations="None") window
/// resizable. Avalonia's Win32 backend doesn't hit-test the borders of a decorationless window, so even
/// with WS_THICKFRAME set you can't drag the edges to resize — these grips call <c>BeginResizeDrag</c> for
/// the matching edge, which kicks off the native resize loop (so it also snaps). Only the thin outer rim is
/// hit-testable; the whole centre is empty, so the window content underneath stays fully interactive.
/// Drop one in as the last (top-most) child of the window's root grid, spanning every row/column.
/// </summary>
public sealed class WindowResizeBorder : Grid
{
    private const double EdgeThickness = 6;    // grab strip along each side
    private const double CornerSize = 13;      // grab square at each corner

    public WindowResizeBorder()
    {
        Background = null;                      // never block the centre
        // Sides
        AddGrip(WindowEdge.North, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, EdgeThickness, StandardCursorType.SizeNorthSouth);
        AddGrip(WindowEdge.South, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, EdgeThickness, StandardCursorType.SizeNorthSouth);
        AddGrip(WindowEdge.West, HorizontalAlignment.Left, VerticalAlignment.Stretch, EdgeThickness, double.NaN, StandardCursorType.SizeWestEast);
        AddGrip(WindowEdge.East, HorizontalAlignment.Right, VerticalAlignment.Stretch, EdgeThickness, double.NaN, StandardCursorType.SizeWestEast);
        // Corners (last → on top of the side strips for accurate diagonal cursors)
        AddGrip(WindowEdge.NorthWest, HorizontalAlignment.Left, VerticalAlignment.Top, CornerSize, CornerSize, StandardCursorType.TopLeftCorner);
        AddGrip(WindowEdge.NorthEast, HorizontalAlignment.Right, VerticalAlignment.Top, CornerSize, CornerSize, StandardCursorType.TopRightCorner);
        AddGrip(WindowEdge.SouthWest, HorizontalAlignment.Left, VerticalAlignment.Bottom, CornerSize, CornerSize, StandardCursorType.BottomLeftCorner);
        AddGrip(WindowEdge.SouthEast, HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerSize, CornerSize, StandardCursorType.BottomRightCorner);
    }

    private void AddGrip(WindowEdge edge, HorizontalAlignment h, VerticalAlignment v, double w, double ht, StandardCursorType cursor)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,   // transparent is still hit-testable; null is not
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Cursor = new Cursor(cursor),
        };
        if (!double.IsNaN(w)) grip.Width = w;
        if (!double.IsNaN(ht)) grip.Height = ht;
        grip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                TopLevel.GetTopLevel(this) is Window win)
            {
                win.BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        };
        Children.Add(grip);
    }
}
