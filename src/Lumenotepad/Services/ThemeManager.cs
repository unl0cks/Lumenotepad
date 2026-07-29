using System.Collections.Generic;
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
        // whole-window glass (Lumen) so the acrylic behind child windows shows through as frost.
        // ChildSurfaceBrush is the same fill under a stable name for the dialog rounding wrapper.
        string childSurface = t.FrostedWindow ? "#D8" + t.WindowBackground[^6..] : t.WindowBackground;
        Brush("ChildSurfaceBrush", childSurface);
        Brush("WindowSurfaceBrush", childSurface);
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
        // macOS child windows are real NSWindows with their own backdrop, so they have to be re-armed
        // when the theme changes underneath them (Lumen = frost, anything else = opaque).
        if (!System.OperatingSystem.IsWindows()) RefreshMacChildGlass();
    }

    /// <summary>Re-tint a window's native chrome for the active theme: immersive dark, and the
    /// acrylic backdrop ONLY when the theme actually shows glass — solid themes are fully painted,
    /// and skipping the backdrop there avoids DWM's maximize/snap artifacts.</summary>
    public static void ApplyChrome(Window window) =>
        Platform.DwmAcrylic.Apply(window,
            Current.GlassWindow ? Platform.DwmAcrylic.Backdrop.Acrylic : Platform.DwmAcrylic.Backdrop.None,
            dark: Current.DarkChrome);

    /// <summary>Force the window's DWM backdrop to re-composite by toggling it OFF then back ON — clears
    /// the stuck "bright wash" a snap/maximize can leave on the acrylic surface (a plain re-set is often a
    /// no-op DWM ignores; the off→on toggle makes it tear the surface down and rebuild). No-op unless the
    /// theme uses glass — solid themes never show the wash.</summary>
    public static void RefreshBackdrop(Window window)
    {
        if (!Current.GlassWindow) { ApplyChrome(window); return; }
        Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.None, dark: Current.DarkChrome);
        ApplyChrome(window);
    }

    /// <summary>Chrome for secondary windows (preferences, notebook wizard, font browser): acrylic
    /// frost ONLY when the theme is whole-window glass (Lumen), else a plain opaque window matching
    /// the solid frame. Paired with a Background bound to WindowSurfaceBrush.</summary>
    public static void ApplyChildChrome(Window window)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            // No DWM on mac; frost via NSVisualEffectView. Child windows call this from their ctors —
            // before the native handle exists — so re-assert once the window is actually open.
            // A window that already took the native mac frame follows the theme (frost only for
            // Lumen); the rest are rounded from content, which requires plain clear glass.
            bool native = window.WindowDecorations != WindowDecorations.None;
            var mode = native ? ChildGlass : MacGlass.Opaque;
            ApplyMacGlass(window, mode);
            window.Opened += (_, _) => ApplyMacGlass(window, native ? ChildGlass : MacGlass.Opaque);
            return;
        }
        Platform.DwmAcrylic.Apply(window,
            Current.FrostedWindow ? Platform.DwmAcrylic.Backdrop.Acrylic : Platform.DwmAcrylic.Backdrop.None,
            dark: Current.DarkChrome);
    }

    /// <summary>macOS frost. Three things must all line up or the window reads as a flat wash:
    /// (1) the LEVEL — Avalonia's macOS backend maps only None→Opaque, Transparent→Transparent and
    ///     AcrylicBlur→Blur; plain <c>Blur</c> is silently UNRECOGNISED. Worse, re-running the
    ///     backend's SetTransparencyLevelHint is DESTRUCTIVE (verified by disassembling
    ///     Avalonia.Native): it skips any level equal to the one already active, and if the whole list
    ///     ends up applying nothing it falls out of the loop and forces the window back to Opaque. So
    ///     the list is a single shared AcrylicBlur entry, assigned only when it isn't already set —
    ///     a second entry would simply override the frost on the next call.
    /// (2) the APPEARANCE — NSVisualEffectView takes its material from the window's NSAppearance,
    ///     which follows the theme variant. A Light appearance frosts WHITE: exactly the grey-white
    ///     wash the tester reported. Pin the variant to the theme's own chrome darkness.
    /// (3) the FALLBACK — if the OS still declines, paint the theme's opaque frame base rather than
    ///     Avalonia's default white, so the failure mode is clean dark instead of washed grey.
    /// Must be re-applied once the native window exists (see MainWindow.OnOpened): a hint set before
    /// the handle is created can be dropped.</summary>
    /// <summary>macOS: give a SECONDARY window the native frame. A borderless NSWindow is square and
    /// cannot host the frost (the blur layer is a square pane that juts out of any corner we round
    /// ourselves), so the only way to get Windows-like frosted-AND-rounded child windows is to let
    /// macOS draw the frame: native rounding, shadow, and a real NSVisualEffectView. The cost is the
    /// traffic-light buttons, which the caller's title bar is inset to clear. Windows path untouched.
    /// Because decorations are no longer None, the content-rounding wrapper is skipped automatically.</summary>
    public static void UseMacNativeChrome(Window window, Panel? titleBar = null, double titleBarHeight = 38)
    {
        if (System.OperatingSystem.IsWindows()) return;
        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = titleBarHeight;
        // Shifting the whole title-bar grid right keeps its right-aligned buttons in place and only
        // moves the title clear of the traffic lights.
        if (titleBar is not null) titleBar.Margin = new Thickness(72, 0, 0, 0);
        // The traffic lights now close the window, so our own caption button is a duplicate.
        if (window.FindControl<Button>("CloseBtn") is { } close) close.IsVisible = false;
        _macChildWindows.Add(new System.WeakReference<Window>(window));
        ApplyMacGlass(window, ChildGlass);
        window.Opened += (_, _) =>
        {
            ApplyMacGlass(window, ChildGlass);
            Platform.MacVibrancy.KeepFrostActive(window);
        };
    }

    /// <summary>Chrome for the small code-built dialogs (confirm / input / reorder / crop). On Windows
    /// they stay chromeless and DWM rounds them. On macOS a borderless NSWindow is square AND cannot
    /// host the frost, so they take the native frame for its rounding, shadow and real
    /// NSVisualEffectView — then the traffic lights are hidden, because a confirm box must not sprout
    /// window buttons. Glass follows the theme: frosted under Lumen, opaque under the others.</summary>
    public static void ConfigureDialogChrome(Window window)
    {
        if (System.OperatingSystem.IsWindows()) { window.WindowDecorations = WindowDecorations.None; return; }
        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = 0;   // no titlebar band: the dialog owns the whole sheet
        _macChildWindows.Add(new System.WeakReference<Window>(window));
        ApplyMacGlass(window, ChildGlass);
        window.Opened += (_, _) =>
        {
            ApplyMacGlass(window, ChildGlass);
            Platform.MacVibrancy.HideTrafficLights(window);
            Platform.MacVibrancy.KeepFrostActive(window);
        };
    }

    /// <summary>Secondary windows wearing the native mac frame, so a theme switch can re-run their
    /// glass (Lumen frosts; every other theme is an opaque sheet). Weak, and pruned as it is walked.</summary>
    private static readonly List<System.WeakReference<Window>> _macChildWindows = new();

    private static void RefreshMacChildGlass()
    {
        for (int i = _macChildWindows.Count - 1; i >= 0; i--)
        {
            if (_macChildWindows[i].TryGetTarget(out var w) && w.IsVisible)
            {
                ApplyMacGlass(w, ChildGlass);
                Platform.MacVibrancy.KeepFrostActive(w);
            }
            else if (!_macChildWindows[i].TryGetTarget(out _))
            {
                _macChildWindows.RemoveAt(i);
            }
        }
    }

    /// <summary>The macOS level lists — see <see cref="ApplyMacGlass"/> for why the frost is armed by
    /// stepping through <see cref="MacOff"/> first.</summary>
    private static readonly WindowTransparencyLevel[] MacOff = { WindowTransparencyLevel.None };
    private static readonly WindowTransparencyLevel[] MacBlur = { WindowTransparencyLevel.AcrylicBlur };
    /// <summary>Child windows use plain transparency, never blur: the NSVisualEffectView is a SQUARE
    /// layer filling the whole window, so on a borderless window it shows as a hard-edged slab behind
    /// our rounded content ("a sharp layer underneath" in testing). Clear glass has no such layer.</summary>
    private static readonly WindowTransparencyLevel[] MacClear = { WindowTransparencyLevel.Transparent };

    /// <summary>Round a borderless child window's corners on macOS. Windows rounds these through DWM,
    /// but a borderless NSWindow is square, so the shape has to come from the CONTENT: move the window's
    /// own surface fill onto a clipped, rounded Border wrapped around the existing content and let the
    /// window itself go transparent. Keeps the surface bound to the theme resource so it still restyles,
    /// and is idempotent (the wrapper tags itself) since chrome is re-applied on theme changes.</summary>
    internal static void RoundMacChildWindow(Window window)
    {
        const string Tag = "mac-rounded";
        if (window.Content is not Control inner || (inner as Border)?.Tag as string == Tag) return;
        // DETACH before re-parenting: handing `inner` to the Border while it is still the window's
        // content leaves it owning two parents, and Avalonia throws on the attach (this crashed
        // Preferences on macOS in 1.0.3).
        window.Content = null;
        var shell = new Border
        {
            Tag = Tag,
            CornerRadius = new CornerRadius(11),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),   // borderless mac windows have no edge of their own
            Child = inner,
        };
        // Windows whose fill IS the themed surface keep following it (Preferences is open while the
        // user switches themes); the few dialogs that set their own literal brush keep that brush.
        object? surface = null;
        Application.Current?.TryFindResource("WindowSurfaceBrush", out surface);
        if (Application.Current is { } app && ReferenceEquals(window.Background, surface))
            shell.Bind(Border.BackgroundProperty, app.GetResourceObservable("ChildSurfaceBrush"));
        if (Application.Current is { } bapp)
            shell.Bind(Border.BorderBrushProperty, bapp.GetResourceObservable("FrameBorderBrush"));
        else
            shell.Background = window.Background;
        window.Content = shell;
        window.Background = Brushes.Transparent;   // the rounded Border is what paints now
    }

    /// <summary>How a macOS window treats its backdrop. <see cref="Frost"/> = real NSVisualEffectView
    /// blur, <see cref="Clear"/> = plain see-through (used only where we round from content and a
    /// square blur pane would show), <see cref="Opaque"/> = no transparency at all.</summary>
    public enum MacGlass { Frost, Clear, Opaque }

    /// <summary>What a SECONDARY window should be: glass only when the theme actually is glass
    /// (Lumen), otherwise a plain opaque sheet — mirrors ApplyChildChrome's rule on Windows.</summary>
    private static MacGlass ChildGlass => Current.FrostedWindow ? MacGlass.Frost : MacGlass.Opaque;

    public static void ApplyMacGlass(Window window, MacGlass mode)
    {
        // APPEARANCE FIRST, and as a real TRANSITION. The native layer only pushes the frame appearance
        // to the NSWindow when the variant actually CHANGES, so a window that is dark from birth is
        // never told — the NSVisualEffectView keeps the default VibrantLight material and renders as
        // pale grey (diagnostics from the tester's Mac: frost granted, variant Dark, yet grey). Passing
        // through the opposite variant guarantees the change fires. It is exactly why switching theme
        // away and back "fixed" the window by hand.
        var target = Current.DarkChrome ? ThemeVariant.Dark : ThemeVariant.Light;
        window.RequestedThemeVariant = target == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        window.RequestedThemeVariant = target;
        // ...then hand the window BACK to the app-level variant. Pinning it here permanently left a
        // window that was open across a theme switch stuck on its old variant — a Preferences window
        // opened under Lumen (dark) and then switched to Pink kept dark-variant Fluent styling, i.e.
        // white text on a pale pink sheet (tester report; Windows was fine because nothing pins it).
        // Default re-inherits, and since the app is already on `target` no further change fires, so
        // the native appearance set above stands.
        window.RequestedThemeVariant = ThemeVariant.Default;
        // Then force a real OFF→ON transition. The backend ignores any level equal to the one already
        // active (so a plain re-assert is a no-op, and a list whose entries are all no-ops resets the
        // window to Opaque). Stepping through None guarantees the next assignment genuinely re-arms it.
        window.TransparencyLevelHint = MacOff;
        if (mode != MacGlass.Opaque)
            window.TransparencyLevelHint = mode == MacGlass.Frost ? MacBlur : MacClear;
        window.TransparencyBackgroundFallback = new SolidColorBrush(Color.Parse(Current.WindowBackground));
    }
}
