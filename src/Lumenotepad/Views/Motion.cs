using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Lumenotepad.Views;

/// <summary>The app's single motion engine. Transforms (scale/translate) MUST be driven here as
/// LOCAL values tweened per-frame — this build's RenderTransform Transitions/Animation are dead
/// (see docs/superpowers/specs/2026-07-09-motion-system-design.md). Opacity is tweened in the same
/// loop. One tween per element; a new tween on the same element cancels the old.</summary>
public static class Motion
{
    public const int Fast = 220, Base = 360, Slow = 520;
    public const double Rise = 12;

    /// <summary>Prefs: master motion switch + global speed. When disabled every tween snaps
    /// straight to its final frame (and still fires onDone) so callers never special-case it.</summary>
    public static bool Enabled { get; set; } = true;
    public static double SpeedScale { get; set; } = 1.0;    // Calm 1.4 / Normal 1.0 / Snappy 0.6
    public static int Ms(int ms) => Math.Max(1, (int)Math.Round(ms * SpeedScale));

    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseOutSoft(double t) => 1 - Math.Pow(1 - t, 5);   // stronger deceleration — no hard stop
    public static double EaseIn(double t) => t * t * t;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static readonly Dictionary<Visual, DispatcherTimer> Tweens = new();

    public static void Stop(Visual v)
    {
        if (Tweens.TryGetValue(v, out var t)) { t.Stop(); Tweens.Remove(v); }
    }

    /// <summary>The tween clock every frame loop here runs on: REAL-TIME progress (a Stopwatch, not
    /// a tick count), so when a frame is expensive — the solid themes repaint an opaque plate with a
    /// punched acrylic hole, which made step-counted tweens visibly stretch (owner report) — the
    /// animation drops frames but keeps its duration. Render priority fires just before the frame
    /// instead of queueing behind input/layout (the SmoothScroll lesson).</summary>
    public static DispatcherTimer Clock(int ms, Action<double> frame, Action done)
    {
        double dur = Math.Max(1, Ms(ms));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            double p = Math.Min(1.0, sw.ElapsedMilliseconds / dur);
            frame(p);
            if (p >= 1) { timer.Stop(); done(); }
        };
        timer.Start();
        return timer;
    }

    private static ITransform Make(double tx, double ty, double s)
    {
        var b = TransformOperations.CreateBuilder(2);
        b.AppendTranslate(tx, ty);
        b.AppendScale(s, s);
        return b.Build();
    }

    /// <summary>Tween translate+scale (and optionally opacity) from a start to a target over ms. At
    /// rest at identity the RenderTransform is cleared. onDone always fires at the end.</summary>
    public static void Tween(Control c, double fx, double fy, double fs, double tx, double ty, double ts,
                             int ms, Func<double, double>? ease = null, double? fromOpacity = null,
                             double? toOpacity = null, Action? onDone = null)
    {
        Stop(c);
        c.Transitions = null;
        ease ??= EaseOut;
        if (!Enabled)
        {
            c.RenderTransform = Make(tx, ty, ts);
            bool restNow = Math.Abs(ts - 1) < 1e-3 && Math.Abs(tx) < 1e-3 && Math.Abs(ty) < 1e-3;
            if (restNow) { c.ClearValue(Visual.RenderTransformProperty); c.ClearValue(Animatable.TransitionsProperty); }
            if (toOpacity is double o) c.Opacity = o;
            onDone?.Invoke();
            return;
        }
        void Frame(double e)
        {
            c.RenderTransform = Make(Lerp(fx, tx, e), Lerp(fy, ty, e), Lerp(fs, ts, e));
            if (fromOpacity is double o0 && toOpacity is double o1) c.Opacity = Lerp(o0, o1, e);
        }
        Frame(0);
        Tweens[c] = Clock(ms, p => Frame(ease(p)), done: () =>
        {
            Tweens.Remove(c);
            bool rest = Math.Abs(ts - 1) < 1e-3 && Math.Abs(tx) < 1e-3 && Math.Abs(ty) < 1e-3;
            if (rest) { c.ClearValue(Visual.RenderTransformProperty); c.ClearValue(Animatable.TransitionsProperty); }
            if (toOpacity is double t1) c.Opacity = t1;
            onDone?.Invoke();
        });
    }

    public static void FadeIn(Control c, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void RiseIn(Control c, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, Rise, 1, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void ScaleIn(Control c, double from = 0.96, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, from, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void FadeOut(Control c, int ms = Base, Action? onDone = null)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 1, ms, EaseIn, c.Opacity, 0, onDone); }

    public static void CollapseOut(Control c, int ms = Base, Action? onDone = null)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 0.92, ms, EaseIn, c.Opacity, 0, onDone); }

    /// <summary>Collapse/expand a side panel: animate its Width (to fullWidth or 0) and fade, for the
    /// rail / pages toggle. Width is a layout property so this re-lays-out each frame — fine for a
    /// narrow occasional panel.</summary>
    public static void Reveal(Control c, double fullWidth, bool show, int ms = Base)
    {
        Stop(c);
        double fromW = double.IsNaN(c.Width) ? c.Bounds.Width : c.Width;
        double toW = show ? fullWidth : 0, fromO = c.Opacity, toO = show ? 1 : 0;
        if (!Enabled) { c.Width = toW; c.Opacity = toO; return; }
        Tweens[c] = Clock(ms, p =>
        {
            double e = EaseOut(p);
            c.Width = Lerp(fromW, toW, e);
            c.Opacity = Lerp(fromO, toO, e);
        }, done: () => { Tweens.Remove(c); c.Width = toW; c.Opacity = toO; });
    }
}
