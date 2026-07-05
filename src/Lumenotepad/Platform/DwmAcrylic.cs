using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

/// <summary>Applies the Windows 11 DWM system backdrop (Mica / Acrylic) behind a chromeless window,
/// plus immersive dark mode. No-ops on non-Windows or pre-Win11 builds. Pair with a transparent
/// window Background and a tint overlay in XAML for the frosted-glass look.</summary>
public static class DwmAcrylic
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>DWM_SYSTEMBACKDROP_TYPE values.</summary>
    public enum Backdrop { None = 1, Mica = 2, Acrylic = 3, Tabbed = 4 }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window, Backdrop backdrop = Backdrop.Acrylic, bool dark = true)
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = window.TryGetPlatformHandle();
        if (h is null || h.Handle == IntPtr.Zero) return;
        try
        {
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(h.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
            int b = (int)backdrop;
            DwmSetWindowAttribute(h.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref b, sizeof(int));
        }
        catch { /* pre-Win11: no system-backdrop API */ }
    }
}
