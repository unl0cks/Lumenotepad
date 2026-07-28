using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

/// <summary>macOS only. By default an NSVisualEffectView follows its window's ACTIVE state, so the
/// frost drains to flat grey the moment another window takes focus — opening Preferences made the
/// main window look dead (tester report). AppKit exposes the fix as a one-line property, but Avalonia
/// creates the effect view internally and does not surface it, so we reach it through the Objective-C
/// runtime: resolve the NSWindow behind the Avalonia handle, walk its view tree, and pin every
/// NSVisualEffectView to NSVisualEffectStateActive.
///
/// Every call is defensive — a missing class, a changed view tree, or a handle that is not what we
/// expect leaves the window exactly as Avalonia set it up. No-op off macOS.</summary>
internal static class MacVibrancy
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";

    // NSVisualEffectState
    private const nint StateActive = 1;

    [DllImport(Objc, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Objc, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Msg(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgIndex(IntPtr receiver, IntPtr selector, nint index);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetLong(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern nint MsgCount(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool MsgIsKind(IntPtr receiver, IntPtr selector, IntPtr cls);

    /// <summary>Keep <paramref name="window"/>'s frost at full strength even when it is not the key
    /// window. Safe to call repeatedly (setting the same state twice is harmless).</summary>
    public static void KeepFrostActive(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;

            IntPtr nsWindowCls = GetClass("NSWindow");
            IntPtr nsViewCls = GetClass("NSView");
            IntPtr effectCls = GetClass("NSVisualEffectView");
            if (nsWindowCls == IntPtr.Zero || effectCls == IntPtr.Zero) return;

            IntPtr isKindSel = Sel("isKindOfClass:");

            // Avalonia hands back the top-level NSView on macOS, but accept an NSWindow too.
            IntPtr nsWindow = IntPtr.Zero;
            if (MsgIsKind(handle, isKindSel, nsWindowCls))
                nsWindow = handle;
            else if (nsViewCls != IntPtr.Zero && MsgIsKind(handle, isKindSel, nsViewCls))
                nsWindow = Msg(handle, Sel("window"));
            if (nsWindow == IntPtr.Zero) return;

            IntPtr content = Msg(nsWindow, Sel("contentView"));
            if (content == IntPtr.Zero) return;
            // The frost usually sits BESIDE the content view under the window's theme frame, so start
            // from the superview when there is one — that covers both layouts.
            IntPtr root = Msg(content, Sel("superview"));
            Pin(root != IntPtr.Zero ? root : content, effectCls, isKindSel, 0);
        }
        catch { /* interop must never take the window (or the app) down */ }
    }

    /// <summary>Depth-limited walk: set every NSVisualEffectView in the subtree to the active state.</summary>
    private static void Pin(IntPtr view, IntPtr effectCls, IntPtr isKindSel, int depth)
    {
        if (view == IntPtr.Zero || depth > 6) return;
        if (MsgIsKind(view, isKindSel, effectCls))
            MsgSetLong(view, Sel("setState:"), StateActive);

        IntPtr subviews = Msg(view, Sel("subviews"));
        if (subviews == IntPtr.Zero) return;
        nint count = MsgCount(subviews, Sel("count"));
        IntPtr atIndex = Sel("objectAtIndex:");
        for (nint i = 0; i < count && i < 64; i++)
            Pin(MsgIndex(subviews, atIndex, i), effectCls, isKindSel, depth + 1);
    }
}
