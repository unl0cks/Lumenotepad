using System;
using System.Globalization;

namespace Lumenotepad.Services;

/// <summary>Every resolved color a theme produces, as hex strings (pure model — no Avalonia).
/// Regions: FRAME (title bar, rails, nav), CANVAS (body backdrop incl. homepage), PAPER (the page
/// box + note containers). Glass regions render over the dark acrylic backdrop, so they always use
/// light text regardless of the theme's own text color.</summary>
public sealed record ThemeTokens(
    string FrameBackground, string FrameBorder,
    string TextPrimary, string TextSecondary, string TextMuted,
    string ControlHover, string ControlPressed,
    string ScrollThumb, string ScrollThumbHover, string ScrollThumbPressed,
    string CanvasBackground, string CanvasText, string CanvasTextMuted,
    string PaperBackground, string PaperBorder, string PaperText, string PaperTextMuted,
    string FieldSelection,
    string NoteChromeHover, string NoteChromeFocus, string NoteGripFill, string NoteGripBar,
    string Accent, string AccentHover, string AccentSoft, string AccentDeep,
    string AccentGradTop, string AccentGradBottom,
    string WindowBackground,   // opaque frame-family fill for secondary windows (preferences)
    bool DarkChrome,           // DWM immersive dark + Fluent Dark variant
    bool GlassWindow);         // at least one region shows the acrylic backdrop

/// <summary>The owner's theme matrix: <c>Theme</c> picks the FRAME material/palette; <c>Full theme</c>
/// OFF (default) gives the paper the CONTRASTING material (glass paper under solid frames, solid
/// paper under the Lumen glass frame — dark by default, light via <c>paperLight</c>); ON makes the
/// paper match the frame.</summary>
public static class ThemePalettes
{
    public static readonly string[] Themes = { "Lumen", "Dark", "Light", "Pink", "Light blue" };

    public static ThemeTokens Resolve(string theme, bool fullTheme, bool paperLight)
    {
        return theme switch
        {
            "Dark" => Solid(
                accent: "#4DA6FF", dark: true,
                frameBg: "#F214161C", frameBorder: "#26FFFFFF",
                solidCanvas: "#101218", solidPaper: "#1A1C24", solidPaperBorder: "#30FFFFFF",
                fullTheme),
            "Light" => Solid(
                accent: "#3E8EE0", dark: false,
                frameBg: "#F2F2F5F9", frameBorder: "#1F000000",
                solidCanvas: "#E9EDF3", solidPaper: "#FFFFFF", solidPaperBorder: "#24000000",
                fullTheme),
            "Pink" => Solid(
                accent: "#FB6F92", dark: false,
                frameBg: "#F2FFE5EC", frameBorder: "#26B0526E",
                solidCanvas: "#FFD3DF", solidPaper: "#FFF5F8", solidPaperBorder: "#33C97D97",
                fullTheme),
            "Light blue" => Solid(
                accent: "#5C85E6", dark: false,
                frameBg: "#F2EDF2FB", frameBorder: "#265F7BAE",
                solidCanvas: "#D7E3FC", solidPaper: "#F8FAFF", solidPaperBorder: "#336E86B8",
                fullTheme),
            _ => Lumen(fullTheme, paperLight),
        };
    }

    /// <summary>Lumen: glass frame + glass canvas. Paper is glass when Full theme is on, otherwise
    /// solid (dark by default, light when the paper toggle says so).</summary>
    private static ThemeTokens Lumen(bool fullTheme, bool paperLight)
    {
        var t = GlassRegionBase(accent: "#4DA6FF") with
        {
            FrameBackground = "#02FFFFFF",
            FrameBorder = "#33FFFFFF",
            CanvasBackground = "#00000000",
            DarkChrome = true,
            GlassWindow = true,
        };
        if (fullTheme)
            return t with { PaperBackground = "#0BFFFFFF", PaperBorder = "#33FFFFFF" };
        return paperLight
            ? t with
            {
                PaperBackground = "#F7F9FD", PaperBorder = "#2E000000",
                PaperText = "#23262F", PaperTextMuted = "#8023262F",
                NoteChromeHover = "#24000000", NoteGripFill = "#0E000000", NoteGripBar = "#38000000",
                ScrollThumb = "#33000000", ScrollThumbHover = "#4D000000", ScrollThumbPressed = "#66000000",
            }
            : t with { PaperBackground = "#191B22", PaperBorder = "#30FFFFFF" };
    }

