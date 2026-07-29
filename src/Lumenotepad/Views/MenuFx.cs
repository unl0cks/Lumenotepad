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
        if (!OperatingSystem.IsWindows())
        {
            // ONCE, and only after the popup surface exists. Everything the mac path does needs the
            // NSWindow, and — unlike the Windows path — re-running it is actively harmful: see
            // ApplyMacPopupFx.
            Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(anyInPopup) is { } tl) ApplyMacPopupFx(tl);
            }, DispatcherPriority.Loaded);
            return;
        }
        ApplyPopupFxCore(anyInPopup);
        // Re-assert once the popup has fully materialized: on some opens the composition-attribute
        // blur lands before the popup surface is ready and silently does nothing — the menu then
        // shows the translucent tint with NO blur behind it (flat gray, owner report).
        Dispatcher.UIThread.Post(() => ApplyPopupFxCore(anyInPopup), DispatcherPriority.Loaded);
    }

    private static readonly WindowTransparencyLevel[] MacOff = { WindowTransparencyLevel.None };
    private static readonly WindowTransparencyLevel[] MacBlur = { WindowTransparencyLevel.AcrylicBlur };

    /// <summary>macOS menu material. Frost under a glass theme (Lumen), opaque otherwise — and always
    /// with an OPAQUE fallback set first, because the themed menu fill is only ~25% opaque: it is
    /// designed to sit over a blur, so a transparent surface with no frost behind it is an invisible
    /// menu (exactly what the tester hit). After the popup materialises we check whether macOS really
    /// gave it a frost layer; if it did not, the request is withdrawn so the opaque fallback paints.
    ///
    /// The transition MUST step through None. Avalonia's backend ignores any level equal to the one
    /// already active, and a pass that ends up applying nothing falls through and forces the window
    /// back to Opaque — so plainly re-asserting AcrylicBlur on an already-frosted popup DESTROYS its
    /// frost. That is what turned every mac menu opaque in 1.1.1: the fx ran twice per open, the
    /// second run knocked the popup to Opaque, and the frost check then withdrew the request for
    /// good. Fixed on both sides — this runs once per open now, and the assignment is a real
    /// transition either way.</summary>
    private static void ApplyMacPopupFx(TopLevel tl)
    {
        // Square popup corners poke out from behind the rounded menu content; macOS rounds neither the
        // popup window nor its frost pane for us, so cut the radius into the layers ourselves.
        Platform.MacVibrancy.RoundPopup(tl, 8);

        string bg = ThemeManager.Current.MenuBackground;
        var opaque = new SolidColorBrush(Color.Parse(bg.Length == 9 ? "#FF" + bg[^6..] : bg));
        tl.TransparencyBackgroundFallback = opaque;
        if (!ThemeManager.Current.FrostedWindow)
        {
            // Rounding required clearing the NSWindow's own surface, so under a solid theme the popup
            // must paint its fill from the CONTENT side — otherwise cutting the corners would also cut
            // away the only thing drawing the menu's background.
            tl.Background = opaque;
            return;
        }

        tl.TransparencyLevelHint = MacOff;
        tl.TransparencyLevelHint = MacBlur;
        // Verify on a real timer rather than another dispatcher pass: AppKit builds the effect view
        // when it services the window, which is not guaranteed to have happened by the next frame.
        DispatcherTimer.RunOnce(() =>
        {
            if (Platform.MacVibrancy.HasFrostLayer(tl)) { Platform.MacVibrancy.RoundPopup(tl, 8); return; }
            tl.TransparencyLevelHint = MacOff;                // no frost: let the opaque fallback paint
        }, TimeSpan.FromMilliseconds(90));
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
