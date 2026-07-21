using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumenotepad.Editor;

/// <summary>Draws the mind-map connectors — under the bubbles, over the paper. Each connector anchors at
/// the specific compass port it was drawn from / snapped onto (N/S/E/W, plus diagonals on the rounded
/// corners), leaves along that port's direction, is stroked with a gradient between the two bubbles'
/// colours, and bends around any OTHER bubble that would cross it. The belly of each curve is a spring,
/// so dragging a bubble makes its links lag and settle in a few decaying bounces (the ends stay glued).
/// A straight-line mode swaps the curves for rigid segments.
///
/// The in-flight rubber band (while a connect port is being dragged) keeps its tip on the cursor — no
/// lag — but its body is a spring, so a quick flick whips like a string; it wears the source bubble's
/// colour, carries a node at the tip, and snaps that tip onto the nearest port of any bubble it hovers.</summary>
public sealed class LinkLayer : Control
{
    internal CanvasDocument? Doc;
    internal Func<NoteBox, Rect?>? Resolve;

    /// <summary>Rigid straight segments instead of springy curves (toolbar "Straight links").</summary>
    internal bool Straight;

    /// <summary>While a connect-port drag is in flight: the source bubble + edge it started from, the live
    /// cursor, and the bubble + port the tip has snapped onto (if any).</summary>
    internal NoteBox? PendingSource;
    internal string PendingSourceDir = "E";
    internal Point PendingCursor;
    internal NoteBox? PendingSnap;
    internal string PendingSnapDir = "W";

    /// <summary>Per-link spring state for the belly (the two control points chase their target geometry).</summary>
    private sealed class LinkSpring
    {
        public Point C1, C2;
        public Vector V1, V2;
    }

    private readonly Dictionary<MindLink, LinkSpring> _springs = new();

    // The rubber band's body spring: one control point chasing the straight midpoint.
    private Point _penCtrl;
    private Vector _penCtrlVel;

    private DispatcherTimer? _timer;
    private Color _accent = Colors.Gray;

    public LinkLayer() => IsHitTestVisible = false;

    /// <summary>Re-derive theme-accent colours (theme changes arrive as a canvas Rebuild).</summary>
    public void Refresh()
    {
        _accent = Color.Parse(Services.ThemeManager.Current.Accent);
        InvalidateVisual();
    }

    // ---- pending rubber band ----

    internal void BeginPending(NoteBox src, string dir, Point cursor)
    {
        PendingSource = src;
        PendingSourceDir = dir;
        PendingCursor = cursor;
        PendingSnap = null;
        if (Resolve?.Invoke(src) is { } rs)
        {
            var a = EdgePoint(rs, dir);
            _penCtrl = new Point((a.X + cursor.X) / 2, (a.Y + cursor.Y) / 2);
        }
        else _penCtrl = cursor;
        _penCtrlVel = default;
        Animate();
    }

    internal void CancelPending()
    {
        PendingSource = null;
        PendingSnap = null;
        InvalidateVisual();
    }

    // ---- animation driver: springy connector bellies + the rubber band, self-stopping when at rest ----

