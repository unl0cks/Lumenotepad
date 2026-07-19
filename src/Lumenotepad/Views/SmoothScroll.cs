using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

/// <summary>Wheel-driven smooth vertical scrolling for a <see cref="ScrollViewer"/>: each notch nudges
/// a target offset and the real offset eases toward it, so the pane glides instead of jumping a line at
/// a time. Honors the reduce-motion pref. Vertical only.
///
/// The ease runs on the compositor's animation-frame callback (<see cref="TopLevel.RequestAnimationFrame"/>),
/// NOT a free-running DispatcherTimer: one update per REAL rendered frame, timed by that frame's own
/// clock. A timer that fires at a fixed 10ms cadence drifts against vsync — offsets get set at moments
/// that don't line up with when frames actually paint — which read as a stutter/jump on a heavier pane
/// (the Customize window's acrylic; owner report). Frame-locked, each rendered frame shows exactly the
/// right offset for its timestamp, so the glide stays even at whatever rate the pane can render.
///
/// Chaining: when the pointer is over an inner scrollable (e.g. the fonts checklist's own ScrollViewer),
/// the wheel is left to native handling so that list scrolls itself.</summary>
public sealed class SmoothScroll
{
    private readonly ScrollViewer _sv;
    private TopLevel? _top;
    private double _target;
    private bool _running;
    private TimeSpan? _last;

    private const double StepPerNotch = 64;      // px of target movement per wheel unit
    // Fraction of the remaining gap closed per 10ms of REAL time; scaled to each frame's actual dt so
    // the pace is identical whether frames land every 8ms or every 30ms.
    private const double CatchUpPer10ms = 0.16;

    private SmoothScroll(ScrollViewer sv)
    {
        _sv = sv;
        // Tunnel: intercept before the ScrollViewer's own bubble-phase line scroll; setting Handled
        // then suppresses the default jump.
        sv.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>Attach smooth vertical wheel scrolling to a ScrollViewer.</summary>
    public static void Attach(ScrollViewer sv) => _ = new SmoothScroll(sv);

    private double MaxOffset => Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        // Ctrl+wheel (zoom intent) and inner scrollables keep native handling.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || OverInnerScrollable(e.Source)) return;

        double max = MaxOffset;
        if (max <= 0) return;                          // nothing to scroll

        if (!_running) _target = _sv.Offset.Y;         // fresh gesture: start from where we actually are
        _target = Math.Clamp(_target - e.Delta.Y * StepPerNotch, 0, max);
        e.Handled = true;

        if (!Motion.Enabled)                           // reduce-motion: jump straight there
        {
            _sv.Offset = new Vector(_sv.Offset.X, _target);
            return;
        }
        _top ??= TopLevel.GetTopLevel(_sv);
        if (_top is null) { _sv.Offset = new Vector(_sv.Offset.X, _target); return; }
        if (!_running) { _running = true; _last = null; _top.RequestAnimationFrame(Frame); }
    }

    private void Frame(TimeSpan now)
    {
        if (!_running) return;
        // dt from the real frame clock (clamped so a long stall settles rather than leaping).
        double dtMs = _last is { } l ? Math.Clamp((now - l).TotalMilliseconds, 1, 40) : 16;
        _last = now;
        double factor = 1 - Math.Pow(1 - CatchUpPer10ms, dtMs / 10.0);

        double max = MaxOffset;
        double cur = _sv.Offset.Y;
        double next = cur + (Math.Clamp(_target, 0, max) - cur) * factor;
        if (Math.Abs(Math.Clamp(_target, 0, max) - next) < 0.5)
        {
            _sv.Offset = new Vector(_sv.Offset.X, Math.Clamp(_target, 0, max));
            _running = false;
            return;
        }
        _sv.Offset = new Vector(_sv.Offset.X, next);
        _top?.RequestAnimationFrame(Frame);            // one callback per frame — re-arm for the next
    }

    /// <summary>True when the wheel is over a nested scrollable between the source and our ScrollViewer,
    /// so we leave the event alone and let that inner control scroll natively.</summary>
    private bool OverInnerScrollable(object? source)
    {
        var v = source as Visual;
        while (v is not null && !ReferenceEquals(v, _sv))
        {
            if (v is ScrollViewer) return true;        // e.g. the ScrollViewer inside the fonts ListBox
            v = v.GetVisualParent();
        }
        return false;
    }
}
