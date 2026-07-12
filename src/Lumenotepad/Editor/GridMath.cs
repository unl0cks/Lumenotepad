using System;

namespace Lumenotepad.Editor;

/// <summary>The canvas paper-grid lattice. Pure math — kept off the UI types so it unit-tests
/// without an Avalonia platform.</summary>
public static class GridMath
{
    /// <summary>Grid cell edge in canvas pixels. The drawn grid and the snap share it, so a
    /// snapped container lands exactly on the dots/lines.</summary>
    public const double Cell = 20;

    /// <summary>Nearest lattice point (callers clamp to their own bounds afterwards).</summary>
    public static double Snap(double v) => Math.Round(v / Cell) * Cell;
}
