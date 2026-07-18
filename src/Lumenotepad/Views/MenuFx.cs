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
            ApplyPopupFx(menu, OwnerOf(menu.PlacementTarget));
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
            ApplyPopupFx(target, OwnerOf(flyout.Target));
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
            ApplyPopupFx(c, OwnerOf(combo));
            if (!smoothed && c.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
            {
                SmoothScroll.Attach(sv);
                smoothed = true;
            }
        };
    }

    /// <summary>The window a popup opens over (for the frost snapshot): the placement target's own
    /// window, falling back to the app's main window.</summary>
    private static Window? OwnerOf(Control? placementTarget) =>
        (placementTarget is null ? null : TopLevel.GetTopLevel(placementTarget) as Window)
        ?? (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static void ApplyPopupFx(Control anyInPopup, Window? owner)
    {
        if (ApplyPopupFxCore(anyInPopup, owner)) return;   // fake frost applied — nothing to re-assert
        // Re-assert once the popup has fully materialized: on some opens the composition-attribute
        // blur lands before the popup surface is ready and silently does nothing — the menu then
        // shows the translucent tint with NO blur behind it (flat gray, owner report).
        Dispatcher.UIThread.Post(() => ApplyPopupFxCore(anyInPopup, owner), DispatcherPriority.Loaded);
    }

    /// <summary>Returns true when the continuous fake frost was painted (no DWM follow-up needed).</summary>
    private static bool ApplyPopupFxCore(Control anyInPopup, Window? owner)
    {
        try
        {
            if (TopLevel.GetTopLevel(anyInPopup) is not { } tl) return false;
            var hwnd = tl.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            // Standard (8px) rounding, matching the content styles' CornerRadius 8 — mismatched
            // radii leave a wedge of popup surface visible in each corner (owner screenshot).
            Platform.WinChrome.RoundCorners(hwnd, small: false);

            var bg = ThemeManager.Current.MenuBackground;                 // "#AARRGGBB"
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return false;
            // Text on a per-pixel-alpha surface must NOT use subpixel (ClearType) smoothing: its
            // RGB fringes composite against transparency and every glyph grows red/blue "glitchy"
            // edges (owner report — visible in every menu list). Grayscale AA renders clean on glass.
            RenderOptions.SetTextRenderingMode(tl, TextRenderingMode.Antialias);
            tl.TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            };

            // Menu blur strength: 0 = completely clear (the translucent tint alone, no blur), else a
            // TRULY CONTINUOUS strength — the menu paints its own frost (a blurred snapshot of the
            // window under it), because DWM itself only offers fixed levels. If the snapshot can't be
            // taken, fall back to the nearest real DWM tier.
            int pct = BlurPrefs.MenusPct;
            if (pct <= 0)
            {
                Platform.DwmAcrylic.DisableBlurBehind(hwnd);
                ResetBackdrop(anyInPopup);
                return false;
            }
            if (owner is not null && MenuFrost.TryBackdrop(tl, owner, pct) is { } frost)
            {
                Platform.DwmAcrylic.DisableBlurBehind(hwnd);
                SetBackdrop(anyInPopup, frost);
                return true;
            }
            ResetBackdrop(anyInPopup);
            if (BlurPrefs.TierOf(pct) == BlurPrefs.Tier.Soft)
                Platform.DwmAcrylic.BlurBehind(hwnd, soft: true);
            else
            {
                Platform.DwmAcrylic.BlurBehind(hwnd);
                Platform.DwmAcrylic.Apply(hwnd, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
            }
        }
        catch { /* popups that reject the backdrop keep the translucent fallback */ }
        return false;
    }

    /// <summary>Paint the frost image as the popup content's background (the layer that normally
    /// paints the translucent menu tint).</summary>
    private static void SetBackdrop(Control anyInPopup, Avalonia.Media.IBrush brush)
    {
        switch (anyInPopup)
        {
            case ContextMenu cm: cm.Background = brush; break;
            case MenuFlyoutPresenter mp: mp.Background = brush; break;
            default:
                if (anyInPopup.FindAncestorOfType<FlyoutPresenter>() is { } fp) fp.Background = brush;
                else if (anyInPopup is Border b) b.Background = brush;
                else if (anyInPopup.FindAncestorOfType<Border>() is { } ab) ab.Background = brush;
                break;
        }
    }

    /// <summary>Back to the themed translucent brush (style setter) after a frost image was set.</summary>
    private static void ResetBackdrop(Control anyInPopup)
    {
        switch (anyInPopup)
        {
            case ContextMenu cm: cm.ClearValue(ContextMenu.BackgroundProperty); break;
            case MenuFlyoutPresenter mp: mp.ClearValue(MenuFlyoutPresenter.BackgroundProperty); break;
            default:
                if (anyInPopup.FindAncestorOfType<FlyoutPresenter>() is { } fp) fp.ClearValue(FlyoutPresenter.BackgroundProperty);
                else if (anyInPopup is Border b) b.ClearValue(Border.BackgroundProperty);
                else if (anyInPopup.FindAncestorOfType<Border>() is { } ab) ab.ClearValue(Border.BackgroundProperty);
                break;
        }
    }
}
