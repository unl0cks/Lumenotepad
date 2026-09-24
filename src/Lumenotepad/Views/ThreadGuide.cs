using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Views;

public sealed class ThreadGuide : Control
{
    public static readonly StyledProperty<int> LevelProperty =
        AvaloniaProperty.Register<ThreadGuide, int>(nameof(Level));

    public static readonly StyledProperty<bool> IsLastSiblingProperty =
        AvaloniaProperty.Register<ThreadGuide, bool>(nameof(IsLastSibling), true);

    public static readonly StyledProperty<int> GuideMaskProperty =
        AvaloniaProperty.Register<ThreadGuide, int>(nameof(GuideMask));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ThreadGuide, IBrush?>(nameof(Stroke));

    static ThreadGuide()
    {
        AffectsRender<ThreadGuide>(LevelProperty, IsLastSiblingProperty, GuideMaskProperty, StrokeProperty);
    }

    public int Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsLastSibling { get => GetValue(IsLastSiblingProperty); set => SetValue(IsLastSiblingProperty, value); }
    public int GuideMask { get => GetValue(GuideMaskProperty); set => SetValue(GuideMaskProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public double IndentStep { get; set; } = 24;
    public double PadLeft { get; set; } = 9;
    public double RowGap { get; set; } = 18;

    public override void Render(DrawingContext context)
    {
        int level = Level;
        if (level <= 0 || Stroke is not { } brush) return;
        double h = Bounds.Height;
        double top = -RowGap + 2, bottom = h + 2, mid = Math.Round(h / 2) + 0.5;
        double spine = -IndentStep + 3.5;
        double end = -PadLeft - 2;
        double r = Math.Max(1, Math.Min(8, end - spine));

        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            for (int k = 1; k < level; k++)
            {
                if ((GuideMask & (1 << k)) == 0) continue;
                double x = spine - (level - k) * IndentStep;
                c.BeginFigure(new Point(x, top), false);
                c.LineTo(new Point(x, bottom));
                c.EndFigure(false);
            }
            c.BeginFigure(new Point(spine, top), false);
            c.LineTo(new Point(spine, mid - r));
            c.ArcTo(new Point(spine + r, mid), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
            c.LineTo(new Point(end, mid));
            c.EndFigure(false);
            if (!IsLastSibling)
            {
                c.BeginFigure(new Point(spine, mid - r), false);
                c.LineTo(new Point(spine, bottom));
                c.EndFigure(false);
            }
        }
        context.DrawGeometry(null, new Pen(brush, 1.5, lineCap: PenLineCap.Round), shape);
    }
}
