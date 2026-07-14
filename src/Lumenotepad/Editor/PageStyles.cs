using Lumenotepad.Models;

namespace Lumenotepad.Editor;

/// <summary>The two per-page style axes (spec: M9 Notebook Studio). GRID styles are the paper
/// background pattern; PAGE styles are the note-taking-method structure drawn over it. Pure —
/// key catalogs + effective-style resolution, no Avalonia.</summary>
public static class PageStyles
{
    // Grid styles (paper background). "Ruled" is new; the rest map from the Part-3 global pref.
    public const string Blank = "Blank", Ruled = "Ruled", Grid = "Grid", Dots = "Dots";
    public static readonly string[] GridStyles = { Blank, Ruled, Grid, Dots };

    // Page styles (methods). Mindmap is reserved for M9 Part 5 — renders like Freeform until then.
    public const string Freeform = "Freeform", Cornell = "Cornell", TwoColumn = "Two-column",
        Outline = "Outline", Boxing = "Boxing", Charting = "Charting", Sentence = "Sentence",
        Mindmap = "Mindmap";
    public static readonly string[] Styles =
        { Freeform, Cornell, TwoColumn, Outline, Boxing, Charting, Sentence };

    // Apply modes for the guide-based styles.
    public const int ModeGuides = 0;       // guides + starter containers
    public const int ModeStartersOnly = 1; // starter containers, no guides
    public const int ModeRigid = 2;        // guides + LOCKED starter containers

    /// <summary>The app-wide Part-3 grid pref ("None"|"Dots"|"Lines") → a grid-style key.</summary>
    public static string MapGlobalGrid(string pageGrid) => pageGrid switch
    {
        "Dots" => Dots,
        "Lines" => Grid,
        _ => Blank,
    };

    /// <summary>Effective grid style: page ?? notebook ?? global pref.</summary>
    public static string EffectiveGrid(Page page, Notebook nb, string globalPageGrid) =>
        page.GridStyle ?? nb.DefaultGridStyle ?? MapGlobalGrid(globalPageGrid);

    /// <summary>Effective page style + apply mode: an explicit page style carries its own mode;
    /// inheriting takes both from the notebook.</summary>
    public static (string Style, int Mode) EffectiveStyle(Page page, Notebook nb) =>
        page.PageStyle is { } s ? (s, page.PageStyleMode) : (nb.DefaultPageStyle, nb.DefaultPageStyleMode);
}
