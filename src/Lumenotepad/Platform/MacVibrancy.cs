using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

internal static class MacVibrancy
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";

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
    private static extern nint MsgGetLong(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool MsgIsKind(IntPtr receiver, IntPtr selector, IntPtr cls);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetByte(IntPtr receiver, IntPtr selector, byte value);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetDouble(IntPtr receiver, IntPtr selector, double value);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void MsgSetPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    public static nint Material = 13;

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

    public static void HideTrafficLights(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NSWindowOf(window);
            if (nsWindow == IntPtr.Zero) return;
            IntPtr btnSel = Sel("standardWindowButton:"), hideSel = Sel("setHidden:");
            for (nint i = 0; i <= 2; i++)
            {
                IntPtr btn = MsgIndex(nsWindow, btnSel, i);
                if (btn != IntPtr.Zero) MsgSetByte(btn, hideSel, 1);
            }
        }
        catch {  }
    }

    public static void KeepFrostActive(TopLevel window, nint? material = null)
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

            IntPtr root = Msg(content, Sel("superview"));
            Pin(root != IntPtr.Zero ? root : content, effectCls, isKindSel, 0, 0, material ?? Material);
        }
        catch {  }
    }

    public static void DisableNativeFullScreen(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NSWindowOf(window);
            if (nsWindow == IntPtr.Zero) return;
            const nint Primary = 1 << 7, Auxiliary = 1 << 8, None = 1 << 9;

            nint behavior = MsgGetLong(nsWindow, Sel("collectionBehavior"));
            MsgSetLong(nsWindow, Sel("setCollectionBehavior:"), (behavior & ~(Primary | Auxiliary)) | None);
        }
        catch {  }
    }

    public static bool HasFrostLayer(TopLevel topLevel) => FrostLayerCount(topLevel) > 0;

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
            return Pin(root != IntPtr.Zero ? root : content, effectCls, Sel("isKindOfClass:"), 0, 0, 0);
        }
        catch { return 0; }
    }

    public static void RoundPopup(TopLevel topLevel, double radius, nint? material = null)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr nsWindow = NSWindowOf(topLevel);
            if (nsWindow == IntPtr.Zero) return;

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
            nint mat = material ?? Material;
            Pin(content, effectCls, isKindSel, 0, radius, mat);
            IntPtr root = Msg(content, Sel("superview"));
            if (root != IntPtr.Zero) Pin(root, effectCls, isKindSel, 0, radius, mat);
        }
        catch {  }
    }

    private static void RoundLayer(IntPtr view, double radius)
    {
        if (view == IntPtr.Zero || radius <= 0) return;
        MsgSetByte(view, Sel("setWantsLayer:"), 1);
        IntPtr layer = Msg(view, Sel("layer"));
        if (layer == IntPtr.Zero) return;
        MsgSetDouble(layer, Sel("setCornerRadius:"), radius);
        MsgSetByte(layer, Sel("setMasksToBounds:"), 1);
    }

    private static int Pin(IntPtr view, IntPtr effectCls, IntPtr isKindSel, int depth, double radius, nint material)
    {
        if (view == IntPtr.Zero || depth > 6) return 0;
        int found = 0;
        if (MsgIsKind(view, isKindSel, effectCls))
        {
            MsgSetLong(view, Sel("setState:"), StateActive);
            if (material > 0) MsgSetLong(view, Sel("setMaterial:"), material);
            RoundLayer(view, radius);
            found = 1;
        }

        IntPtr subviews = Msg(view, Sel("subviews"));
        if (subviews == IntPtr.Zero) return found;
        nint count = MsgCount(subviews, Sel("count"));
        IntPtr atIndex = Sel("objectAtIndex:");
        for (nint i = 0; i < count && i < 64; i++)
            found += Pin(MsgIndex(subviews, atIndex, i), effectCls, isKindSel, depth + 1, radius, material);
        return found;
    }
}
