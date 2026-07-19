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

    /// <summary>The bottom of the REAL content (lowest container), pushed by NoteCanvas on measure.
    /// The canvas Bounds carry a big trailing breathing-room pad, so styles that dock to the page
    /// foot (Cornell's summary band) use this instead — it hugs the content and descends with it.</summary>
    public double ContentBottom
    {
        get => _contentBottom;
        set { if (System.Math.Abs(value - _contentBottom) < 0.5) return; _contentBottom = value; InvalidateVisual(); }
    }
    private double _contentBottom;

    /// <summary>Chip-preview instances set this (wizard + customize dialogs): styles whose real
    /// page draws NOTHING (Mindmap — bubbles + links only) still get a little illustrative motif
    /// so their chip isn't a blank square. The live canvas leaves it false.</summary>
    public bool PreviewMotif { get; set; }

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
        if (PreviewMotif && _pageStyle == PageStyles.Mindmap) { RenderMindmapMotif(ctx, size); return; }
        if (_mode == PageStyles.ModeStartersOnly) return;          // starters-only: no guides
        var set = PageStyleGuides.For(_pageStyle, Viewport, size, _contentBottom);
        if (set.Lines.Count == 0 && set.Boxes.Count == 0) return;
        var pen = new Pen(new SolidColorBrush(
            Color.Parse(Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x26))), 1);
        foreach (var (a, b) in set.Lines) ctx.DrawLine(pen, a, b);
        foreach (var r in set.Boxes) ctx.DrawRectangle(null, pen, r, 10, 10);
    }

    /// <summary>The Mindmap chip illustration: a central bubble with two linked branches.</summary>
    private static void RenderMindmapMotif(DrawingContext ctx, Size size)
    {
        var t = Services.ThemeManager.Current;
        var line = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.Accent, 0x8C))), 1.6);
        var ring = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x40))), 1.2);
        var center = new Point(size.Width * 0.5, size.Height * 0.5);
        var left = new Point(size.Width * 0.2, size.Height * 0.26);
        var right = new Point(size.Width * 0.78, size.Height * 0.72);
        ctx.DrawLine(line, center, left);
        ctx.DrawLine(line, center, right);
        ctx.DrawEllipse(null, ring, center, size.Width * 0.13, size.Height * 0.14);
        ctx.DrawEllipse(null, ring, left, size.Width * 0.09, size.Height * 0.11);
        ctx.DrawEllipse(null, ring, right, size.Width * 0.09, size.Height * 0.11);
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
