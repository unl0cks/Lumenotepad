using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private static void ApplyPopupFx(Control anyInPopup)
    {
        try
        {
            if (TopLevel.GetTopLevel(anyInPopup) is not { } tl) return;
            var hwnd = tl.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            Platform.WinChrome.RoundCorners(hwnd);                        // every theme: no square popup corners

            var bg = ThemeManager.Current.MenuBackground;                 // "#AARRGGBB"
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return;
            tl.TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            };
            if (hwnd != IntPtr.Zero)
                Platform.DwmAcrylic.Apply(hwnd, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
        }
        catch { /* popups that reject the backdrop keep the translucent fallback */ }
    }
}
