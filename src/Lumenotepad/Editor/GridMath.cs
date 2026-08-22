using System;

namespace Lumenotepad.Editor;

public static class GridMath
{

    public const double Cell = 20;

    public const double ChromeTop = 21;

    public static double Snap(double v) => Step(v, Cell);

    public static bool IsRuled(string gridStyle) =>
        gridStyle is PageStyles.Ruled or PageStyles.RuledWide;

    public static double StepFor(string gridStyle) => gridStyle switch
    {
        PageStyles.Ruled => PageStyleGuides.RuleSpacing,
        PageStyles.RuledWide => PageStyleGuides.RuleSpacingWide,
        _ => Cell,
    };

    public static double SnapX(double v, string gridStyle) =>
        IsRuled(gridStyle) ? v : Step(v, Cell);

    public static double SnapHeight(double v, string gridStyle) =>
        Step(v, StepFor(gridStyle));

    public static double SnapY(double v, string gridStyle, double firstLineHeight = 0)
    {
        double step = StepFor(gridStyle);
        if (!IsRuled(gridStyle)) return Step(v, step);
        double phase = Phase(step, firstLineHeight);
        return Step(v - phase, step) + phase;
    }

    private static double Phase(double step, double lineHeight)
    {
        double p = (step - lineHeight) / 2 - ChromeTop;
        p %= step;
        if (p < 0) p += step;
        return p;
    }

    private static double Step(double v, double step) => Math.Round(v / step) * step;
}
