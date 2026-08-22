using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

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

            Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(anyInPopup) is { } tl) ApplyMacPopupFx(tl);
            }, DispatcherPriority.Loaded);
            return;
        }
        ApplyPopupFxCore(anyInPopup);

        Dispatcher.UIThread.Post(() => ApplyPopupFxCore(anyInPopup), DispatcherPriority.Loaded);
    }

    private static readonly WindowTransparencyLevel[] MacOff = { WindowTransparencyLevel.None };
    private static readonly WindowTransparencyLevel[] MacBlur = { WindowTransparencyLevel.AcrylicBlur };

    private static void ApplyMacPopupFx(TopLevel tl)
    {

        Platform.MacVibrancy.RoundPopup(tl, 8, Platform.MacVibrancy.Material);

        string bg = ThemeManager.Current.MenuBackground;
        var opaque = new SolidColorBrush(Color.Parse(bg.Length == 9 ? "#FF" + bg[^6..] : bg));
        tl.TransparencyBackgroundFallback = opaque;
        if (!ThemeManager.Current.FrostedWindow)
        {

            tl.Background = opaque;
            return;
        }

        tl.TransparencyLevelHint = MacOff;
        tl.TransparencyLevelHint = MacBlur;

        DispatcherTimer.RunOnce(() =>
        {
            if (Platform.MacVibrancy.HasFrostLayer(tl))
            {
                Platform.MacVibrancy.RoundPopup(tl, 8, Platform.MacVibrancy.Material);
                return;
            }
            tl.TransparencyLevelHint = MacOff;
        }, TimeSpan.FromMilliseconds(90));
    }

    private static void ApplyPopupFxCore(Control anyInPopup)
    {
        try
        {
            if (TopLevel.GetTopLevel(anyInPopup) is not { } tl) return;
            var hwnd = tl.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            Platform.WinChrome.RoundCorners(hwnd, small: false);

            var bg = ThemeManager.Current.MenuBackground;
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return;

            TextOptions.SetTextRenderingMode(tl, TextRenderingMode.Antialias);
            tl.TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            };

            Platform.DwmAcrylic.BlurBehind(hwnd, MenuTint(bg));
            Platform.DwmAcrylic.Apply(hwnd, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
        }
        catch {  }
    }

    private static uint MenuTint(string menuBackground)
    {
        try
        {
            uint v = Convert.ToUInt32(menuBackground.TrimStart('#'), 16);
            uint r = (v >> 16) & 0xFF, g = (v >> 8) & 0xFF, b = v & 0xFF;
            return 0x0A000000u | (b << 16) | (g << 8) | r;
        }
        catch { return 0x0A1C1614; }
    }
}
