using Lumenotepad.Models;

namespace Lumenotepad.Editor;

public static class PageStyles
{

    public const string Blank = "Blank", Ruled = "Ruled", RuledWide = "Wide ruled", Grid = "Grid", Dots = "Dots";
    public static readonly string[] GridStyles = { Blank, Ruled, RuledWide, Grid, Dots };

    public const string Freeform = "Freeform", Cornell = "Cornell", TwoColumn = "Two-column",
        Outline = "Outline", Boxing = "Boxing", Charting = "Charting", Sentence = "Sentence",
        Mindmap = "Mindmap";
    public static readonly string[] Styles =
        { Freeform, Cornell, TwoColumn, Outline, Boxing, Charting, Sentence, Mindmap };

    public const int ModeGuides = 0;
    public const int ModeStartersOnly = 1;
    public const int ModeRigid = 2;

    public static string MapGlobalGrid(string pageGrid) => pageGrid switch
    {
        "Blank" => Blank,
        "Ruled" => Ruled,
        "Wide ruled" => RuledWide,
        "Grid" => Grid,
        "Dots" => Dots,
        "Lines" => Grid,
        _ => Blank,
    };

    public static string EffectiveGrid(Page page, Notebook nb, string globalPageGrid) =>
        page.GridStyle ?? nb.DefaultGridStyle ?? MapGlobalGrid(globalPageGrid);

    public static (string Style, int Mode) EffectiveStyle(Page page, Notebook nb) =>
        page.PageStyle is { } s ? (s, page.PageStyleMode) : (nb.DefaultPageStyle, nb.DefaultPageStyleMode);
}
