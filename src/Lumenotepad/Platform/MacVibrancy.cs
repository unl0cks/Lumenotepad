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

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetByte(IntPtr receiver, IntPtr selector, byte value);

    /// <summary>Resolve the NSWindow behind an Avalonia window (the handle is normally the top-level
    /// NSView, but accept an NSWindow too). Zero when it cannot be resolved.</summary>
    private static IntPtr NSWindowOf(TopLevel window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return IntPtr.Zero;
        IntPtr nsWindowCls = GetClass("NSWindow"), nsViewCls = GetClass("NSView");
        if (nsWindowCls == IntPtr.Zero) return IntPtr.Zero;
        IntPtr isKindSel = Sel("isKindOfClass:");
        if (MsgIsKind(handle, isKindSel, nsWindowCls)) return handle;
        if (nsViewCls != IntPtr.Zero && MsgIsKind(handle, isKindSel, nsViewCls)) return Msg(handle, Sel("window"));
        return IntPtr.Zero;
    }

    /// <summary>Hide the close / minimise / zoom buttons. Dialogs take the native frame purely to get
    /// macOS's rounding, shadow and frost — they have their own buttons and must not sprout a second
    /// set of traffic lights.</summary>
    public static void HideTrafficLights(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NSWindowOf(window);
            if (nsWindow == IntPtr.Zero) return;
            IntPtr btnSel = Sel("standardWindowButton:"), hideSel = Sel("setHidden:");
            for (nint i = 0; i <= 2; i++)          // 0 close, 1 miniaturise, 2 zoom
            {
                IntPtr btn = MsgIndex(nsWindow, btnSel, i);
                if (btn != IntPtr.Zero) MsgSetByte(btn, hideSel, 1);
            }
        }
        catch { /* interop must never take the window down */ }
    }

    /// <summary>Keep the window's frost at full strength even when it is not the key window.
    /// Safe to call repeatedly.</summary>
    public static void KeepFrostActive(TopLevel window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr effectCls = GetClass("NSVisualEffectView");
            if (effectCls == IntPtr.Zero) return;
            IntPtr isKindSel = Sel("isKindOfClass:");
            IntPtr nsWindow = NSWindowOf(window);
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

    /// <summary>True when macOS actually gave this surface a frost layer. Popups do not always get
    /// one, and a transparent surface with NO frost behind it is simply invisible — callers use this
    /// to fall back to an opaque background instead of shipping an invisible menu.</summary>
    public static bool HasFrostLayer(TopLevel topLevel)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            IntPtr effectCls = GetClass("NSVisualEffectView");
            IntPtr nsWindow = NSWindowOf(topLevel);
            if (effectCls == IntPtr.Zero || nsWindow == IntPtr.Zero) return false;
            IntPtr content = Msg(nsWindow, Sel("contentView"));
            if (content == IntPtr.Zero) return false;
            IntPtr root = Msg(content, Sel("superview"));
            return Pin(root != IntPtr.Zero ? root : content, effectCls, Sel("isKindOfClass:"), 0);
        }
        catch { return false; }
    }

    /// <summary>Depth-limited walk: pin every NSVisualEffectView in the subtree to the active state.
    /// Returns true when at least one was found.</summary>
    private static bool Pin(IntPtr view, IntPtr effectCls, IntPtr isKindSel, int depth)
    {
        if (view == IntPtr.Zero || depth > 6) return false;
        bool found = false;
        if (MsgIsKind(view, isKindSel, effectCls))
        {
            MsgSetLong(view, Sel("setState:"), StateActive);
            found = true;
        }

        IntPtr subviews = Msg(view, Sel("subviews"));
        if (subviews == IntPtr.Zero) return found;
        nint count = MsgCount(subviews, Sel("count"));
        IntPtr atIndex = Sel("objectAtIndex:");
        for (nint i = 0; i < count && i < 64; i++)
            found |= Pin(MsgIndex(subviews, atIndex, i), effectCls, isKindSel, depth + 1);
        return found;
    }
}
