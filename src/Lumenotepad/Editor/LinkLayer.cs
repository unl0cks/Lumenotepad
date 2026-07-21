using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Editor;

/// <summary>Draws the mind-map connectors — under the bubbles, over the paper. Each link anchors at the
/// compass edge its ports were drawn from/dropped on, curves smoothly out of those edges, is stroked
/// with a gradient between the two bubbles' colours, and bends away from any other bubble that strays
/// near it. Geometry resolves per frame through the canvas resolver, so a connector follows its bubbles
/// live while they are dragged (NoteCanvas invalidates this layer from ArrangeOverride).</summary>
public sealed class LinkLayer : Control
{
    internal CanvasDocument? Doc;
    internal Func<NoteBox, Rect?>? Resolve;

    /// <summary>While a connect-port drag is in flight: the source bubble, the edge it started from,
    /// and the live cursor point (canvas coords).</summary>
    internal NoteBox? PendingSource;
    internal string PendingSourceDir = "E";
    internal Point PendingCursor;

    private Color _accent = Colors.Gray;
    private IPen _pendingPen = new Pen(Brushes.Gray, 2);

    public LinkLayer() => IsHitTestVisible = false;

    /// <summary>Re-derive theme-accent colours (theme changes arrive as a canvas Rebuild).</summary>
    public void Refresh()
    {
        _accent = Color.Parse(Services.ThemeManager.Current.Accent);
        _pendingPen = new Pen(new SolidColorBrush(_accent, 0.85), 2.2, lineCap: PenLineCap.Round)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0),
        };
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (Resolve is null) return;

        if (Doc is not null)
            foreach (var link in Doc.Links)
                if (Resolve(link.A) is { } ra && Resolve(link.B) is { } rb)
                {
                    var a = EdgePoint(ra, link.DirA);
                    var b = EdgePoint(rb, link.DirB);
                    var (c1, c2) = Controls(a, b, link.DirA, link.DirB, link.A, link.B);
                    var brush = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(a, RelativeUnit.Absolute),
                        EndPoint = new RelativePoint(b, RelativeUnit.Absolute),
                        GradientStops =
                        {
                            new GradientStop(ColorOf(link.A), 0),
                            new GradientStop(ColorOf(link.B), 1),
                        },
                    };
                    var pen = new Pen(brush, 2.6, lineCap: PenLineCap.Round);
                    ctx.DrawGeometry(null, pen, Curve(a, c1, c2, b));
                }

        if (PendingSource is { } src && Resolve(src) is { } rs)
        {
            var a = EdgePoint(rs, PendingSourceDir);
            ctx.DrawLine(_pendingPen, a, PendingCursor);   // straight rubber-band while connecting
        }
    }

    private Color ColorOf(NoteBox box) =>
        box.Color is { } h && Color.TryParse(h, out var c) ? c : _accent;

    /// <summary>Control points for the connector: they leave A and enter B along their edge directions
    /// (a clean "flow" curve), then bend away from any OTHER bubble whose body strays near the line.</summary>
    private (Point C1, Point C2) Controls(Point a, Point b, string dirA, string dirB, NoteBox ba, NoteBox bb)
    {
        double dist = Distance(a, b);
        double off = Math.Clamp(dist * 0.35, 34, 160);
        var va = DirVector(dirA);
        var vb = DirVector(dirB);
        var c1 = new Point(a.X + va.X * off, a.Y + va.Y * off);
        var c2 = new Point(b.X + vb.X * off, b.Y + vb.Y * off);

        // Obstacle avoidance: push the mid of the curve perpendicular, away from any near bubble.
        var perp = Perp(a, b);
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        double push = 0;
        if (Doc is not null)
            foreach (var box in Doc.Boxes)
            {
                if (ReferenceEquals(box, ba) || ReferenceEquals(box, bb)) continue;
                if (Resolve!(box) is not { } r) continue;
                var cc = r.Center;
                double d = DistToSegment(cc, a, b);
                double reach = Math.Max(r.Width, r.Height) * 0.55 + 34;
                if (d >= reach) continue;
                double side = (cc.X - mid.X) * perp.X + (cc.Y - mid.Y) * perp.Y >= 0 ? -1 : 1;  // opposite the box
                push += side * (reach - d) * 0.9;
            }
        push = Math.Clamp(push, -220, 220);
        return (new Point(c1.X + perp.X * push, c1.Y + perp.Y * push),
                new Point(c2.X + perp.X * push, c2.Y + perp.Y * push));
    }

    private static Geometry Curve(Point a, Point c1, Point c2, Point b)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(a, false);
        g.CubicBezierTo(c1, c2, b);
        g.EndFigure(false);
        return geo;
    }

    // ---- shared edge geometry (NoteCanvas reuses these to anchor the drag + pick the drop edge) ----

    /// <summary>The point on a box's boundary at a compass direction.</summary>
    public static Point EdgePoint(Rect r, string dir)
    {
        var (fx, fy) = Frac(dir);
        return new Point(r.X + fx * r.Width, r.Y + fy * r.Height);
    }

    /// <summary>The compass edge of <paramref name="r"/> whose boundary point is nearest <paramref name="p"/>.</summary>
    public static string NearestDir(Rect r, Point p)
    {
        string best = "E";
        double bestD = double.MaxValue;
        foreach (var dir in Dirs)
        {
            var e = EdgePoint(r, dir);
            double d = (e.X - p.X) * (e.X - p.X) + (e.Y - p.Y) * (e.Y - p.Y);
            if (d < bestD) { bestD = d; best = dir; }
        }
        return best;
    }

    private static readonly string[] Dirs = { "N", "S", "E", "W", "NE", "NW", "SE", "SW" };

    private static (double, double) Frac(string dir) => dir switch
    {
        "N" => (0.5, 0.0), "S" => (0.5, 1.0), "E" => (1.0, 0.5), "W" => (0.0, 0.5),
        "NE" => (1.0, 0.0), "NW" => (0.0, 0.0), "SE" => (1.0, 1.0), "SW" => (0.0, 1.0),
        _ => (1.0, 0.5),
    };

    private static Point DirVector(string dir)
    {
        const double q = 0.7071;
        return dir switch
        {
            "N" => new Point(0, -1), "S" => new Point(0, 1), "E" => new Point(1, 0), "W" => new Point(-1, 0),
            "NE" => new Point(q, -q), "NW" => new Point(-q, -q), "SE" => new Point(q, q), "SW" => new Point(-q, q),
            _ => new Point(1, 0),
        };
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point Perp(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 0.001 ? new Point(0, -1) : new Point(-dy / len, dx / len);
    }

    private static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 0.001) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
}
