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
    string CanvasChip, string CanvasChipBorder,
    string PaperBackground, string PaperBorder, string PaperText, string PaperTextMuted,
    string FieldSelection,
    string NoteChromeHover, string NoteChromeFocus, string NoteGripFill, string NoteGripBar,
    string Accent, string AccentHover, string AccentSoft, string AccentDeep,
    string AccentGradTop, string AccentGradBottom,
    string WindowBackground,   // opaque frame-family fill for secondary windows (preferences)
    string MenuBackground, string MenuBorder,   // right-click/flyout menu material, themed per-theme
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
            // Border alphas softened 2026-07-14 (owner: dark theme borders read too bright, the
            // three light-family themes read too dark — contrast was too big on every divider/frame).
            // Made OPAQUE 2026-07-15 (owner re-test: the translucent white/black borders composited
            // over the wallpaper-showing acrylic glass regions and picked up a reddish/orange tint
            // from the desktop wallpaper) — family-tinted opaque colors instead.
            "Dark" => Solid(
                accent: "#4DA6FF", dark: true,
                frameBg: "#F214161C", frameBorder: "#FF292C34",
                solidCanvas: "#101218", solidPaper: "#1A1C24", solidPaperBorder: "#FF2C2F38",
                fullTheme),
            "Light" => Solid(
                accent: "#3E8EE0", dark: false,
                frameBg: "#F2F2F5F9", frameBorder: "#FFC9CFD8",
                solidCanvas: "#E9EDF3", solidPaper: "#FFFFFF", solidPaperBorder: "#FFCDD3DC",
                fullTheme),
            "Pink" => Solid(
                accent: "#FB6F92", dark: false,
                frameBg: "#F2FFE5EC", frameBorder: "#FFE9B7C6",
                solidCanvas: "#FFD3DF", solidPaper: "#FFF5F8", solidPaperBorder: "#FFEBBECD",
                fullTheme),
            "Light blue" => Solid(
                accent: "#5C85E6", dark: false,
                frameBg: "#F2EDF2FB", frameBorder: "#FFC5D2E7",
                solidCanvas: "#D7E3FC", solidPaper: "#F8FAFF", solidPaperBorder: "#FFCBD7EA",
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
            // Full-theme-OFF context menu: dark opaque (owner's Lumen special case).
            MenuBackground = "#F5171922", MenuBorder = "#2EFFFFFF",
            DarkChrome = true,
            GlassWindow = true,
        };
        if (fullTheme)
            // Full-theme-ON: translucent dark — reads as smoked/frosted glass over content; paired
            // with best-effort DWM acrylic on the popup's own top-level (see MainView.OpenMenu).
            return t with
            {
                PaperBackground = "#0BFFFFFF", PaperBorder = "#33FFFFFF",
                // Light enough that the REAL blur-behind (DwmAcrylic.BlurBehind on the popup hwnd)
                // visibly shows; the blur's own dark gradient tint supplies the rest of the depth.
                MenuBackground = "#6614161C", MenuBorder = "#33FFFFFF",
            };
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
            CanvasChip = dark ? "#1AFFFFFF" : "#FFFFFFFF",
            CanvasChipBorder = dark ? "#FF383B44" : "#FFC9CFD8",
            // LumenButton's border (home-page "New notebook"/"Preferences") is an opaque deep-accent
            // shade, not one of the neutral frame/paper tokens above — re-pointing it at those would
            // put a mismatched gray/black edge on a colored gradient button. Soften it in place
            // (alpha only, hue untouched) instead; scoped to Solid() so Lumen's own AccentDeep is
            // never touched.
            AccentDeep = Alpha(t.AccentDeep, 0x80),
            // Context menu: the opaque frame family, bordered with this theme's (already softened)
            // frameBorder — reads as a themed, solid menu rather than Lumen's glass/frosted variants.
            MenuBackground = Alpha(frameBg, 0xFF), MenuBorder = frameBorder,
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
        // PaperBorder made opaque alongside the other border tokens (PaperBackground is a FILL, left
        // untouched — same #14FFFFFF value happens to appear elsewhere in this file as a background;
        // the glass region shows wallpaper, so a neutral opaque edge is the point here).
        return t with
        {
            GlassWindow = true,
            PaperBackground = "#14FFFFFF", PaperBorder = dark ? "#FF3A3D46" : "#FFAEB5BF",
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
        CanvasChip: "#14FFFFFF", CanvasChipBorder: "#33FFFFFF",
        PaperBackground: "#0BFFFFFF", PaperBorder: "#33FFFFFF",
        PaperText: "#FFFFFFFF", PaperTextMuted: "#80FFFFFF",
        FieldSelection: Alpha(accent, 0x78),
        NoteChromeHover: "#26FFFFFF", NoteChromeFocus: Alpha(accent, 0x4D),
        NoteGripFill: "#12FFFFFF", NoteGripBar: "#3DFFFFFF",
        Accent: accent, AccentHover: Shade(accent, 0.15), AccentSoft: Alpha(accent, 0x38),
        AccentDeep: Shade(accent, -0.28), AccentGradTop: Shade(accent, 0.12), AccentGradBottom: Shade(accent, -0.10),
        WindowBackground: "#FF14161C",
        // Sensible dark-opaque default menu material — every caller (Lumen, Solid dark/light) that
        // cares overrides both of these; this default just guarantees every path is covered.
        MenuBackground: "#F514161C", MenuBorder: "#33FFFFFF",
        DarkChrome: true, GlassWindow: true);

    /// <summary>Baseline for light solid frames: dark text and black-alpha controls on the frame.</summary>
    private static ThemeTokens LightFrameBase(string accent) => GlassRegionBase(accent) with
    {
        TextPrimary = "#FF23262F", TextSecondary = "#CC23262F", TextMuted = "#8023262F",
        ControlHover = "#12000000", ControlPressed = "#22000000",
    };

    /// <summary>Recompute every accent-derived token from a new seed color — the custom-accent
    /// preference. Pure: same Shade/Alpha math the palettes use, everything else untouched.</summary>
    public static ThemeTokens WithAccent(ThemeTokens t, string seed) => t with
    {
        Accent = seed,
        AccentHover = Shade(seed, 0.15),
        AccentSoft = Alpha(seed, 0x38),
        AccentDeep = Shade(seed, -0.28),
        AccentGradTop = Shade(seed, 0.12),
        AccentGradBottom = Shade(seed, -0.10),
        FieldSelection = Alpha(seed, 0x78),
        NoteChromeFocus = Alpha(seed, 0x4D),
    };

    /// <summary>Normalize user hex input ("4da6ff", " #4DA6FF ") to "#RRGGBB"; null when invalid.</summary>
    public static string? NormalizeHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().TrimStart('#');
        if (t.Length != 6) return null;
        foreach (char ch in t)
            if (!Uri.IsHexDigit(ch)) return null;
        return "#" + t.ToUpperInvariant();
    }

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
