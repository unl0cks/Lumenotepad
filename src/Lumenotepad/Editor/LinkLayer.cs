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

    // ---- create / remove flourishes: growing links, retracting ghosts, and sparks ----
    private sealed class Particle { public Point Pos; public Vector Vel; public double Life, MaxLife, Size; public Color Color; }
    private sealed class Retract { public NoteBox Survivor = null!; public string SurvivorDir = "E"; public Point Start; public Color ColSurv, ColGone; public double T; }
    private readonly List<Particle> _particles = new();
    private readonly Dictionary<MindLink, double> _grow = new();   // link → draw-in progress 0..1
    private readonly List<Retract> _retracts = new();
    private readonly Random _rng = new();

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

    // ---- create / remove animations ----

    /// <summary>Draw a freshly created link IN: the string extends from the source into the target with
    /// sparks trailing at the growing tip (a fuse getting longer).</summary>
    internal void AnimateLinkIn(MindLink link)
    {
        _grow[link] = 0;
        Animate();
    }

    /// <summary>A bubble is being removed: each of its links retracts toward the surviving bubble,
    /// flailing left/right and shedding sparks until it is sucked in. Call BEFORE the box is removed,
    /// while its links still exist; <paramref name="goneRect"/> is the bubble's last on-screen rect.</summary>
    internal void AnimateBubbleRemoval(NoteBox gone, Rect goneRect)
    {
        if (Doc is null) return;
        foreach (var link in Doc.Links)
        {
            NoteBox? surv = ReferenceEquals(link.A, gone) ? link.B
                          : ReferenceEquals(link.B, gone) ? link.A : null;
            if (surv is null) continue;
            bool goneIsA = ReferenceEquals(link.A, gone);
            _retracts.Add(new Retract
            {
                Survivor = surv,
                SurvivorDir = goneIsA ? link.DirB : link.DirA,
                Start = EdgePoint(goneRect, goneIsA ? link.DirA : link.DirB),
                ColSurv = ColorOf(surv),
                ColGone = ColorOf(gone),
            });
        }
        Burst(goneRect, ColorOf(gone), 28);   // the bubble pops into a spray of its own colour
        Animate();
    }

    /// <summary>Remove a single link (both bubbles survive): the same fuse retract — the string detaches
    /// from A and whips into B, shedding sparks. Call while the link's bubbles still resolve.</summary>
    internal void AnimateLinkRemoval(MindLink link)
    {
        if (Resolve is null || Resolve(link.A) is not { } ra || Resolve(link.B) is null) return;
        _retracts.Add(new Retract
        {
            Survivor = link.B,
            SurvivorDir = link.DirB,
            Start = EdgePoint(ra, link.DirA),
            ColSurv = ColorOf(link.B),
            ColGone = ColorOf(link.A),
        });
        _grow.Remove(link);
        Animate();
    }

    /// <summary>Spray particles across a rect's footprint (a bubble popping).</summary>
    private void Burst(Rect r, Color col, int count)
    {
        var c = r.Center;
        for (int i = 0; i < count; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            double sp = 45 + _rng.NextDouble() * 150;
            double px = c.X + (_rng.NextDouble() - 0.5) * r.Width * 0.7;
            double py = c.Y + (_rng.NextDouble() - 0.5) * r.Height * 0.6;
            _particles.Add(new Particle
            {
                Pos = new Point(px, py), Vel = new Vector(Math.Cos(ang) * sp, Math.Sin(ang) * sp),
                Life = 0, MaxLife = 0.5 + _rng.NextDouble() * 0.5, Size = 2 + _rng.NextDouble() * 2.5, Color = col,
            });
        }
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

        // Link draw-in: advance progress and spray sparks at the growing tip (a fuse getting longer).
        foreach (var link in _grow.Keys.ToList())
        {
            double g = _grow[link] + dt / 0.45;
            if (g >= 1) { _grow.Remove(link); continue; }
            _grow[link] = g;
            if (Ends(link, out var a, out var b, out var va, out var vb))
            {
                Point tip;
                if (Straight) tip = new Point(a.X + (b.X - a.X) * g, a.Y + (b.Y - a.Y) * g);
                else if (_springs.TryGetValue(link, out var s)) tip = Bezier(a, s.C1, s.C2, b, g);
                else { var (c1, c2) = Controls(a, b, va, vb, link.A, link.B); tip = Bezier(a, c1, c2, b, g); }
                Spawn(tip, ColorOf(link.B), 2, 95);
            }
        }
        if (_grow.Count > 0) moving = true;

        // Retracting ghosts (a removed bubble's links): flail toward the survivor, then get sucked in.
        for (int i = _retracts.Count - 1; i >= 0; i--)
        {
            var r = _retracts[i];
            r.T += dt / 0.85;
            if (r.T >= 1)
            {
                if (Resolve(r.Survivor) is { } srr) Spawn(EdgePoint(srr, r.SurvivorDir), r.ColSurv, 12, 150);
                _retracts.RemoveAt(i);
                continue;
            }
            if (RetractGeo(r, out _, out _, out var fe)) Spawn(fe, r.ColGone, 1, 70);
        }
        if (_retracts.Count > 0) moving = true;

        // Sparks: integrate + fade.
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life += dt;
            if (p.Life >= p.MaxLife) { _particles.RemoveAt(i); continue; }
            p.Pos = new Point(p.Pos.X + p.Vel.X * dt, p.Pos.Y + p.Vel.Y * dt);
            p.Vel = new Vector(p.Vel.X * 0.90, p.Vel.Y * 0.90);
        }
        if (_particles.Count > 0) moving = true;

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
                bool growing = _grow.TryGetValue(link, out var gp);
                if (Straight)
                {
                    var end = growing ? new Point(a.X + (b.X - a.X) * gp, a.Y + (b.Y - a.Y) * gp) : b;
                    ctx.DrawLine(pen, a, end);
                }
                else
                {
                    if (!_springs.TryGetValue(link, out var s))
                    {
                        var (c1t, c2t) = Controls(a, b, va, vb, link.A, link.B);
                        s = new LinkSpring { C1 = c1t, C2 = c2t };
                        _springs[link] = s;
                    }
                    ctx.DrawGeometry(null, pen, growing ? PartialCurve(a, s.C1, s.C2, b, gp) : Curve(a, s.C1, s.C2, b));
                }

                if (!growing && !string.IsNullOrEmpty(link.Label))
                {
                    Point mid = Straight
                        ? new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2)
                        : (_springs.TryGetValue(link, out var sl) ? Bezier(a, sl.C1, sl.C2, b, 0.5)
                                                                  : new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
                    DrawLabel(ctx, mid, link.Label!, a, b, ColorOf(link.A), ColorOf(link.B));
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

        // Retracting ghost links (a removed bubble's connectors, whipping into the survivor).
        foreach (var r in _retracts)
            if (RetractGeo(r, out var anchor, out var ctrl, out var fe))
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(anchor, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(fe, RelativeUnit.Absolute),
                    GradientStops = { new GradientStop(r.ColSurv, 0), new GradientStop(r.ColGone, 1) },
                };
                var pen = new Pen(brush, 2.6, lineCap: PenLineCap.Round);
                var geo = new StreamGeometry();
                using (var g = geo.Open()) { g.BeginFigure(anchor, false); g.QuadraticBezierTo(ctrl, fe); g.EndFigure(false); }
                ctx.DrawGeometry(null, pen, geo);
                ctx.DrawEllipse(new SolidColorBrush(r.ColGone), null, fe, 3.2, 3.2);
            }

        // Sparks on top of everything.
        foreach (var p in _particles)
        {
            double a = 1 - p.Life / p.MaxLife;
            double rad = p.Size * a + 0.5;
            ctx.DrawEllipse(new SolidColorBrush(p.Color, a * 0.9), null, p.Pos, rad, rad);
        }
    }

    private void Spawn(Point at, Color col, int count, double speed)
    {
        for (int i = 0; i < count; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            double sp = speed * (0.35 + _rng.NextDouble() * 0.9);
            _particles.Add(new Particle
            {
                Pos = at, Vel = new Vector(Math.Cos(ang) * sp, Math.Sin(ang) * sp),
                Life = 0, MaxLife = 0.35 + _rng.NextDouble() * 0.45,
                Size = 1.4 + _rng.NextDouble() * 1.9, Color = col,
            });
        }
    }

    /// <summary>Geometry of a retracting ghost link at its current progress: the survivor anchor, a bowed
    /// control point, and the flailing free end sweeping toward the anchor.</summary>
    private bool RetractGeo(Retract r, out Point anchor, out Point ctrl, out Point freeEnd)
    {
        anchor = ctrl = freeEnd = default;
        if (Resolve!(r.Survivor) is not { } sr) return false;
        anchor = EdgePoint(sr, r.SurvivorDir);
        double e = r.T * r.T;                                          // ease-in: drifts, then zips into the survivor
        var to = new Point(r.Start.X + (anchor.X - r.Start.X) * e, r.Start.Y + (anchor.Y - r.Start.Y) * e);
        var perp = Perp(anchor, to);
        double swing = 52 * (1 - r.T) * Math.Sin(r.T * 12);           // wide, slowly-decaying left↔right swing
        freeEnd = new Point(to.X + perp.X * swing * 0.55, to.Y + perp.Y * swing * 0.55);
        // The belly swings MORE than the ends, so the whole string whips side to side like a slack rope.
        var mid = new Point((anchor.X + freeEnd.X) / 2, (anchor.Y + freeEnd.Y) / 2);
        ctrl = new Point(mid.X + perp.X * swing * 1.4, mid.Y + perp.Y * swing * 1.4);
        return true;
    }

    private static Geometry PartialCurve(Point a, Point c1, Point c2, Point b, double t)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(a, false);
        int n = Math.Max(2, (int)(t * 26));
        for (int i = 1; i <= n; i++) g.LineTo(Bezier(a, c1, c2, b, t * i / n));
        g.EndFigure(false);
        return geo;
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

    private void DrawLabel(DrawingContext ctx, Point mid, string text, Point a, Point b, Color colA, Color colB)
    {
        // Text colour flips with the gradient's brightness so it stays legible on light or dark colours.
        var midCol = Color.FromRgb((byte)((colA.R + colB.R) / 2), (byte)((colA.G + colB.G) / 2), (byte)((colA.B + colB.B) / 2));
        double lum = 0.299 * midCol.R + 0.587 * midCol.G + 0.114 * midCol.B;
        var fg = lum > 140 ? Colors.Black : Colors.White;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 12, new SolidColorBrush(fg));
        const double px = 7, py = 3;
        var rect = new Rect(mid.X - ft.Width / 2 - px, mid.Y - ft.Height / 2 - py, ft.Width + px * 2, ft.Height + py * 2);
        var grad = new LinearGradientBrush   // the SAME gradient the connector wears (absolute endpoints)
        {
            StartPoint = new RelativePoint(a, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(b, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(colA, 0), new GradientStop(colB, 1) },
        };
        ctx.DrawRectangle(grad, new Pen(new SolidColorBrush(Colors.White, 0.5), 1), new RoundedRect(rect, 5));
        ctx.DrawText(ft, new Point(rect.X + px, rect.Y + py));
    }

    /// <summary>The link whose drawn line passes closest to <paramref name="p"/> (within a few px), or null.</summary>
    internal MindLink? HitLink(Point p)
    {
        if (Doc is null || Resolve is null) return null;
        MindLink? best = null;
        double bestD = 14;
        foreach (var link in Doc.Links)
        {
            if (!Ends(link, out var a, out var b, out var va, out var vb)) continue;
            double d;
            if (Straight) d = DistToSeg(p, a, b);
            else
            {
                Point c1, c2;
                if (_springs.TryGetValue(link, out var s)) { c1 = s.C1; c2 = s.C2; }
                else (c1, c2) = Controls(a, b, va, vb, link.A, link.B);
                d = MinDistToCurve(a, c1, c2, b, p);
            }
            if (d < bestD) { bestD = d; best = link; }
        }
        return best;
    }

    /// <summary>The midpoint of a link's drawn line (where its label rides).</summary>
    internal Point? LinkMidpoint(MindLink link)
    {
        if (!Ends(link, out var a, out var b, out var va, out var vb)) return null;
        if (Straight) return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        Point c1, c2;
        if (_springs.TryGetValue(link, out var s)) { c1 = s.C1; c2 = s.C2; }
        else (c1, c2) = Controls(a, b, va, vb, link.A, link.B);
        return Bezier(a, c1, c2, b, 0.5);
    }

    private static double DistToSeg(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 < 0.001) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

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
