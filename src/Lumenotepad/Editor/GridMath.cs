using System;

namespace Lumenotepad.Editor;

public static class GridMath
{

    public const double Cell = 20;

    public static double Snap(double v) => Math.Round(v / Cell) * Cell;
}
