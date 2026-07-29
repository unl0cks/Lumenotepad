using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

/// <summary>Windows-only window-chrome tweaks (DWM rounded corners). No-ops on other platforms.</summary>
public static class WinChrome
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    /// <summary>User-chosen window corner style: "round" | "small" | "square". Set from settings; RoundCorners reads it.</summary>
    public static string CornerStyle = "round";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- native caption move (enables Aero Snap) ----
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    /// <summary>Small corner rounding on a raw HWND — popup windows (context menus / flyouts) are
    /// not <see cref="Window"/>s, and an un-rounded popup surface pokes square corners out behind
    /// the rounded menu content.</summary>
    public static void RoundCorners(IntPtr hwnd, bool small = true)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch { /* pre-Windows-11: no rounded-corner API */ }
    }

    /// <summary>Round (or un-round) the window's corners on Windows 11. Safe to call anywhere.
    /// On macOS there is no DWM corner attribute and a BORDERLESS NSWindow is square, so the rounding
    /// has to come from the content — every child window already calls this, making it the one funnel
    /// point. The main window is skipped: it uses the native (already rounded) mac shell.</summary>
    public static void RoundCorners(Window window, bool round = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Small code-built dialogs (confirm / input / reorder / crop) are the only windows left
            // here; the five real secondary windows take the native mac frame instead. Keep these
            // OPAQUE: they use SizeToContent, and re-parenting their content into a rounding wrapper
            // left them fully see-through on macOS (tester report). Square but visible beats invisible.
            if (round && window.WindowDecorations == WindowDecorations.None)
                Services.ThemeManager.ApplyMacGlass(window, Services.ThemeManager.MacGlass.Opaque);
            return;
        }
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;
        int pref = !round ? DWMWCP_DEFAULT
                 : CornerStyle switch { "small" => DWMWCP_ROUNDSMALL, "square" => DWMWCP_DONOTROUND, _ => DWMWCP_ROUND };
        try { DwmSetWindowAttribute(handle.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch { /* pre-Windows-11: no rounded-corner API */ }
    }

    /// <summary>Make sure the window keeps the sizing-frame + maximize styles so the OS allows snapping
    /// (drag-to-top to maximize, drag-to-edge to half/quarter-snap). The frame stays invisible.</summary>
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
        catch { /* ignore */ }
    }

    /// <summary>Start the NATIVE caption drag so Windows Aero Snap engages (Avalonia's managed
    /// BeginMoveDrag doesn't snap). Returns false on non-Windows so callers can fall back.</summary>
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
