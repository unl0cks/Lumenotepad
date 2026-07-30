using System;
using Avalonia;

namespace Lumenotepad.Platform;

public static class WindowGeometry
{

    public static Thickness MaximizeInset(PixelRect work, PixelPoint position, Size clientSize, double scaling)
    {
        double s = scaling <= 0 ? 1 : scaling;
        double physW = clientSize.Width * s, physH = clientSize.Height * s;
        double left   = Math.Max(0, work.X - position.X);
        double top    = Math.Max(0, work.Y - position.Y);
        double right  = Math.Max(0, (position.X + physW) - (work.X + work.Width));
        double bottom = Math.Max(0, (position.Y + physH) - (work.Y + work.Height));
        return new Thickness(left / s, top / s, right / s, bottom / s);
    }
}
