using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumenotepad.Controls;

public sealed class TextSelectionUnderlay : Control
{
    public static readonly StyledProperty<TextPresenter?> PresenterProperty =
        AvaloniaProperty.Register<TextSelectionUnderlay, TextPresenter?>(nameof(Presenter));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<TextSelectionUnderlay, IBrush?>(nameof(Brush));

    public TextPresenter? Presenter
    {
        get => GetValue(PresenterProperty);
        set => SetValue(PresenterProperty, value);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    static TextSelectionUnderlay()
    {
        AffectsRender<TextSelectionUnderlay>(BrushProperty);
        PresenterProperty.Changed.AddClassHandler<TextSelectionUnderlay>((o, e) => o.Hook(e));
    }

    public TextSelectionUnderlay() => IsHitTestVisible = false;

    private void Hook(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is TextPresenter old) old.PropertyChanged -= OnPresenterChanged;
        if (e.NewValue is TextPresenter p) p.PropertyChanged += OnPresenterChanged;
        InvalidateVisual();
    }

    private void OnPresenterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.SelectionStartProperty ||
            e.Property == TextPresenter.SelectionEndProperty ||
            e.Property == TextPresenter.TextProperty ||
            e.Property == Visual.BoundsProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Presenter is not { } p || Brush is not { } brush) return;
        int start = Math.Min(p.SelectionStart, p.SelectionEnd);
        int length = Math.Abs(p.SelectionEnd - p.SelectionStart);
        if (length <= 0) return;
        try
        {
            var offset = p.TranslatePoint(new Point(0, 0), this) ?? default;
            foreach (var r in p.TextLayout.HitTestTextRange(start, length))
                context.FillRectangle(brush,
                    new Rect(r.X + offset.X, r.Y + offset.Y, Math.Max(r.Width, 2), r.Height), 3f);
        }
        catch {  }
    }
}

public sealed class GlidingCaret : Control
{
    public static readonly StyledProperty<TextPresenter?> PresenterProperty =
        AvaloniaProperty.Register<GlidingCaret, TextPresenter?>(nameof(Presenter));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<GlidingCaret, IBrush?>(nameof(Brush));

    public TextPresenter? Presenter
    {
        get => GetValue(PresenterProperty);
        set => SetValue(PresenterProperty, value);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private TextBox? _host;
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Rect _display;
    private bool _seeded;
    private double _opacity = 1, _blinkMs;

    static GlidingCaret()
    {
        PresenterProperty.Changed.AddClassHandler<GlidingCaret>((o, e) => o.Hook(e));
    }

    public GlidingCaret()
    {
        IsHitTestVisible = false;
        _anim.Tick += (_, _) => Tick();
    }

    private void Hook(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is TextPresenter old) old.PropertyChanged -= OnPresenterChanged;
        if (e.NewValue is TextPresenter p) p.PropertyChanged += OnPresenterChanged;
    }

    private void OnPresenterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.CaretIndexProperty || e.Property == TextPresenter.TextProperty)
        {
            _blinkMs = 0; _opacity = 1;
            if (_host?.IsFocused == true && !_anim.IsEnabled) _anim.Start();
            InvalidateVisual();
        }
        else if (e.Property == Visual.BoundsProperty)
            InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = this.FindAncestorOfType<TextBox>();
        if (_host is null) return;
        _host.GotFocus += OnHostGotFocus;
        _host.LostFocus += OnHostLostFocus;
        if (_host.IsFocused) OnHostGotFocus(null, null!);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _anim.Stop();
        if (_host is not null)
        {
            _host.GotFocus -= OnHostGotFocus;
            _host.LostFocus -= OnHostLostFocus;
            _host = null;
        }
    }

    private void OnHostGotFocus(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _blinkMs = 0; _opacity = 1; _seeded = false;
        _anim.Start();
        InvalidateVisual();
    }

    private void OnHostLostFocus(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _anim.Stop();
        InvalidateVisual();
    }

    private void Tick()
    {
        if (_host is null || !_host.IsFocused) { _anim.Stop(); InvalidateVisual(); return; }

        bool dirty = false;
        var target = CaretRect();
        if (target != default)
        {
            if (!_seeded) { _display = target; _seeded = true; dirty = true; }
            else if (Math.Abs(_display.X - target.X) + Math.Abs(_display.Y - target.Y)
                   + Math.Abs(_display.Height - target.Height) > 0.15)
            {
                double k = Views.Motion.Enabled ? 0.35 : 1;
                _display = new Rect(
                    Lerp(_display.X, target.X, k), Lerp(_display.Y, target.Y, k),
                    target.Width, Lerp(_display.Height, target.Height, k));
                dirty = true;
            }
        }

        _blinkMs += 16;
        double o = BlinkOpacity();
        if (Math.Abs(o - _opacity) > 0.01) { _opacity = o; dirty = true; }

        if (dirty) InvalidateVisual();
    }

    private double BlinkOpacity()
    {
        if (!Editor.RichTextEditor.CaretBlinkPref) return 1;
        const double On = 600, Fade = 180, DimHold = 320, Dim = 0.12;
        double t = _blinkMs % (On + Fade + DimHold + Fade);
        if (!Views.Motion.Enabled) return t < On + Fade ? 1 : 0;
        if (t < On) return 1;
        t -= On;
        if (t < Fade) return 1 - (1 - Dim) * (t / Fade);
        t -= Fade;
        if (t < DimHold) return Dim;
        t -= DimHold;
        return Dim + (1 - Dim) * (t / Fade);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private Rect CaretRect()
    {
        if (Presenter is not { } p) return default;
        try
        {
            var offset = p.TranslatePoint(new Point(0, 0), this) ?? default;
            var hit = p.TextLayout.HitTestTextPosition(Math.Max(0, p.CaretIndex));
            double h = hit.Height > 1 ? hit.Height : p.FontSize * 1.35;
            return new Rect(hit.X + offset.X, hit.Y + offset.Y, Editor.RichTextEditor.CaretWidthPref, h);
        }
        catch { return default; }
    }

    public override void Render(DrawingContext context)
    {
        if (Brush is not { } brush || _host is null || !_host.IsFocused || _opacity < 0.01) return;
        var r = _seeded ? _display : CaretRect();
        if (r == default) return;
        using (context.PushOpacity(_opacity))
            context.FillRectangle(brush, r, 0.8f);
    }
}
