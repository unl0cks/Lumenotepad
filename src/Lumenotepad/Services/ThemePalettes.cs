using System;
using System.Globalization;

namespace Lumenotepad.Services;

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
    string AccentGradTop, string AccentGradBottom, string AccentText,
    string WindowBackground,
    string MenuBackground, string MenuBorder,
    bool DarkChrome,
    bool GlassWindow)
{

    public bool FrostedWindow => GlassWindow && FrameAlpha < 0x40;

    private int FrameAlpha
    {
        get
        {
            var h = FrameBackground.TrimStart('#');
            return h.Length == 8 ? Convert.ToInt32(h[..2], 16) : 0xFF;
        }
    }
}

public static class ThemePalettes
{
    public static readonly string[] Themes = { "Lumen", "Dark", "Light", "Pink", "Light blue" };

    public static ThemeTokens Resolve(string theme, bool fullTheme, bool paperLight)
    {
        return theme switch
        {

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

    private static ThemeTokens Lumen(bool fullTheme, bool paperLight)
    {
        var t = GlassRegionBase(accent: "#4DA6FF") with
        {
            FrameBackground = "#02FFFFFF",
            FrameBorder = "#33FFFFFF",
            CanvasBackground = "#00000000",

            MenuBackground = "#F5171922", MenuBorder = "#2EFFFFFF",
            DarkChrome = true,
            GlassWindow = true,
        };
        if (fullTheme)

            return t with
            {
                PaperBackground = "#0BFFFFFF", PaperBorder = "#33FFFFFF",

                MenuBackground = "#4014161C", MenuBorder = "#33FFFFFF",
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

            AccentDeep = Alpha(t.AccentDeep, 0x80),

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

        return t with
        {
            GlassWindow = true,
            PaperBackground = "#14FFFFFF", PaperBorder = dark ? "#FF3A3D46" : "#FFAEB5BF",
            PaperText = "#FFFFFFFF", PaperTextMuted = "#80FFFFFF",
            NoteChromeHover = "#26FFFFFF", NoteGripFill = "#12FFFFFF", NoteGripBar = "#3DFFFFFF",
            ScrollThumb = "#2EFFFFFF", ScrollThumbHover = "#52FFFFFF", ScrollThumbPressed = "#70FFFFFF",
        };
    }

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
        AccentText: IdealTextOn(accent),
        WindowBackground: "#FF14161C",

        MenuBackground: "#F514161C", MenuBorder: "#33FFFFFF",
        DarkChrome: true, GlassWindow: true);

    private static ThemeTokens LightFrameBase(string accent) => GlassRegionBase(accent) with
    {
        TextPrimary = "#FF23262F", TextSecondary = "#CC23262F", TextMuted = "#8023262F",
        ControlHover = "#12000000", ControlPressed = "#22000000",
    };

    public static ThemeTokens WithAccent(ThemeTokens t, string seed) => t with
    {
        Accent = seed,
        AccentHover = Shade(seed, 0.15),
        AccentSoft = Alpha(seed, 0x38),
        AccentDeep = Shade(seed, -0.28),
        AccentGradTop = Shade(seed, 0.12),
        AccentGradBottom = Shade(seed, -0.10),
        AccentText = IdealTextOn(seed),
        FieldSelection = Alpha(seed, 0x78),
        NoteChromeFocus = Alpha(seed, 0x4D),
    };

    public static string IdealTextOn(string hex)
    {
        var (_, r, g, b) = Parse(hex);
        return (r * 299 + g * 587 + b * 114) / 1000 >= 152 ? "#FF23262F" : "#FFFFFFFF";
    }

    public static string? NormalizeHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().TrimStart('#');
        if (t.Length != 6) return null;
        foreach (char ch in t)
            if (!Uri.IsHexDigit(ch)) return null;
        return "#" + t.ToUpperInvariant();
    }

    public static string Shade(string hex, double f)
    {
        var (a, r, g, b) = Parse(hex);
        byte Mix(byte ch) => (byte)Math.Clamp(f >= 0 ? ch + (255 - ch) * f : ch * (1 + f), 0, 255);
        return Format(a, Mix(r), Mix(g), Mix(b));
    }

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