    /// <summary>Kick the spring loop and repaint. Safe to call on every arrange — the timer settles itself.</summary>
    internal void Animate()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
        }
        if (!_timer.IsEnabled) _timer.Start();
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Doc is null || Resolve is null) { _timer?.Stop(); return; }
        const double dt = 0.016, stiffness = 300, damping = 12;   // ~2–3 decaying bounces, settles ~0.6s
        bool moving = false;

        if (!Straight)
            foreach (var link in Doc.Links)
            {
                if (!Ends(link, out var a, out var b, out var va, out var vb)) continue;
                var (c1t, c2t) = Controls(a, b, va, vb, link.A, link.B);
                var s = SpringFor(link, c1t, c2t);
                moving |= Step(ref s.C1, ref s.V1, c1t, dt, stiffness, damping);
                moving |= Step(ref s.C2, ref s.V2, c2t, dt, stiffness, damping);
            }

        if (PendingSource is { } src && Resolve(src) is { } rs)
        {
            var a = EdgePoint(rs, PendingSourceDir);
            var end = PendingEnd();
            var mid = new Point((a.X + end.X) / 2, (a.Y + end.Y) / 2);
            Step(ref _penCtrl, ref _penCtrlVel, mid, dt, 320, 13);
            moving = true;   // keep the loop live for the whole drag so the next flick is caught
        }

        Prune();
        InvalidateVisual();
        if (!moving) _timer?.Stop();
    }

    /// <summary>Advance one spring point toward its target; returns whether it is still meaningfully moving.</summary>
    private static bool Step(ref Point p, ref Vector v, Point target, double dt, double k, double damp)
    {
        double ax = (target.X - p.X) * k - v.X * damp;
        double ay = (target.Y - p.Y) * k - v.Y * damp;
        v = new Vector(v.X + ax * dt, v.Y + ay * dt);
        p = new Point(p.X + v.X * dt, p.Y + v.Y * dt);
        double dx = target.X - p.X, dy = target.Y - p.Y;
        return dx * dx + dy * dy > 0.09 || v.Length > 3;
    }

    private LinkSpring SpringFor(MindLink link, Point c1, Point c2)
    {
        if (!_springs.TryGetValue(link, out var s))
        {
            s = new LinkSpring { C1 = c1, C2 = c2 };   // born settled on target — no first-frame lurch
            _springs[link] = s;
        }
        return s;
    }

    private void Prune()
    {
        if (Doc is null || _springs.Count <= Doc.Links.Count) return;
        var live = new HashSet<MindLink>(Doc.Links);
        foreach (var key in _springs.Keys.Where(k => !live.Contains(k)).ToList())
            _springs.Remove(key);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Resolve is null) return;

        if (Doc is not null)
            foreach (var link in Doc.Links)
            {
                if (!Ends(link, out var a, out var b, out var va, out var vb)) continue;
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
                if (Straight)
                {
                    ctx.DrawLine(pen, a, b);
                }
                else
                {
                    if (!_springs.TryGetValue(link, out var s))
                    {
                        var (c1t, c2t) = Controls(a, b, va, vb, link.A, link.B);
                        s = new LinkSpring { C1 = c1t, C2 = c2t };
                        _springs[link] = s;
                    }
                    ctx.DrawGeometry(null, pen, Curve(a, s.C1, s.C2, b));   // belly = animated control points
                }
            }

        if (PendingSource is { } src && Resolve(src) is { } rs)
        {
            var a = EdgePoint(rs, PendingSourceDir);
            var end = PendingEnd();
            var col = ColorOf(src);                                        // line wears the source colour
            var pen = new Pen(new SolidColorBrush(col, 0.95), 2.6, lineCap: PenLineCap.Round);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(a, false);
                g.QuadraticBezierTo(_penCtrl, end);   // tip pinned to cursor/port; body springs → whip
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
            // A node at the tip so the line reads as a wire forming; it grows when snapped to a port.
            double halo = PendingSnap is null ? 9 : 11;
            ctx.DrawEllipse(new SolidColorBrush(col, 0.18), null, end, halo, halo);
            ctx.DrawEllipse(new SolidColorBrush(col),
                new Pen(new SolidColorBrush(Colors.White, 0.85), 1.25), end, 4.5, 4.5);
        }
    }

    /// <summary>Where the rubber-band tip sits: on the snapped port of the bubble it hovers, else the cursor.</summary>
    private Point PendingEnd()
    {
        if (PendingSnap is { } snap && Resolve?.Invoke(snap) is { } sr)
            return EdgePoint(sr, PendingSnapDir);
        return PendingCursor;
    }

    /// <summary>Resolve a link's two port anchors and their outward exit directions.</summary>
    private bool Ends(MindLink link, out Point a, out Point b, out Point va, out Point vb)
    {
        a = b = va = vb = default;
        if (Resolve!(link.A) is not { } ra || Resolve!(link.B) is not { } rb) return false;
        a = EdgePoint(ra, link.DirA);
        b = EdgePoint(rb, link.DirB);
        va = DirVector(link.DirA);
        vb = DirVector(link.DirB);
        return true;
    }

    private Color ColorOf(NoteBox box) =>
        box.Color is { } h && Color.TryParse(h, out var c) ? c : _accent;

    /// <summary>Control points for the connector: they leave A and enter B along their port directions
    /// (a clean flow), then bend to whichever side clears every OTHER bubble the run would cross. A port
    /// facing away from its partner gets a shorter lead-out, so it bends gently instead of curling into a
    /// loop over its own bubble.</summary>
    private (Point C1, Point C2) Controls(Point a, Point b, Point va, Point vb, NoteBox ba, NoteBox bb)
    {
        double dist = Distance(a, b);
        double baseOff = Math.Clamp(dist * 0.35, 34, 160);
        double offA = baseOff * FaceScale(va, b.X - a.X, b.Y - a.Y);
        double offB = baseOff * FaceScale(vb, a.X - b.X, a.Y - b.Y);
        var baseC1 = new Point(a.X + va.X * offA, a.Y + va.Y * offA);
        var baseC2 = new Point(b.X + vb.X * offB, b.Y + vb.Y * offB);
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

    /// <summary>1 when a port points straight at its partner, 0.35 when it points away — scales the
    /// lead-out length so an away-facing anchor doesn't fling a control point out into a loop.</summary>
    private static double FaceScale(Point normal, double tx, double ty)
    {
        double len = Math.Sqrt(tx * tx + ty * ty);
        if (len < 1e-6) return 1;
        double dot = (normal.X * tx + normal.Y * ty) / len;   // 1 = toward partner, -1 = away
        return 0.35 + 0.65 * (dot * 0.5 + 0.5);
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

    // ---- shared port geometry (NoteCanvas reuses these to anchor the drag + pick the snapped port) ----

    /// <summary>The point on a bubble's outline at a compass port: orthogonal at the flat mid-sides,
    /// diagonals on the rounded pill corners (radius = half the shorter side).</summary>
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

    /// <summary>The port of <paramref name="r"/> nearest <paramref name="p"/>; diagonals considered only
    /// when they are shown, so the snap matches the visible dots.</summary>
    public static string NearestDir(Rect r, Point p, bool diagonals)
    {
        string best = "E";
        double bestD = double.MaxValue;
        foreach (var dir in diagonals ? Dirs : Ortho)
        {
            var e = EdgePoint(r, dir);
            double d = (e.X - p.X) * (e.X - p.X) + (e.Y - p.Y) * (e.Y - p.Y);
            if (d < bestD) { bestD = d; best = dir; }
        }
        return best;
    }

    public static string NearestDir(Rect r, Point p) => NearestDir(r, p, true);

    private static readonly string[] Ortho = { "N", "S", "E", "W" };
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
