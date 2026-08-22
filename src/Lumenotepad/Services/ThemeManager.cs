using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Media.Immutable;

namespace Lumenotepad.Services;

public static class ThemeManager
{
    public static ThemeTokens Current { get; private set; } = ThemePalettes.Resolve("Lumen", false, false);

    public static double Roundness { get; private set; } = 1.0;

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
        Brush("GlassBorderBrush", t.FrameBorder);
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
        Brush("AccentTextBrush", t.AccentText);
        Brush("CardBackgroundBrush", t.DarkChrome ? "#1F000000" : "#66FFFFFF");
        Brush("WindowBackgroundBrush", t.WindowBackground);

        string childSurface = t.FrostedWindow ? "#D8" + t.WindowBackground[^6..] : t.WindowBackground;
        Brush("ChildSurfaceBrush", childSurface);
        Brush("WindowSurfaceBrush", childSurface);
        Brush("MenuBackgroundBrush", MacMenuFill(t.MenuBackground));
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

        r["AccentGlowShadow"] = BoxShadows.Parse($"0 0 5 0 {ThemePalettes.Alpha(t.Accent, 0x4D)}");

        var accent = Color.Parse(t.Accent);
        r["AccentColor"] = accent;
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorLight1"] = Color.Parse(ThemePalettes.Shade(t.Accent, 0.15));
        r["SystemAccentColorLight2"] = Color.Parse(ThemePalettes.Shade(t.Accent, 0.30));
        r["SystemAccentColorDark1"] = Color.Parse(ThemePalettes.Shade(t.Accent, -0.15));
        r["SystemAccentColorDark2"] = Color.Parse(ThemePalettes.Shade(t.Accent, -0.30));
        var variant = t.DarkChrome ? ThemeVariant.Dark : ThemeVariant.Light;

        if (!Equals(app.RequestedThemeVariant, variant)) app.RequestedThemeVariant = variant;

        if (!System.OperatingSystem.IsWindows()) RefreshMacChildGlass();
    }

    public static void ApplyChrome(Window window) =>
        Platform.DwmAcrylic.Apply(window,
            Current.GlassWindow ? Platform.DwmAcrylic.Backdrop.Acrylic : Platform.DwmAcrylic.Backdrop.None,
            dark: Current.DarkChrome);

    public static void RefreshBackdrop(Window window)
    {
        if (!Current.GlassWindow) { ApplyChrome(window); return; }
        Platform.DwmAcrylic.Apply(window, Platform.DwmAcrylic.Backdrop.None, dark: Current.DarkChrome);
        ApplyChrome(window);
    }

    public static void ApplyChildChrome(Window window)
    {
        if (!System.OperatingSystem.IsWindows())
        {

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

    public static void UseMacNativeChrome(Window window, Panel? titleBar = null, double titleBarHeight = 38)
    {
        if (System.OperatingSystem.IsWindows()) return;
        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = titleBarHeight;

        if (titleBar is not null) titleBar.Margin = new Thickness(72, 0, 0, 0);

        if (window.FindControl<Button>("CloseBtn") is { } close) close.IsVisible = false;

        _macChildWindows.Add((new System.WeakReference<Window>(window), 0));
        ApplyMacGlass(window, ChildGlass);
        window.Opened += (_, _) =>
        {
            ApplyMacGlass(window, ChildGlass);
            Platform.MacVibrancy.KeepFrostActive(window, 0);
        };
    }

    public static void ConfigureDialogChrome(Window window)
    {
        if (System.OperatingSystem.IsWindows()) { window.WindowDecorations = WindowDecorations.None; return; }
        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = 0;

        if (Current.FrostedWindow) window.Background = new SolidColorBrush(Color.Parse(SurfaceFill));
        _macChildWindows.Add((new System.WeakReference<Window>(window), SurfaceMaterial));
        ApplyMacGlass(window, ChildGlass);
        window.Opened += (_, _) =>
        {
            ApplyMacGlass(window, ChildGlass);
            Platform.MacVibrancy.HideTrafficLights(window);
            Platform.MacVibrancy.KeepFrostActive(window, SurfaceMaterial);
        };
    }

    private static nint SurfaceMaterial => Platform.MacVibrancy.Material;

    private static string SurfaceFill => "#B8" + Current.WindowBackground[^6..];

    private static string MacMenuFill(string menuBackground)
    {
        if (System.OperatingSystem.IsWindows() || menuBackground.Length != 9) return menuBackground;
        return System.Convert.ToInt32(menuBackground.Substring(1, 2), 16) >= 0xF0
            ? menuBackground
            : "#73" + menuBackground[^6..];
    }

    private static readonly List<(System.WeakReference<Window> Window, nint Material)> _macChildWindows = new();

    internal static void RefreshMacChildGlass()
    {
        for (int i = _macChildWindows.Count - 1; i >= 0; i--)
        {
            if (_macChildWindows[i].Window.TryGetTarget(out var w) && w.IsVisible)
            {
                if (Current.FrostedWindow && w.WindowDecorations != WindowDecorations.None &&
                    w.ExtendClientAreaTitleBarHeightHint == 0)
                    w.Background = new SolidColorBrush(Color.Parse(SurfaceFill));
                ApplyMacGlass(w, ChildGlass);
                Platform.MacVibrancy.KeepFrostActive(w, _macChildWindows[i].Material);
            }
            else if (!_macChildWindows[i].Window.TryGetTarget(out _))
            {
                _macChildWindows.RemoveAt(i);
            }
        }
    }

    private static readonly WindowTransparencyLevel[] MacOff = { WindowTransparencyLevel.None };
    private static readonly WindowTransparencyLevel[] MacBlur = { WindowTransparencyLevel.AcrylicBlur };

    private static readonly WindowTransparencyLevel[] MacClear = { WindowTransparencyLevel.Transparent };

    internal static void RoundMacChildWindow(Window window)
    {
        const string Tag = "mac-rounded";
        if (window.Content is not Control inner || (inner as Border)?.Tag as string == Tag) return;

        window.Content = null;
        var shell = new Border
        {
            Tag = Tag,
            CornerRadius = new CornerRadius(11),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            Child = inner,
        };

        object? surface = null;
        Application.Current?.TryFindResource("WindowSurfaceBrush", out surface);
        if (Application.Current is { } app && ReferenceEquals(window.Background, surface))
            shell.Bind(Border.BackgroundProperty, app.GetResourceObservable("ChildSurfaceBrush"));
        if (Application.Current is { } bapp)
            shell.Bind(Border.BorderBrushProperty, bapp.GetResourceObservable("FrameBorderBrush"));
        else
            shell.Background = window.Background;
        window.Content = shell;
        window.Background = Brushes.Transparent;
    }

    public enum MacGlass { Frost, Clear, Opaque }

    private static MacGlass ChildGlass => Current.FrostedWindow ? MacGlass.Frost : MacGlass.Opaque;

    public static void ApplyMacGlass(Window window, MacGlass mode)
    {

        var target = Current.DarkChrome ? ThemeVariant.Dark : ThemeVariant.Light;
        window.RequestedThemeVariant = target == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        window.RequestedThemeVariant = target;

        window.RequestedThemeVariant = ThemeVariant.Default;

        window.TransparencyLevelHint = MacOff;
        if (mode != MacGlass.Opaque)
            window.TransparencyLevelHint = mode == MacGlass.Frost ? MacBlur : MacClear;
        window.TransparencyBackgroundFallback = new SolidColorBrush(Color.Parse(Current.WindowBackground));
    }
}
