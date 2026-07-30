using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

public static class WinChrome
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    public static string CornerStyle = "round";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    public static void RoundCorners(IntPtr hwnd, bool small = true)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch {  }
    }

    public static void RoundCorners(Window window, bool round = true)
    {
        if (!OperatingSystem.IsWindows())
        {

            if (round && window.WindowDecorations == WindowDecorations.None)
                Services.ThemeManager.ApplyMacGlass(window, Services.ThemeManager.MacGlass.Opaque);
            return;
        }
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;
        int pref = !round ? DWMWCP_DEFAULT
                 : CornerStyle switch { "small" => DWMWCP_ROUNDSMALL, "square" => DWMWCP_DONOTROUND, _ => DWMWCP_ROUND };
        try { DwmSetWindowAttribute(handle.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch {  }
    }

    public static void EnableSnap(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;
        try
        {
            int style = GetWindowLong(handle.Handle, GWL_STYLE);
            SetWindowLong(handle.Handle, GWL_STYLE, style | WS_THICKFRAME | WS_MAXIMIZEBOX);
        }
        catch {  }
    }

    public static bool BeginNativeMoveDrag(Window window)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return false;
        try
        {
            ReleaseCapture();
            SendMessage(handle.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            return true;
        }
        catch { return false; }
    }
}
