using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Editor;

public sealed class GuideLayer : Control
{
    private string _gridStyle = PageStyles.Blank;
    private string _pageStyle = PageStyles.Freeform;
    private int _mode;
    private IBrush? _gridBrush;

    public GuideLayer() => IsHitTestVisible = false;

    public Size Viewport { get; set; }

    public double ContentBottom
    {
        get => _contentBottom;
        set { if (System.Math.Abs(value - _contentBottom) < 0.5) return; _contentBottom = value; InvalidateVisual(); }
    }
    private double _contentBottom;

    public bool PreviewMotif { get; set; }

    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _gridStyle = gridStyle;
        _pageStyle = pageStyle;
        _mode = mode;
        Refresh();
    }

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
        if (_mode == PageStyles.ModeStartersOnly) return;
        var set = PageStyleGuides.For(_pageStyle, Viewport, size, _contentBottom);
        if (set.Lines.Count == 0 && set.Boxes.Count == 0) return;
        var pen = new Pen(new SolidColorBrush(
            Color.Parse(Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x26))), 1);
        foreach (var (a, b) in set.Lines) ctx.DrawLine(pen, a, b);
        foreach (var r in set.Boxes) ctx.DrawRectangle(null, pen, r, 10, 10);
    }

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

    private static IBrush? BuildGridBrush(string style)
    {
        var t = Services.ThemeManager.Current;
        if (style == PageStyles.Dots)
        {

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
        if (style is PageStyles.Ruled or PageStyles.RuledWide)
        {
            double spacing = style == PageStyles.RuledWide
                ? PageStyleGuides.RuleSpacingWide
                : PageStyleGuides.RuleSpacing;
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(spacing, 0)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            }, spacing);
        }
        return null;
    }

    private static DrawingBrush Tile(Drawing cell, double size) => new()
    {
        Drawing = cell, TileMode = TileMode.Tile, Stretch = Stretch.None,
        SourceRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
    };
}
