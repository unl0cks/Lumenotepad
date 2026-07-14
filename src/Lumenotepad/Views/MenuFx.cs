using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>Shared context-menu opening effects: the rise-in animation, and — when the active
/// theme's menu background is the translucent glass variant (alpha below 0xF0, i.e. Lumen with
/// Full theme on) — a real DWM acrylic backdrop on the menu's own popup window.</summary>
public static class MenuFx
{
    public static void Attach(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            Motion.RiseIn(menu, Motion.Fast);
            TryBlur(menu);
        };
    }

    /// <summary>Rise-in for button flyouts (the toolbar's bullet/highlight/color/font pickers, the
    /// dock menu, the sort menu). A plain Flyout animates its Content; a MenuFlyout has no content
    /// control, so the presenter is reached through the first realized item — either lookup failing
    /// just skips the animation.</summary>
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
            if (target is not null) Motion.RiseIn(target, Motion.Fast);
        };
    }

    private static void TryBlur(ContextMenu menu)
    {
        try
        {
            var bg = ThemeManager.Current.MenuBackground;                 // "#AARRGGBB"
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return;
            if (TopLevel.GetTopLevel(menu) is not { } tl) return;
            tl.TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            };
            if (tl.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
                Platform.DwmAcrylic.Apply(h, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
        }
        catch { /* popups that reject the backdrop keep the translucent fallback */ }
    }
}
