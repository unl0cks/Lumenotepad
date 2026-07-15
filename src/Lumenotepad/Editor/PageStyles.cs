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

    // Page styles (methods). Mindmap (M9 Part 5) is interactive: bubbles link by dragging
    // one onto another (the Mapping Method folded in).
    public const string Freeform = "Freeform", Cornell = "Cornell", TwoColumn = "Two-column",
        Outline = "Outline", Boxing = "Boxing", Charting = "Charting", Sentence = "Sentence",
        Mindmap = "Mindmap";
    public static readonly string[] Styles =
        { Freeform, Cornell, TwoColumn, Outline, Boxing, Charting, Sentence, Mindmap };

    // Apply modes for the guide-based styles.
    public const int ModeGuides = 0;       // guides + starter containers
    public const int ModeStartersOnly = 1; // starter containers, no guides
    public const int ModeRigid = 2;        // guides + LOCKED starter containers

    /// <summary>The app-wide grid pref → a grid-style key. Accepts both the new-style picker
    /// keys (Blank/Ruled/Grid/Dots) and the legacy Part-3 stored values ("None"/"Dots"/"Lines")
    /// for backward compatibility with settings saved before the picker grew Ruled/Grid.</summary>
    public static string MapGlobalGrid(string pageGrid) => pageGrid switch
    {
        "Blank" => Blank,
        "Ruled" => Ruled,
        "Grid" => Grid,
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
