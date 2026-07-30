using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Controls;

public static class Squircle
{

    public const double Exponent = 4.0;

    public static Geometry RoundedSquircle(double w, double h, double radius, double n = Exponent, int perCorner = 14)
    {
        double r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (r < 0.75)
            {
                ctx.BeginFigure(new Point(0, 0), true);
                ctx.LineTo(new Point(w, 0));
                ctx.LineTo(new Point(w, h));
                ctx.LineTo(new Point(0, h));
                ctx.EndFigure(true);
                return geo;
            }

            bool started = false;

            Corner(ctx, w - r, r, +1, -1, true, r, n, perCorner, ref started);
            Corner(ctx, w - r, h - r, +1, +1, false, r, n, perCorner, ref started);
            Corner(ctx, r, h - r, -1, +1, true, r, n, perCorner, ref started);
            Corner(ctx, r, r, -1, -1, false, r, n, perCorner, ref started);
            ctx.EndFigure(true);
        }
        return geo;
    }

    private static void Corner(StreamGeometryContext ctx, double cx, double cy, double sx, double sy,
                               bool reverse, double r, double n, int steps, ref bool started)
    {
        for (int i = 0; i <= steps; i++)
        {
            int idx = reverse ? steps - i : i;
            double a = Math.PI / 2 * idx / steps;
            double c = Math.Pow(Math.Cos(a), 2.0 / n);
            double s = Math.Pow(Math.Sin(a), 2.0 / n);
            var p = new Point(cx + sx * r * c, cy + sy * r * s);
            if (!started) { ctx.BeginFigure(p, true); started = true; }
            else ctx.LineTo(p);
        }
    }

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Squircle));

    public static readonly AttachedProperty<double> RadiusProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Radius", typeof(Squircle), double.NaN);

    public static void SetEnabled(Control c, bool v) => c.SetValue(EnabledProperty, v);
    public static bool GetEnabled(Control c) => c.GetValue(EnabledProperty);
    public static void SetRadius(Control c, double v) => c.SetValue(RadiusProperty, v);
    public static double GetRadius(Control c) => c.GetValue(RadiusProperty);

    static Squircle()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.GetNewValue<bool>()) Attach(c);
        });
    }

    private static void Attach(Control c)
    {
        UpdateClip(c);
        c.PropertyChanged += (_, e) => { if (e.Property == Visual.BoundsProperty) UpdateClip(c); };
    }

    private static void UpdateClip(Control c)
    {
        double w = c.Bounds.Width, h = c.Bounds.Height;
        if (w <= 1 || h <= 1) { c.Clip = null; return; }
        double radius = c.GetValue(RadiusProperty);
        if (double.IsNaN(radius)) radius = Math.Min(Math.Min(w, h) * 0.42, 16);
        c.Clip = RoundedSquircle(w, h, radius);
    }
}
