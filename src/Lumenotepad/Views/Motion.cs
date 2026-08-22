using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

public static class Motion
{
    public const int Fast = 220, Base = 360, Slow = 520;
    public const double Rise = 12;

    public static bool Enabled { get; set; } = true;
    public static double SpeedScale { get; set; } = 1.0;
    public static int Ms(int ms) => Math.Max(1, (int)Math.Round(ms * SpeedScale));

    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseOutSoft(double t) => 1 - Math.Pow(1 - t, 5);
    public static double EaseIn(double t) => t * t * t;

    public static double Smooth(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static readonly Dictionary<Visual, DispatcherTimer> Tweens = new();

    private sealed class FrameRun { public bool Cancelled; }

    private static readonly Dictionary<Visual, FrameRun> Runs = new();

    public static void Stop(Visual v)
    {
        if (Tweens.TryGetValue(v, out var t)) { t.Stop(); Tweens.Remove(v); }
        if (Runs.TryGetValue(v, out var r)) { r.Cancelled = true; Runs.Remove(v); }
    }

    public static bool LogFrames { get; set; } =
        Environment.GetEnvironmentVariable("LUMENOTEPAD_FRAMELOG") == "1";

    private static void WriteFrameLog(string label, List<double> stamps)
    {
        try
        {
            if (stamps.Count < 2) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(label).Append("  frames=").Append(stamps.Count)
              .Append("  total=").Append(stamps[^1].ToString("F1")).AppendLine("ms");
            double worst = 0;
            for (int i = 1; i < stamps.Count; i++)
            {
                double d = stamps[i] - stamps[i - 1];
                worst = Math.Max(worst, d);
                sb.Append(d.ToString("F1")).Append(' ');
            }
            sb.AppendLine().Append("  worst gap = ").Append(worst.ToString("F1")).AppendLine(" ms");
            var dir = Services.AppSettings.DefaultDir;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "frames.log"), sb.ToString());
        }
        catch {  }
    }

    private static bool FrameClock(Visual anchor, int ms, Action<double> frame, Action done)
    {
        if (TopLevel.GetTopLevel(anchor) is not { } top) return false;
        double dur = Math.Max(1, Ms(ms));
        var run = new FrameRun();
        Runs[anchor] = run;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool finished = false;
        var stamps = LogFrames ? new List<double>() : null;

        void Finish()
        {
            if (finished || run.Cancelled) return;
            finished = true;
            if (Runs.TryGetValue(anchor, out var cur) && ReferenceEquals(cur, run)) Runs.Remove(anchor);
            if (stamps is not null) WriteFrameLog(anchor.GetType().Name, stamps);
            done();
        }

        void Step(TimeSpan _)
        {
            if (run.Cancelled || finished) return;
            double p = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / dur);
            stamps?.Add(sw.Elapsed.TotalMilliseconds);
            frame(p);
            if (p < 1) { top.RequestAnimationFrame(Step); return; }
            Finish();
        }

        top.RequestAnimationFrame(Step);

        DispatcherTimer.RunOnce(() =>
        {
            if (finished || run.Cancelled) return;
            frame(1);
            Finish();
        }, TimeSpan.FromMilliseconds(dur * 3 + 250));
        return true;
    }

    public static DispatcherTimer Clock(int ms, Action<double> frame, Action done, int intervalMs = 10)
    {
        double dur = Math.Max(1, Ms(ms));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        timer.Tick += (_, _) =>
        {
            double p = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / dur);
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

        void Done()
        {
            bool rest = Math.Abs(ts - 1) < 1e-3 && Math.Abs(tx) < 1e-3 && Math.Abs(ty) < 1e-3;
            if (rest) { c.ClearValue(Visual.RenderTransformProperty); c.ClearValue(Animatable.TransitionsProperty); }
            if (toOpacity is double t1) c.Opacity = t1;
            onDone?.Invoke();
        }

        if (FrameClock(c, ms, p => Frame(ease(p)), Done)) return;
        Tweens[c] = Clock(ms, p => Frame(ease(p)), done: () => { Tweens.Remove(c); Done(); });
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

    public const double Slide = 8;

    private static readonly Dictionary<Control, double> Offsets = new();

    public static void Reflow(Panel panel, IReadOnlyList<(Control Row, bool Show)> targets, int ms = 260)
    {
        Stop(panel);

        var kids = new List<Control>();
        foreach (var child in panel.Children) if (child is Control k) kids.Add(k);

        var oldY = new Dictionary<Control, double>();
        var wasVisible = new Dictionary<Control, bool>();
        foreach (var k in kids) { oldY[k] = k.Bounds.Y; wasVisible[k] = k.IsVisible; }
        double oldScroll = ScrollY(panel);

        foreach (var (row, show) in targets)
        {
            row.ClearValue(Layoutable.HeightProperty);
            row.ClearValue(Layoutable.MarginProperty);
            row.IsVisible = show;
        }

        panel.UpdateLayout();

        double scrolled = ScrollY(panel) - oldScroll;

        var moving = new List<(Control C, double Dy)>();
        var entering = new List<Control>();
        foreach (var k in kids)
        {
            Stop(k);
            double held = Offsets.TryGetValue(k, out var o) ? o : 0;
            Offsets.Remove(k);
            if (!k.IsVisible) { k.ClearValue(Visual.RenderTransformProperty); continue; }
            k.RenderTransformOrigin = RelativePoint.Center;
            if (!wasVisible[k]) { entering.Add(k); continue; }

            k.Opacity = 1;
            double dy = oldY[k] - k.Bounds.Y + held - scrolled;
            if (Math.Abs(dy) >= 0.5) moving.Add((k, dy));
            else k.ClearValue(Visual.RenderTransformProperty);
        }

        if (!Enabled || (moving.Count == 0 && entering.Count == 0))
        {
            Land(moving, entering);
            return;
        }

        foreach (var k in entering) k.Opacity = 0;

        void Frame(double p)
        {
            double e = Smooth(p);
            foreach (var (c, dy) in moving)
            {
                double at = dy * (1 - e);
                Offsets[c] = at;
                c.RenderTransform = Make(0, at, 1);
            }
            foreach (var c in entering)
            {
                c.Opacity = e;
                c.RenderTransform = Make(-Slide * (1 - e), 0, 1);
            }
        }

        Frame(0);
        void Done() => Land(moving, entering);
        if (FrameClock(panel, ms, Frame, Done)) return;
        Tweens[panel] = Clock(ms, Frame, done: () => { Tweens.Remove(panel); Done(); }, intervalMs: 6);
    }

    private static void Land(List<(Control C, double Dy)> moving, List<Control> entering)
    {
        foreach (var (c, _) in moving) { Offsets.Remove(c); c.ClearValue(Visual.RenderTransformProperty); }
        foreach (var c in entering) { c.Opacity = 1; c.ClearValue(Visual.RenderTransformProperty); }
    }

    private static double ScrollY(Visual v) =>
        v.FindAncestorOfType<ScrollViewer>() is { } sv ? sv.Offset.Y : 0;

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
