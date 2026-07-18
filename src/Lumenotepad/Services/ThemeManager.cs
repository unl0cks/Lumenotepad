using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Media.Immutable;

namespace Lumenotepad.Services;

/// <summary>Applies a resolved <see cref="ThemeTokens"/> to the live app: writes every themed brush
/// into Application.Resources (entries there override the Theme.axaml defaults, and DynamicResource
/// consumers restyle immediately), flips the Fluent variant, and re-tints the DWM chrome of a window.
/// <see cref="Current"/> holds the active tokens for code-built views to read at construction.</summary>
public static class ThemeManager
{
    public static ThemeTokens Current { get; private set; } = ThemePalettes.Resolve("Lumen", false, false);

    /// <summary>The active corner-roundness scale (M8 Part 6) for CODE-drawn corners (canvas plate
    /// hole, note-container chrome). XAML consumers restyle via the Radius* resources instead.</summary>
    public static double Roundness { get; private set; } = 1.0;

    /// <summary>Scale the app's corner radii: writes the Radius* CornerRadius resources (page box,
    /// paper veil) and records the scale for code-drawn corners. Menus are deliberately EXCLUDED —
    /// their popup windows carry fixed 8px DWM rounding, and a mismatched content radius brings the
    /// corner wedges back (PF3 lesson).</summary>
    public static void PushRoundness(Application app, double scale)
    {
        Roundness = scale;
        var r = app.Resources;
        r["RadiusPage"] = new CornerRadius(System.Math.Round(14 * scale));
        r["RadiusPageInner"] = new CornerRadius(System.Math.Round(13 * scale));
    }

    public static void Apply(Application app, ThemeTokens t)
    {
        Current = t;
        var r = app.Resources;

        void Brush(string key, string hex) => r[key] = new SolidColorBrush(Color.Parse(hex));

        Brush("FrameBackgroundBrush", t.FrameBackground);
        Brush("FrameBorderBrush", t.FrameBorder);
        Brush("GlassBorderBrush", t.FrameBorder);          // legacy key, kept in sync
        Brush("TextPrimaryBrush", t.TextPrimary);
        Brush("TextSecondaryBrush", t.TextSecondary);
        Brush("TextMutedBrush", t.TextMuted);
        Brush("ControlHoverBrush", t.ControlHover);
        Brush("ControlPressedBrush", t.ControlPressed);
        Brush("ScrollThumbBrush", t.ScrollThumb);
        Brush("ScrollThumbHoverBrush", t.ScrollThumbHover);
        Brush("ScrollThumbPressedBrush", t.ScrollThumbPressed);
        Brush("CanvasBackgroundBrush", t.CanvasBackground);
        Brush("CanvasTextBrush", t.CanvasText);
        Brush("CanvasTextMutedBrush", t.CanvasTextMuted);
        Brush("CanvasChipBrush", t.CanvasChip);
        Brush("CanvasChipBorderBrush", t.CanvasChipBorder);
        Brush("PaperBackgroundBrush", t.PaperBackground);
        Brush("PaperBorderBrush", t.PaperBorder);
        Brush("PaperTextBrush", t.PaperText);
        Brush("PaperTextMutedBrush", t.PaperTextMuted);
        Brush("FieldSelectionBrush", t.FieldSelection);
        Brush("NoteChromeHoverBrush", t.NoteChromeHover);
        Brush("NoteChromeFocusBrush", t.NoteChromeFocus);
        Brush("NoteGripFillBrush", t.NoteGripFill);
        Brush("NoteGripBarBrush", t.NoteGripBar);
        Brush("AccentBrush", t.Accent);
        Brush("AccentHoverBrush", t.AccentHover);
        Brush("AccentSoftBrush", t.AccentSoft);
        Brush("AccentDeepBrush", t.AccentDeep);
        Brush("WindowBackgroundBrush", t.WindowBackground);
        // Secondary-window surface: opaque normally, but a translucent tint when the theme is
        // whole-window glass (Lumen) so the DWM acrylic behind child windows shows through as frost.
        Brush("WindowSurfaceBrush", t.FrostedWindow ? "#D8" + t.WindowBackground[^6..] : t.WindowBackground);
        Brush("MenuBackgroundBrush", t.MenuBackground);
        Brush("MenuBorderBrush", t.MenuBorder);

        r["AccentGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(t.AccentGradTop), 0),
                new GradientStop(Color.Parse(t.AccentGradBottom), 1),
            },
        };

        // A very faint accent glow (BoxShadows can't take a DynamicResource color, so bake it here
        // and refresh on every theme change) for selected section/page rows.
        r["AccentGlowShadow"] = BoxShadows.Parse($"0 0 5 0 {ThemePalettes.Alpha(t.Accent, 0x4D)}");

        // Fluent's own controls (menus, flyouts, list selection) follow the accent + variant.
        var accent = Color.Parse(t.Accent);
        r["AccentColor"] = accent;
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorLight1"] = Color.Parse(ThemePalettes.Shade(t.Accent, 0.15));
        r["SystemAccentColorLight2"] = Color.Parse(ThemePalettes.Shade(t.Accent, 0.30));
        r["SystemAccentColorDark1"] = Color.Parse(ThemePalettes.Shade(t.Accent, -0.15));
        r["SystemAccentColorDark2"] = Color.Parse(ThemePalettes.Shade(t.Accent, -0.30));
        app.RequestedThemeVariant = t.DarkChrome ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>Re-tint a window's native chrome for the active theme: immersive dark, and the
    /// glass backdrop ONLY when the theme actually shows glass — solid themes are fully painted,
    /// and skipping the backdrop there avoids DWM's maximize/snap artifacts. The blur strength
    /// follows the "Main window" preference.</summary>
    public static void ApplyChrome(Window window) =>
        ApplyWindowBlur(window, Current.GlassWindow, BlurPrefs.MainPct);

    /// <summary>Chrome for secondary windows (preferences, notebook wizard, font browser): glass
    /// ONLY when the theme is whole-window glass (Lumen), else a plain opaque window matching the
    /// solid frame. Paired with a Background bound to WindowSurfaceBrush. The blur strength follows
    /// the "Other windows" preference.</summary>
    public static void ApplyChildChrome(Window window) =>
        ApplyWindowBlur(window, Current.FrostedWindow, BlurPrefs.WindowsPct);

    /// <summary>The user's blur tier on a window: Clear = transparent with NO blur, Soft = the
    /// plain gaussian blur-behind, Strong = the full DWM acrylic backdrop.</summary>
    private static void ApplyWindowBlur(Window window, bool glass, int pct)
    {
        bool dark = Current.DarkChrome;
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? System.IntPtr.Zero;
        if (!glass)
        {
            Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.None, dark);
            return;
        }
        switch (BlurPrefs.TierOf(pct))
        {
            case BlurPrefs.Tier.Clear:
                Platform.DwmAcrylic.DisableBlurBehind(hwnd);
                Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.None, dark);
                break;
            case BlurPrefs.Tier.Soft:
                Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.None, dark);
                Platform.DwmAcrylic.BlurBehind(hwnd, soft: true);
                break;
            default:
                Platform.DwmAcrylic.DisableBlurBehind(hwnd);
                Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.Acrylic, dark);
                break;
        }
    }
}
