using System;
using Avalonia;

namespace Lumenotepad.Platform;

/// <summary>Pure window-geometry maths, kept out of the window class so it can be tested directly.</summary>
public static class WindowGeometry
{
    /// <summary>How far a maximized window overhangs the monitor work area, expressed as a content inset
    /// in LOGICAL units. Native Aero Snap parks a WS_THICKFRAME window at (-7,-7) and oversizes it by 14
    /// per axis, so a snapped window bleeds ~7px past every screen edge and clips its own title bar; the
    /// maximize BUTTON constrains cleanly instead. Insetting the content by the overhang makes the two
    /// land identically. Avalonia leaves OffScreenMargin at 0 here, hence the hand-computed geometry.
    ///
    /// WINDOWS ONLY — see the caller. <paramref name="work"/> and <paramref name="position"/> are physical
    /// pixels while <paramref name="clientSize"/> is logical, and that mix does not hold on macOS: there a
    /// zoomed window yields an inset hundreds of points deep, which crushed the whole UI into the top-left
    /// corner (tester report). Feeding it macOS values is meaningless, not merely imprecise.</summary>
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
