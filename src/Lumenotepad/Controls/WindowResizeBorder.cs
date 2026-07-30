using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lumenotepad.Controls;

public sealed class WindowResizeBorder : Grid
{
    private const double EdgeThickness = 6;
    private const double CornerSize = 13;

    public WindowResizeBorder()
    {
        Background = null;

        AddGrip(WindowEdge.North, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, EdgeThickness, StandardCursorType.SizeNorthSouth);
        AddGrip(WindowEdge.South, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, EdgeThickness, StandardCursorType.SizeNorthSouth);
        AddGrip(WindowEdge.West, HorizontalAlignment.Left, VerticalAlignment.Stretch, EdgeThickness, double.NaN, StandardCursorType.SizeWestEast);
        AddGrip(WindowEdge.East, HorizontalAlignment.Right, VerticalAlignment.Stretch, EdgeThickness, double.NaN, StandardCursorType.SizeWestEast);

        AddGrip(WindowEdge.NorthWest, HorizontalAlignment.Left, VerticalAlignment.Top, CornerSize, CornerSize, StandardCursorType.TopLeftCorner);
        AddGrip(WindowEdge.NorthEast, HorizontalAlignment.Right, VerticalAlignment.Top, CornerSize, CornerSize, StandardCursorType.TopRightCorner);
        AddGrip(WindowEdge.SouthWest, HorizontalAlignment.Left, VerticalAlignment.Bottom, CornerSize, CornerSize, StandardCursorType.BottomLeftCorner);
        AddGrip(WindowEdge.SouthEast, HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerSize, CornerSize, StandardCursorType.BottomRightCorner);
    }

    private void AddGrip(WindowEdge edge, HorizontalAlignment h, VerticalAlignment v, double w, double ht, StandardCursorType cursor)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
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
