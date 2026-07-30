using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

public sealed class SmoothScroll
{
    private readonly ScrollViewer _sv;
    private TopLevel? _top;
    private double _target;
    private double _targetX;
    private bool _running;
    private TimeSpan? _last;

    private const double StepPerNotch = 64;

    private const double CatchUpPer10ms = 0.16;

    private SmoothScroll(ScrollViewer sv)
    {
        _sv = sv;

        sv.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    public static void Attach(ScrollViewer sv) => _ = new SmoothScroll(sv);

    private double MaxOffset => Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height);
    private double MaxOffsetX => Math.Max(0, _sv.Extent.Width - _sv.Viewport.Width);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {

        if (Services.Keymap.HasCommandStrict(e.KeyModifiers) || OverInnerScrollable(e.Source)) return;

        bool sideways = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double max = sideways ? MaxOffsetX : MaxOffset;
        if (max <= 0) return;

        if (!_running) { _target = _sv.Offset.Y; _targetX = _sv.Offset.X; }
        double notch = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (sideways) _targetX = Math.Clamp(_targetX - notch * StepPerNotch, 0, max);
        else _target = Math.Clamp(_target - notch * StepPerNotch, 0, max);
        e.Handled = true;

        if (!Motion.Enabled)
        {
            _sv.Offset = new Vector(Math.Clamp(_targetX, 0, MaxOffsetX), Math.Clamp(_target, 0, MaxOffset));
            return;
        }
        _top ??= TopLevel.GetTopLevel(_sv);
        if (_top is null)
        {
            _sv.Offset = new Vector(Math.Clamp(_targetX, 0, MaxOffsetX), Math.Clamp(_target, 0, MaxOffset));
            return;
        }
        if (!_running) { _running = true; _last = null; _top.RequestAnimationFrame(Frame); }
    }

    private void Frame(TimeSpan now)
    {
        if (!_running) return;

        double dtMs = _last is { } l ? Math.Clamp((now - l).TotalMilliseconds, 1, 40) : 16;
        _last = now;
        double factor = 1 - Math.Pow(1 - CatchUpPer10ms, dtMs / 10.0);

        double goalY = Math.Clamp(_target, 0, MaxOffset);
        double goalX = Math.Clamp(_targetX, 0, MaxOffsetX);
        double nextY = _sv.Offset.Y + (goalY - _sv.Offset.Y) * factor;
        double nextX = _sv.Offset.X + (goalX - _sv.Offset.X) * factor;
        if (Math.Abs(goalY - nextY) < 0.5 && Math.Abs(goalX - nextX) < 0.5)
        {
            _sv.Offset = new Vector(goalX, goalY);
            _running = false;
            return;
        }
        _sv.Offset = new Vector(nextX, nextY);
        _top?.RequestAnimationFrame(Frame);
    }

    private bool OverInnerScrollable(object? source)
    {
        var v = source as Visual;
        while (v is not null && !ReferenceEquals(v, _sv))
        {
            if (v is ScrollViewer inner && inner.Extent.Height - inner.Viewport.Height > 1)
                return true;
            v = v.GetVisualParent();
        }
        return false;
    }
}
