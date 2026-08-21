using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lumenotepad.Platform;

public static class AdaptiveCursors
{
    private static Cursor? _move, _nwse, _nesw;

    public static Cursor For(StandardCursorType type)
    {
        if (!OperatingSystem.IsMacOS()) return new Cursor(type);
        try
        {
            return type switch
            {
                StandardCursorType.SizeAll => _move ??= Build(0, both: true),
                StandardCursorType.TopLeftCorner or StandardCursorType.BottomRightCorner
                    => _nwse ??= Build(45, both: false),
                StandardCursorType.TopRightCorner or StandardCursorType.BottomLeftCorner
                    => _nesw ??= Build(-45, both: false),
                _ => new Cursor(type),
            };
        }
        catch
        {
            return new Cursor(type);
        }
    }

    private static Cursor Build(double angleDegrees, bool both)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var arrows = ArrowGeometry(both);
            var transform = Matrix.CreateRotation(Math.PI * angleDegrees / 180) * Matrix.CreateTranslation(16, 16);
            using (ctx.PushTransform(transform))
            {
                ctx.DrawGeometry(null, new Pen(Brushes.White, 3.4) { LineJoin = PenLineJoin.Round }, arrows);
                ctx.DrawGeometry(Brushes.Black, null, arrows);
            }
        }
        return new Cursor(bitmap, new PixelPoint(16, 16));
    }

    private static StreamGeometry ArrowGeometry(bool both)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(FillRule.NonZero);
        AddDoubleArrow(c, vertical: false);
        if (both) AddDoubleArrow(c, vertical: true);
        return g;
    }

    private static void AddDoubleArrow(StreamGeometryContext c, bool vertical)
    {
        Point P(double a, double b) => vertical ? new Point(b, a) : new Point(a, b);
        c.BeginFigure(P(-14, 0), true);
        c.LineTo(P(-7, -5));
        c.LineTo(P(-7, -2));
        c.LineTo(P(7, -2));
        c.LineTo(P(7, -5));
        c.LineTo(P(14, 0));
        c.LineTo(P(7, 5));
        c.LineTo(P(7, 2));
        c.LineTo(P(-7, 2));
        c.LineTo(P(-7, 5));
        c.EndFigure(true);
    }
}
