using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumenotepad.Editor;

/// <summary>Draws the mind-map connectors — under the bubbles, over the paper. Each link anchors at the
/// compass edge its ports were drawn from/dropped on (orthogonal edges at the flat sides, diagonals on
/// the rounded pill corners), curves smoothly out of those edges, is stroked with a gradient between the
/// two bubbles' colours, and bends around any other bubble that would otherwise cross it. Geometry
/// resolves per frame through the canvas resolver, so a connector follows its bubbles live while they are
/// dragged (NoteCanvas invalidates this layer from ArrangeOverride).
///
/// The in-flight rubber band (while a connect port is being dragged) is a little spring: the free end
/// lags the cursor and overshoots on a sharp flick, then settles in a few decaying bounces, and the
/// string bows opposite to its motion — so a fresh link feels like a length of elastic being pulled.</summary>
public sealed class LinkLayer : Control
{
    internal CanvasDocument? Doc;
    internal Func<NoteBox, Rect?>? Resolve;

    /// <summary>While a connect-port drag is in flight: the source bubble, the edge it started from,
    /// and the live cursor point (canvas coords) the spring chases.</summary>
    internal NoteBox? PendingSource;
    internal string PendingSourceDir = "E";
    internal Point PendingCursor;

    // Spring state for the rubber band's free end (canvas coords) and the 60 Hz driver.
    private Point _penPos;
    private Vector _penVel;
    private DispatcherTimer? _spring;

    private Color _accent = Colors.Gray;
    private IPen _pendingPen = new Pen(Brushes.Gray, 2);

    public LinkLayer() => IsHitTestVisible = false;

    /// <summary>Re-derive theme-accent colours (theme changes arrive as a canvas Rebuild).</summary>
    public void Refresh()
    {
        _accent = Color.Parse(Services.ThemeManager.Current.Accent);
        _pendingPen = new Pen(new SolidColorBrush(_accent, 0.9), 2.4, lineCap: PenLineCap.Round);
        InvalidateVisual();
    }

    // ---- pending rubber band (the spring) ----

    /// <summary>Begin the in-flight connector from <paramref name="src"/>'s <paramref name="dir"/> edge.
    /// The free end starts pinned to the cursor (no first-frame snap) and the spring loop begins.</summary>
    internal void BeginPending(NoteBox src, string dir, Point cursor)
    {
        PendingSource = src;
        PendingSourceDir = dir;
        PendingCursor = cursor;
        _penPos = cursor;
        _penVel = default;
        _spring ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spring.Tick -= OnSpringTick;
        _spring.Tick += OnSpringTick;
        _spring.Start();
        InvalidateVisual();
    }

    /// <summary>End the in-flight connector: stop the spring and clear the source.</summary>
    internal void CancelPending()
    {
        PendingSource = null;
        _spring?.Stop();
        InvalidateVisual();
    }

