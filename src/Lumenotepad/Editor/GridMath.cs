using System;

namespace Lumenotepad.Editor;

public static class GridMath
{

    public const double Cell = 20;

    public static double Snap(double v) => Step(v, Cell);

    public static double SnapX(double v, string gridStyle) =>
        gridStyle == PageStyles.Ruled ? v : Step(v, Cell);

    public static double SnapY(double v, string gridStyle) =>
        Step(v, gridStyle == PageStyles.Ruled ? PageStyleGuides.RuleSpacing : Cell);

    private static double Step(double v, double step) => Math.Round(v / step) * step;
}
