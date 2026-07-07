using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;

namespace Lumenotepad.Controls;

/// <summary>Draws a TextBox's selection as ROUNDED rects. The stock TextPresenter paints square
/// selection with no styling hook and its Render is sealed — so this control sits UNDER the presenter
/// in the template (same panel, first child), reads its selection + TextLayout, and paints rounded
/// rects itself while the TextBox's real SelectionBrush is transparent.</summary>
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
        catch { /* layout mid-update — skip this frame */ }
    }
}
