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

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetDouble(IntPtr receiver, IntPtr selector, double value);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    /// <summary>NSVisualEffectMaterial forced onto every frost layer, or 0 to keep whatever Avalonia
    /// picked. Avalonia hard-codes one material and exposes no way to choose, and its choice frosts far
    /// paler than the DWM acrylic the Windows build uses — the tester's glass stayed washed out even
    /// with the tint slider at -100%. The "Glass material (macOS)" preference writes this.
    ///
    /// Values are documented NSVisualEffectMaterial constants (10.14+, all still valid on macOS 26);
    /// only ones from that fixed list are ever assigned, because an out-of-range material is one of the
    /// few things here that AppKit can raise on.</summary>
    public static nint Material;

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
            Pin(root != IntPtr.Zero ? root : content, effectCls, isKindSel, 0, 0);
        }
        catch { /* interop must never take the window (or the app) down */ }
    }

    /// <summary>True when macOS actually gave this surface a frost layer. Popups do not always get
    /// one, and a transparent surface with NO frost behind it is simply invisible — callers use this
    /// to fall back to an opaque background instead of shipping an invisible menu.</summary>
    public static bool HasFrostLayer(TopLevel topLevel) => FrostLayerCount(topLevel) > 0;

    /// <summary>How many NSVisualEffectViews this surface actually has (also re-pins them). Written to
    /// the chrome diagnostics: whether the OS granted a frost layer at all is the one fact that decides
    /// between "our request was wrong" and "macOS declines frost here", and it cannot be observed from
    /// Windows.</summary>
    public static int FrostLayerCount(TopLevel topLevel)
    {
        if (!OperatingSystem.IsMacOS()) return 0;
        try
        {
            IntPtr effectCls = GetClass("NSVisualEffectView");
            IntPtr nsWindow = NSWindowOf(topLevel);
            if (effectCls == IntPtr.Zero || nsWindow == IntPtr.Zero) return 0;
            IntPtr content = Msg(nsWindow, Sel("contentView"));
            if (content == IntPtr.Zero) return 0;
            IntPtr root = Msg(content, Sel("superview"));
            return Pin(root != IntPtr.Zero ? root : content, effectCls, Sel("isKindOfClass:"), 0, 0);
        }
        catch { return 0; }
    }

    /// <summary>Round a POPUP's window surface (menus, flyouts, combo drop-downs). Windows rounds these
    /// through DWM; a macOS popup NSWindow is an unrounded opaque rectangle, so its square corners show
    /// through behind the menu's rounded content (tester screenshot: sharp-cornered context menus).
    /// A popup cannot take the native frame the way our dialogs do, so the shape has to come from the
    /// layers: clear the window surface, then round the content view AND every frost layer, since the
    /// NSVisualEffectView is a square pane that Avalonia parks beside the content view rather than
    /// inside it.</summary>
    public static void RoundPopup(TopLevel topLevel, double radius)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NSWindowOf(topLevel);
            if (nsWindow == IntPtr.Zero) return;
            // An opaque window surface fills the corners we are about to cut out of the layers.
            MsgSetByte(nsWindow, Sel("setOpaque:"), 0);
            IntPtr colorCls = GetClass("NSColor");
            if (colorCls != IntPtr.Zero)
            {
                IntPtr clear = Msg(colorCls, Sel("clearColor"));
                if (clear != IntPtr.Zero) MsgSetPtr(nsWindow, Sel("setBackgroundColor:"), clear);
            }

            IntPtr content = Msg(nsWindow, Sel("contentView"));
            if (content == IntPtr.Zero) return;
            RoundLayer(content, radius);
            IntPtr effectCls = GetClass("NSVisualEffectView");
            if (effectCls == IntPtr.Zero) return;
            IntPtr isKindSel = Sel("isKindOfClass:");
            Pin(content, effectCls, isKindSel, 0, radius);
            IntPtr root = Msg(content, Sel("superview"));
            if (root != IntPtr.Zero) Pin(root, effectCls, isKindSel, 0, radius);
        }
        catch { /* interop must never take the window (or the app) down */ }
    }

    /// <summary>Give a view a backing layer with rounded, clipping corners.</summary>
    private static void RoundLayer(IntPtr view, double radius)
    {
        if (view == IntPtr.Zero || radius <= 0) return;
        MsgSetByte(view, Sel("setWantsLayer:"), 1);
        IntPtr layer = Msg(view, Sel("layer"));
        if (layer == IntPtr.Zero) return;
        MsgSetDouble(layer, Sel("setCornerRadius:"), radius);
        MsgSetByte(layer, Sel("setMasksToBounds:"), 1);
    }

    /// <summary>Depth-limited walk: pin every NSVisualEffectView in the subtree to the active state,
    /// apply the chosen material, and (when <paramref name="radius"/> is set) round its layer.
    /// Returns how many were found.</summary>
    private static int Pin(IntPtr view, IntPtr effectCls, IntPtr isKindSel, int depth, double radius)
    {
        if (view == IntPtr.Zero || depth > 6) return 0;
        int found = 0;
        if (MsgIsKind(view, isKindSel, effectCls))
        {
            MsgSetLong(view, Sel("setState:"), StateActive);
            if (Material > 0) MsgSetLong(view, Sel("setMaterial:"), Material);
            RoundLayer(view, radius);
            found = 1;
        }

        IntPtr subviews = Msg(view, Sel("subviews"));
        if (subviews == IntPtr.Zero) return found;
        nint count = MsgCount(subviews, Sel("count"));
        IntPtr atIndex = Sel("objectAtIndex:");
        for (nint i = 0; i < count && i < 64; i++)
            found += Pin(MsgIndex(subviews, atIndex, i), effectCls, isKindSel, depth + 1, radius);
        return found;
    }
}
