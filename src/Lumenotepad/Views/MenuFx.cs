using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>Shared popup-opening effects for context menus AND button flyouts: the rise-in
/// animation, small DWM rounding on the popup's own window (an un-rounded popup surface pokes
/// square corners out behind the rounded content), and — when the active theme's menu background
/// is the translucent glass variant (alpha below 0xF0, i.e. Lumen with Full theme on) — a real
/// acrylic backdrop on that popup window.</summary>
public static class MenuFx
{
    public static void Attach(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            Motion.RiseIn(menu, Motion.Fast);
            ApplyPopupFx(menu);
        };
    }

    /// <summary>Rise-in + popup window fx for button flyouts. A plain Flyout animates its Content;
    /// a MenuFlyout has no content control, so the presenter is reached through the first realized
    /// item — either lookup failing just skips the effects.</summary>
    public static void AttachFlyout(FlyoutBase flyout)
    {
        flyout.Opened += (_, _) =>
        {
            Control? target = flyout switch
            {
                Flyout { Content: Control c } => c,
                MenuFlyout { Items.Count: > 0 } mf when mf.Items[0] is Control item =>
                    item.FindAncestorOfType<MenuFlyoutPresenter>() as Control ?? item,
                _ => null,
            };
            if (target is null) return;
            Motion.RiseIn(target, Motion.Fast);
            ApplyPopupFx(target);
        };
    }

    /// <summary>Full dropdown treatment for a ComboBox: rise-in, popup-window fx (rounded corners +
    /// the Lumen glass-variant blur), and eased wheel scrolling inside the popup's list (attached
    /// once, on first open — the popup's ScrollViewer doesn't exist until then).</summary>
    public static void AttachDropDown(ComboBox combo)
    {
        bool smoothed = false;
        combo.DropDownOpened += (_, _) =>
        {
            if (combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child is not Control c) return;
            Motion.RiseIn(c, Motion.Fast);
            ApplyPopupFx(c);
            if (!smoothed && c.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
            {
                SmoothScroll.Attach(sv);
                smoothed = true;
            }
        };
    }

    private static void ApplyPopupFx(Control anyInPopup)
    {
        ApplyPopupFxCore(anyInPopup);
        // Re-assert once the popup has fully materialized: on some opens the composition-attribute
        // blur lands before the popup surface is ready and silently does nothing — the menu then
        // shows the translucent tint with NO blur behind it (flat gray, owner report).
        Dispatcher.UIThread.Post(() => ApplyPopupFxCore(anyInPopup), DispatcherPriority.Loaded);
    }

    private static void ApplyPopupFxCore(Control anyInPopup)
    {
        try
        {
            if (TopLevel.GetTopLevel(anyInPopup) is not { } tl) return;
            var hwnd = tl.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            // Standard (8px) rounding, matching the content styles' CornerRadius 8 — mismatched
            // radii leave a wedge of popup surface visible in each corner (owner screenshot).
            Platform.WinChrome.RoundCorners(hwnd, small: false);

            var bg = ThemeManager.Current.MenuBackground;                 // "#AARRGGBB"
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return;
            // Text on a per-pixel-alpha surface must NOT use subpixel (ClearType) smoothing: its
            // RGB fringes composite against transparency and every glyph grows red/blue "glitchy"
            // edges (owner report — visible in every menu list). Grayscale AA renders clean on glass.
            TextOptions.SetTextRenderingMode(tl, TextRenderingMode.Antialias);
            tl.TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            };
            // Two blur mechanisms, oldest first: the legacy composition-attribute blur-behind
            // (works on popup windows on older builds, but the undocumented API is dying off in
            // newer Windows 11 builds), then the MODERN DWM system backdrop — historically ignored
            // on popup-class windows, but newer builds honor it there. Whichever the OS accepts
            // wins; the other is a harmless no-op.
            Platform.DwmAcrylic.BlurBehind(hwnd);
            Platform.DwmAcrylic.Apply(hwnd, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
        }
        catch { /* popups that reject the backdrop keep the translucent fallback */ }
    }
}