    private void OnSpringTick(object? sender, EventArgs e)
    {
        if (PendingSource is null) { _spring?.Stop(); return; }
        // Underdamped spring: stiff enough that the lag stays subtle, damped light enough that a sharp
        // flick overshoots and bounces two or three times, each smaller than the last.
        const double dt = 0.016, stiffness = 260, damping = 10;
        double ax = (PendingCursor.X - _penPos.X) * stiffness - _penVel.X * damping;
        double ay = (PendingCursor.Y - _penPos.Y) * stiffness - _penVel.Y * damping;
        _penVel = new Vector(_penVel.X + ax * dt, _penVel.Y + ay * dt);
        _penPos = new Point(_penPos.X + _penVel.X * dt, _penPos.Y + _penVel.Y * dt);
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
            var end = _penPos;
            // Bow the string opposite to the free end's velocity, so it whips and wobbles as it bounces.
            var mid = new Point((a.X + end.X) / 2, (a.Y + end.Y) / 2);
            const double k = 0.03;
            var ctrl = new Point(mid.X - _penVel.X * k, mid.Y - _penVel.Y * k);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(a, false);
                g.QuadraticBezierTo(ctrl, end);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, _pendingPen, geo);
        }
    }

    private Color ColorOf(NoteBox box) =>
        box.Color is { } h && Color.TryParse(h, out var c) ? c : _accent;

    /// <summary>Control points for the connector: they leave A and enter B along their edge directions
    /// (a clean "flow" curve), then bend to whichever side clears every OTHER bubble the straight run
    /// would cross — re-checked against the actual curve so the line always tries to route around a
    /// circle rather than through it.</summary>
    private (Point C1, Point C2) Controls(Point a, Point b, string dirA, string dirB, NoteBox ba, NoteBox bb)
    {
        double dist = Distance(a, b);
        double off = Math.Clamp(dist * 0.35, 34, 160);
        var va = DirVector(dirA);
        var vb = DirVector(dirB);
        var baseC1 = new Point(a.X + va.X * off, a.Y + va.Y * off);
        var baseC2 = new Point(b.X + vb.X * off, b.Y + vb.Y * off);
        var perp = Perp(a, b);
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        // Bend the whole curve to one side until it clears the worst offender; re-sample and repeat.
        double push = 0;
        for (int iter = 0; iter < 8; iter++)
        {
            var c1 = new Point(baseC1.X + perp.X * push, baseC1.Y + perp.Y * push);
            var c2 = new Point(baseC2.X + perp.X * push, baseC2.Y + perp.Y * push);
            double worst = 0, worstSide = 1;
            if (Doc is not null)
                foreach (var box in Doc.Boxes)
                {
                    if (ReferenceEquals(box, ba) || ReferenceEquals(box, bb)) continue;
                    if (Resolve!(box) is not { } r) continue;
                    var cc = r.Center;
                    double radius = Math.Max(r.Width, r.Height) * 0.5 + 12;   // bubble body + clearance
                    double pen = radius - MinDistToCurve(a, c1, c2, b, cc);
                    if (pen > worst)
                    {
                        worst = pen;
                        double dot = (cc.X - mid.X) * perp.X + (cc.Y - mid.Y) * perp.Y;
                        worstSide = dot >= 0 ? -1 : 1;                          // push away from the box
                    }
                }
            if (worst <= 0.5) break;
            push += worstSide * (worst + 8);
            push = Math.Clamp(push, -420, 420);
        }
        return (new Point(baseC1.X + perp.X * push, baseC1.Y + perp.Y * push),
                new Point(baseC2.X + perp.X * push, baseC2.Y + perp.Y * push));
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

    private static Point Bezier(Point a, Point c1, Point c2, Point b, double t)
    {
        double u = 1 - t;
        double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
        return new Point(w0 * a.X + w1 * c1.X + w2 * c2.X + w3 * b.X,
                         w0 * a.Y + w1 * c1.Y + w2 * c2.Y + w3 * b.Y);
    }

    /// <summary>Shortest distance from a point to a cubic bezier, sampled — enough to tell whether the
    /// drawn curve grazes a bubble's body.</summary>
    private static double MinDistToCurve(Point a, Point c1, Point c2, Point b, Point p)
    {
        double best = double.MaxValue;
        const int n = 18;
        for (int i = 0; i <= n; i++)
        {
            var pt = Bezier(a, c1, c2, b, (double)i / n);
            double dx = pt.X - p.X, dy = pt.Y - p.Y;
            double d = dx * dx + dy * dy;
            if (d < best) best = d;
        }
        return Math.Sqrt(best);
    }

    // ---- shared edge geometry (NoteCanvas reuses these to anchor the drag + pick the drop edge) ----

    /// <summary>The point on a bubble's outline at a compass direction: orthogonal edges at the flat
    /// mid-sides, diagonals on the rounded pill corner (radius = half the shorter side), so a diagonal
    /// link touches the visible corner rather than the empty rectangular corner outside it.</summary>
    public static Point EdgePoint(Rect r, string dir)
    {
        double rad = Math.Min(r.Width, r.Height) / 2;
        double k = 0.2929 * rad;   // 45° inset onto the corner arc: rad·(1 − √2/2)
        return dir switch
        {
            "N" => new Point(r.X + r.Width / 2, r.Y),
            "S" => new Point(r.X + r.Width / 2, r.Bottom),
            "E" => new Point(r.Right, r.Y + r.Height / 2),
            "W" => new Point(r.X, r.Y + r.Height / 2),
            "NW" => new Point(r.X + k, r.Y + k),
            "NE" => new Point(r.Right - k, r.Y + k),
            "SW" => new Point(r.X + k, r.Bottom - k),
            "SE" => new Point(r.Right - k, r.Bottom - k),
            _ => new Point(r.Right, r.Y + r.Height / 2),
        };
    }

    /// <summary>The compass edge of <paramref name="r"/> whose outline point is nearest <paramref name="p"/>.</summary>
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
}
