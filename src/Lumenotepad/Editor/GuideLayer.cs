using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Editor;

/// <summary>The canvas's bottom layer: paints the grid-style paper background (tiled brush) and the
/// page-style guide lines/boxes in one Render pass. A plain Control (Panel.Render is sealed — the
/// same lesson as the Part-3 Border layer this replaces, but guides need real draw calls, and a
/// Control CAN override Render: RichTextEditor proves it).</summary>
public sealed class GuideLayer : Control
{
    private string _gridStyle = PageStyles.Blank;
    private string _pageStyle = PageStyles.Freeform;
    private int _mode;
    private IBrush? _gridBrush;

    public GuideLayer() => IsHitTestVisible = false;

    /// <summary>The viewport (visible page area) — divider positions anchor to it, not the growing
    /// canvas. Pushed by MainView from the ScrollViewer.</summary>
    public Size Viewport { get; set; }

    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _gridStyle = gridStyle;
        _pageStyle = pageStyle;
        _mode = mode;
        Refresh();
    }

    /// <summary>Rebuild the theme-derived brush + repaint (theme changes arrive via canvas Rebuild).</summary>
    public void Refresh()
    {
        _gridBrush = BuildGridBrush(_gridStyle);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (_gridBrush is not null) ctx.FillRectangle(_gridBrush, new Rect(size));
        if (_mode == PageStyles.ModeStartersOnly) return;          // starters-only: no guides
        var set = PageStyleGuides.For(_pageStyle, Viewport, size);
        if (set.Lines.Count == 0 && set.Boxes.Count == 0) return;
        var pen = new Pen(new SolidColorBrush(
            Color.Parse(Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x26))), 1);
        foreach (var (a, b) in set.Lines) ctx.DrawLine(pen, a, b);
        foreach (var r in set.Boxes) ctx.DrawRectangle(null, pen, r, 10, 10);
    }

    // ---- grid-style paper backgrounds (tiled brushes — one cell, GPU-repeated) ----

    private static IBrush? BuildGridBrush(string style)
    {
        var t = Services.ThemeManager.Current;
        if (style == PageStyles.Dots)
        {
            // Full dots at all four tile corners: each is clipped to its quarter inside the cell
            // and the neighbouring tiles complete it — whole dots exactly on the 20px lattice.
            var g = new GeometryGroup();
            foreach (var (x, y) in new[] { (0.0, 0.0), (GridMath.Cell, 0.0), (0.0, GridMath.Cell), (GridMath.Cell, GridMath.Cell) })
                g.Children.Add(new EllipseGeometry(new Rect(x - 1.1, y - 1.1, 2.2, 2.2)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Brush = new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x30))),
            }, GridMath.Cell);
        }
        if (style == PageStyles.Grid)
        {
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(GridMath.Cell, 0)));
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, GridMath.Cell)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            }, GridMath.Cell);
        }
        if (style == PageStyles.Ruled)
        {
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(PageStyleGuides.RuleSpacing, 0)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            }, PageStyleGuides.RuleSpacing);
        }
        return null;                                              // Blank
    }

    private static DrawingBrush Tile(Drawing cell, double size) => new()
    {
        Drawing = cell, TileMode = TileMode.Tile, Stretch = Stretch.None,
        SourceRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
    };
}