    /// <summary>Solid-frame themes: frame AND the body around the page are always opaque — only the
    /// PAGE BOX itself changes material (owner corrections 2026-07-08). Full theme ON = solid family
    /// paper, no acrylic at all. OFF (default) = the page box is REAL glass: the canvas plate leaves
    /// a hole under it (see MainView) so the acrylic backdrop blurs through just the rounded box —
    /// glass regions read with light text.</summary>
    private static ThemeTokens Solid(
        string accent, bool dark, string frameBg, string frameBorder,
        string solidCanvas, string solidPaper, string solidPaperBorder, bool fullTheme)
    {
        var t = dark ? GlassRegionBase(accent) : LightFrameBase(accent);
        t = t with
        {
            FrameBackground = frameBg, FrameBorder = frameBorder,
            WindowBackground = Alpha(frameBg, 0xFF), DarkChrome = dark,
            CanvasBackground = solidCanvas,
            CanvasText = t.TextPrimary, CanvasTextMuted = t.TextMuted,
        };

        if (fullTheme)
        {
            return t with
            {
                GlassWindow = false,
                PaperBackground = solidPaper, PaperBorder = solidPaperBorder,
                PaperText = t.TextPrimary, PaperTextMuted = t.TextMuted,
                NoteChromeHover = dark ? "#26FFFFFF" : "#1F000000",
                NoteGripFill = dark ? "#12FFFFFF" : "#0E000000",
                NoteGripBar = dark ? "#3DFFFFFF" : "#38000000",
                ScrollThumb = dark ? "#2EFFFFFF" : "#33000000",
                ScrollThumbHover = dark ? "#52FFFFFF" : "#4D000000",
                ScrollThumbPressed = dark ? "#70FFFFFF" : "#66000000",
            };
        }

        // Real glass page over the dark acrylic → the paper region reads light on every theme.
        return t with
        {
            GlassWindow = true,
            PaperBackground = "#14FFFFFF", PaperBorder = "#33FFFFFF",
            PaperText = "#FFFFFFFF", PaperTextMuted = "#80FFFFFF",
            NoteChromeHover = "#26FFFFFF", NoteGripFill = "#12FFFFFF", NoteGripBar = "#3DFFFFFF",
            ScrollThumb = "#2EFFFFFF", ScrollThumbHover = "#52FFFFFF", ScrollThumbPressed = "#70FFFFFF",
        };
    }

    /// <summary>Baseline for dark/glass frames: light text and white-alpha controls everywhere.</summary>
    private static ThemeTokens GlassRegionBase(string accent) => new(
        FrameBackground: "#02FFFFFF", FrameBorder: "#33FFFFFF",
        TextPrimary: "#FFFFFFFF", TextSecondary: "#CCFFFFFF", TextMuted: "#80FFFFFF",
        ControlHover: "#22FFFFFF", ControlPressed: "#38FFFFFF",
        ScrollThumb: "#2EFFFFFF", ScrollThumbHover: "#52FFFFFF", ScrollThumbPressed: "#70FFFFFF",
        CanvasBackground: "#00000000", CanvasText: "#FFFFFFFF", CanvasTextMuted: "#80FFFFFF",
        PaperBackground: "#0BFFFFFF", PaperBorder: "#33FFFFFF",
        PaperText: "#FFFFFFFF", PaperTextMuted: "#80FFFFFF",
        FieldSelection: Alpha(accent, 0x55),
        NoteChromeHover: "#26FFFFFF", NoteChromeFocus: Alpha(accent, 0x4D),
        NoteGripFill: "#12FFFFFF", NoteGripBar: "#3DFFFFFF",
        Accent: accent, AccentHover: Shade(accent, 0.15), AccentSoft: Alpha(accent, 0x38),
        AccentDeep: Shade(accent, -0.28), AccentGradTop: Shade(accent, 0.12), AccentGradBottom: Shade(accent, -0.10),
        WindowBackground: "#FF14161C", DarkChrome: true, GlassWindow: true);

    /// <summary>Baseline for light solid frames: dark text and black-alpha controls on the frame.</summary>
    private static ThemeTokens LightFrameBase(string accent) => GlassRegionBase(accent) with
    {
        TextPrimary = "#FF23262F", TextSecondary = "#CC23262F", TextMuted = "#8023262F",
        ControlHover = "#12000000", ControlPressed = "#22000000",
    };

    // ---- tiny color math on hex strings (pure, testable) ----

    /// <summary>Blend toward white (f &gt; 0) or black (f &lt; 0); f in -1..1.</summary>
    public static string Shade(string hex, double f)
    {
        var (a, r, g, b) = Parse(hex);
        byte Mix(byte ch) => (byte)Math.Clamp(f >= 0 ? ch + (255 - ch) * f : ch * (1 + f), 0, 255);
        return Format(a, Mix(r), Mix(g), Mix(b));
    }

    /// <summary>The color with its alpha replaced.</summary>
    public static string Alpha(string hex, byte alpha)
    {
        var (_, r, g, b) = Parse(hex);
        return Format(alpha, r, g, b);
    }

    private static (byte A, byte R, byte G, byte B) Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        uint v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static string Format(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";
}
