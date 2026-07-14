using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

/// <summary>Wheel-driven smooth vertical scrolling for a <see cref="ScrollViewer"/>: each notch nudges
/// a target offset and a per-frame timer eases the real offset toward it, so the pane glides instead
/// of jumping a line at a time. Uses the same code-behind tween approach as <see cref="Motion"/>
/// (declarative Offset animation isn't a thing here) and honors the reduce-motion pref. Vertical only.
///
/// Chaining: when the pointer is over an inner scrollable (e.g. the fonts checklist's own ScrollViewer),
/// the wheel is left to native handling so that list scrolls itself.</summary>
public sealed class SmoothScroll
{
    private readonly ScrollViewer _sv;
    private DispatcherTimer? _timer;
    private double _target;

    private const double StepPerNotch = 64;   // px of target movement per wheel unit
    // Fraction of the remaining gap closed each tick. Scaled down from 0.22/15ms to keep the same
    // easing feel now that ticks fire more often (0.16 per 10ms).
    private const double CatchUp = 0.16;

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

        // Fresh gesture (timer idle): start the target from where we actually are.
        if (_timer is null || !_timer.IsEnabled) _target = _sv.Offset.Y;
        _target = Math.Clamp(_target - e.Delta.Y * StepPerNotch, 0, max);
        e.Handled = true;

        if (!Motion.Enabled)                           // reduce-motion: jump straight there
        {
            _sv.Offset = new Vector(_sv.Offset.X, _target);
            return;
        }
        _timer ??= CreateTimer();
        if (!_timer.IsEnabled) _timer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        // Render priority + 10ms ticks: a default-priority 15ms timer queues behind input/layout
        // work and visibly stutters (owner report). Render priority fires right before the frame.
        var t = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Render, (_, _) => Tick());
        return t;
    }

    private void Tick()
    {
        double cur = _sv.Offset.Y;
        double next = cur + (_target - cur) * CatchUp;
        if (Math.Abs(_target - next) < 0.5)
        {
            _sv.Offset = new Vector(_sv.Offset.X, _target);
            _timer?.Stop();
            return;
        }
        _sv.Offset = new Vector(_sv.Offset.X, next);
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
