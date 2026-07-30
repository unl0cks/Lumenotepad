using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Win32;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

public partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _exiting;

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void EnsureTray()
    {
        if (_tray is not null) return;

        try
        {
            var open = new NativeMenuItem("Open Lumenotepad");
            open.Click += (_, _) => RestoreFromTray();
            var exit = new NativeMenuItem("Exit");
            exit.Click += (_, _) => ExitApp();
            var menu = new NativeMenu();
            menu.Add(open);
            menu.Add(exit);
            _tray = new TrayIcon
            {
                Icon = BuildTrayIcon(), ToolTipText = "Lumenotepad", IsVisible = true, Menu = menu,
            };
            _tray.Clicked += (_, _) => RestoreFromTray();
        }
        catch { _tray = null; }
    }

    private void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private void SyncTrayEnabled()
    {
        if (Vm is { } vm && (vm.CloseToTray || vm.MinimizeToTray)) EnsureTray();
        else DisposeTray();
    }

    private void HideToTray(bool animate)
    {
        EnsureTray();
        if (animate) Motion.CollapseOut(Host, 150, Hide);
        else Hide();
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Motion.ScaleIn(Host, 0.97, 220);
        ReassertChrome();
    }

    private void ExitApp()
    {
        _exiting = true;
        Vm?.FlushDirtyDocs();
        DisposeTray();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        else Close();
    }

    private static WindowIcon BuildTrayIcon()
    {
        try
        {
            return new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://Lumenotepad/Assets/lumenotepad.ico")));
        }
        catch
        {
            var accent = Color.Parse(Services.ThemeManager.Current.Accent);
            var visual = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(accent),
                Child = new TextBlock
                {
                    Text = "L", FontSize = 40, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            visual.Measure(new Size(64, 64));
            visual.Arrange(new Rect(0, 0, 64, 64));
            var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
            rtb.Render(visual);
            using var ms = new MemoryStream();
            rtb.Save(ms);
            ms.Position = 0;
            return new WindowIcon(ms);
        }
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_N = 0x4E;
    private const uint WM_HOTKEY = 0x0312;
    private const int SummonHotkeyId = 0x4C4E;

    private bool _hotkeyRegistered;
    private Win32Properties.CustomWndProcHookCallback? _wndHook;

    private void InstallWndHook()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_wndHook is not null) return;
        _wndHook = (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == SummonHotkeyId)
            {
                handled = true;
                Dispatcher.UIThread.Post(RestoreFromTray);
            }
            return IntPtr.Zero;
        };
        Win32Properties.AddWndProcHookCallback(this, _wndHook);
    }

    private void SyncHotkey()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Vm is { SummonHotkey: true }) RegisterSummon();
        else UnregisterSummon();
    }

    private void RegisterSummon()
    {
        if (!OperatingSystem.IsWindows() || _hotkeyRegistered) return;
        if (TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        InstallWndHook();
        _hotkeyRegistered = RegisterHotKey(hwnd, SummonHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);
    }

    private void UnregisterSummon()
    {
        if (!OperatingSystem.IsWindows() || !_hotkeyRegistered) return;
        if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
            UnregisterHotKey(hwnd, SummonHotkeyId);
        _hotkeyRegistered = false;
    }
}
