using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (!OperatingSystem.IsWindows())
        {
            // macOS: keep the NATIVE window shell — rounded corners, shadow, traffic lights, and
            // working native fullscreen — and extend our UI up under its (hidden) titlebar. The
            // Windows recipe (WindowDecorations=None + DWM) gave macOS a square, fullscreen-broken
            // borderless NSWindow (tester report). Our own caption buttons hide in MainView.
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 44;   // traffic lights centre on our title bar band
            // NOTE: deliberately NOT touching TransparencyLevelHint here. The XAML already asks for
            // AcrylicBlur first, and that request is honoured on its FIRST application — re-assigning
            // it (as an earlier build did) re-runs the backend path that downgrades the frost.
        }
        DataContextChanged += (_, _) => HookThemeVm();
    }

    /// <summary>The edge-grip overlay only exists for the chromeless Windows shell; on macOS the
    /// native frame resizes and the overlay would fight its edge hit-testing.</summary>
    private bool UseResizeOverlay => OperatingSystem.IsWindows();

    private ViewModels.MainViewModel? _themeVm;

    private void HookThemeVm()
    {
        if (_themeVm is not null) _themeVm.PropertyChanged -= OnThemePropertyChanged;
        _themeVm = DataContext as ViewModels.MainViewModel;
        if (_themeVm is not null)
        {
            _themeVm.PropertyChanged += OnThemePropertyChanged;
            ApplyTheme();
            Topmost = _themeVm.AlwaysOnTop;
        }
    }

    private void OnThemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.MainViewModel.Theme)
            or nameof(ViewModels.MainViewModel.FullTheme)
            or nameof(ViewModels.MainViewModel.PaperLight)
            or nameof(ViewModels.MainViewModel.CustomAccent)
            or nameof(ViewModels.MainViewModel.AccentFollowsNotebook))
            ApplyTheme();
        else if ((e.PropertyName is nameof(ViewModels.MainViewModel.SelectedNotebook)
                  or nameof(ViewModels.MainViewModel.IsHomeVisible))
                 && _themeVm is { AccentFollowsNotebook: true })
            ApplyTheme();

        if (e.PropertyName == nameof(ViewModels.MainViewModel.AlwaysOnTop) && _themeVm is { } topVm)
            Topmost = topVm.AlwaysOnTop;

        if (e.PropertyName is nameof(ViewModels.MainViewModel.CloseToTray)
            or nameof(ViewModels.MainViewModel.MinimizeToTray))
            SyncTrayEnabled();
        if (e.PropertyName == nameof(ViewModels.MainViewModel.SummonHotkey))
            SyncHotkey();
    }

    private void ApplyTheme()
    {
        if (_themeVm is not { } vm || Application.Current is not { } app) return;
        var tokens = Services.ThemePalettes.Resolve(vm.Theme, vm.FullTheme, vm.PaperLight);
        // Inside a notebook, its cover color can drive the whole accent (pref); otherwise the
        // user's custom accent; otherwise the theme's own.
        string? seed = vm.AccentFollowsNotebook && !vm.IsHomeVisible && vm.SelectedNotebook is { } nb
            ? Services.ThemePalettes.NormalizeHex(nb.Color)
            : Services.ThemePalettes.NormalizeHex(vm.CustomAccent);
        if (seed is { } accent) tokens = Services.ThemePalettes.WithAccent(tokens, accent);
        Services.ThemeManager.Apply(app, tokens);
        Services.ThemeManager.ApplyChrome(this);
        // macOS: request real blur + repaint the opaque-dark fallback per the just-applied theme so the
        // frost never washes to grey. (No-op on Windows, where DWM ApplyChrome above does the work.)
        if (!OperatingSystem.IsWindows()) RearmMacGlass();
    }

    private bool _closingAnimated;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as ViewModels.MainViewModel)?.FlushDirtyDocs();   // never lose the last keystrokes
        if (_exiting) return;                                          // tray Exit: close immediately
        if (Vm is { CloseToTray: true })                               // close hides to the tray instead
        {
            e.Cancel = true;
            HideToTray(animate: true);
            return;
        }
        if (_closingAnimated) return;                                  // second pass: let the close through
        e.Cancel = true;
        _closingAnimated = true;
        Motion.CollapseOut(Host, 150, Close);                          // quick fade + shrink, then close
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeTray();
        UnregisterSummon();
        base.OnClosed(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ReassertChrome();
        ResizeBorder.IsVisible = UseResizeOverlay && WindowState == WindowState.Normal;
        SyncMaximizeMargin();
        // Some events (display-mode flips, DWM resets) silently drop the corner preference WITHOUT a
        // WindowState change; re-assert it whenever the floating window regains focus — self-heals square corners.
        Activated += (_, _) =>
        {
            if (WindowState == WindowState.Normal) WinChrome.RoundCorners(this, true);
            else if (WindowState == WindowState.Maximized) Services.ThemeManager.RefreshBackdrop(this);   // clear any stuck wash on re-focus
        };
        Motion.ScaleIn(Host, 0.97, 220);                               // launch: fade + scale in
        SyncTrayEnabled();
        SyncHotkey();

        if (!OperatingSystem.IsWindows())
        {
            // Re-assert the frost NOW that the native window exists — a transparency hint applied
            // before the handle is created can be dropped, leaving an opaque window.
            RearmMacGlass();
            // A natively full-screen window owns its Space and has NOTHING behind it, so the frost has
            // no source and collapses to flat grey — unfixable while the window is really full screen.
            // Turning native full screen off makes the green button zoom instead: still fills the
            // screen, still on the desktop, glass intact.
            MacVibrancy.DisableNativeFullScreen(this);
            DispatcherTimer.RunOnce(RearmMacGlass, TimeSpan.FromMilliseconds(200));
            DispatcherTimer.RunOnce(WriteMacChromeDiagnostics, TimeSpan.FromMilliseconds(1200));
        }
    }

    /// <summary>Re-arm the macOS backdrop for the active theme (frost for glass themes, opaque
    /// otherwise) and pin the frost so it does not drain while another window is focused.</summary>
    internal void RearmMacGlass()
    {
        Services.ThemeManager.ApplyMacGlass(this, Services.ThemeManager.Current.GlassWindow
            ? Services.ThemeManager.MacGlass.Frost
            : Services.ThemeManager.MacGlass.Opaque);
        MacVibrancy.KeepFrostActive(this);
        // Avalonia writes collectionBehavior itself, so keep re-asserting ours — this runs on every
        // window-state change, which is exactly when it would be overwritten.
        MacVibrancy.DisableNativeFullScreen(this);
    }

    /// <summary>Record what the OS actually granted, so a "still not transparent" report can be
    /// diagnosed from the tester's Mac without guesswork (no macOS machine on this side).</summary>
    private void WriteMacChromeDiagnostics()
    {
        try
        {
            var dir = Services.AppSettings.DefaultDir;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "macos-chrome-diagnostics.txt"),
                $"os                      = {Environment.OSVersion.VersionString}\n" +
                $"arch                    = {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\n" +
                $"requestedLevels         = {string.Join(", ", TransparencyLevelHint)}\n" +
                $"ActualTransparencyLevel = {ActualTransparencyLevel}\n" +
                $"ActualThemeVariant      = {ActualThemeVariant}\n" +
                $"WindowDecorations       = {WindowDecorations}\n" +
                $"extendedIntoDecorations = {IsExtendedIntoWindowDecorations}\n" +
                $"windowBackground        = {Background}\n" +
                $"fallbackBrush           = {TransparencyBackgroundFallback}\n" +
                // Whether the OS granted a frost layer AT ALL is the one thing that separates "our
                // request was wrong" from "macOS declines frost here" — and it cannot be observed
                // from Windows, so it has to come back in the file.
                $"frostLayers             = {MacVibrancy.FrostLayerCount(this)}\n" +
                $"glassMaterial           = {MacVibrancy.Material}\n");
        }
        catch { /* diagnostics must never break startup */ }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // macOS 26 tiling ("Fill", the halves and quarters in the green button's menu) resizes the
        // window WITHOUT flipping WindowState, so the state-change path below never runs for it. Catch
        // the resize itself as well, or a tiled window keeps whatever inset and layout it had.
        if (change.Property == ClientSizeProperty && !OperatingSystem.IsWindows())
        {
            RelayoutMac();
            return;
        }
        if (change.Property != WindowStateProperty) return;

        var state = change.GetNewValue<WindowState>();

        if (state == WindowState.Minimized && Vm is { MinimizeToTray: true })
        {
            HideToTray(animate: false);
            return;               // hidden — skip the maximize-margin / chrome bookkeeping
        }

        // A maximized / full-screen window must NOT expose the custom resize grips — they bypass the OS and
        // would let you drag-resize an edge a maximized window should treat as fixed. Only float them in Normal.
        ResizeBorder.IsVisible = UseResizeOverlay && state == WindowState.Normal;

        // Snapping / maximizing strips the sizing styles (WS_THICKFRAME), so the SECOND snap after a
        // snap-to-top silently fails unless we re-assert them on every state change.
        WinChrome.EnableSnap(this);

        if (!OperatingSystem.IsWindows())
        {
            // macOS REBUILDS the window when it enters or leaves fullscreen, and the rebuilt one has no
            // NSVisualEffectView — the Lumen frost died on going fullscreen (tester report). The
            // transition is animated (~1s), so a single immediate re-arm lands too early and is thrown
            // away with the old window; re-arm across the whole animation instead.
            RearmMacGlass();
            RelayoutMac();
            foreach (var ms in new[] { 150, 500, 950, 1500 })
                DispatcherTimer.RunOnce(() => { RearmMacGlass(); RelayoutMac(); },
                    TimeSpan.FromMilliseconds(ms));
        }

        // Native Aero Snap maximizes a WS_THICKFRAME window to (-7,-7) + a 14px oversize — a ~7px overhang past
        // every screen edge that clips the title bar / caption buttons (the maximize BUTTON constrains cleanly to
        // the work area). Avalonia leaves OffScreenMargin at 0 here, so compute the real overhang from the window
        // rect vs the monitor work area and inset the content by it. Bounds can settle a beat late — restagger.
        SyncMaximizeMargin();
        Dispatcher.UIThread.Post(SyncMaximizeMargin, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(SyncMaximizeMargin, TimeSpan.FromMilliseconds(120));
        DispatcherTimer.RunOnce(SyncMaximizeMargin, TimeSpan.FromMilliseconds(400));

        if (state == WindowState.Normal)
        {
            // Restoring drops the DWM corner + backdrop attributes, leaving square corners. The native
            // frame is rebuilt a beat AFTER WindowState flips, so one synchronous call is too early —
            // re-assert now and again as the frame settles.
            ReassertChrome();
            Dispatcher.UIThread.Post(ReassertChrome, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(ReassertChrome, TimeSpan.FromMilliseconds(150));
            DispatcherTimer.RunOnce(ReassertChrome, TimeSpan.FromMilliseconds(450));   // slow frame restores
        }
        else if (state == WindowState.Maximized)
        {
            // Snap-maximize can leave the acrylic backdrop showing DWM's snap-animation surface (a stuck
            // bright wash) that otherwise only clears on the next activation (alt-tab). The wash lands
            // ~1s in, past a single early re-assert — toggle the backdrop off→on repeatedly as the maximize
            // settles so it recomposites and clears itself.
            foreach (var ms in new[] { 120, 450, 900, 1400 })
                DispatcherTimer.RunOnce(() => Services.ThemeManager.RefreshBackdrop(this), TimeSpan.FromMilliseconds(ms));
        }
    }

    /// <summary>Inset the content by however much the window overhangs the monitor work area when maximized, so
    /// a native snap-maximize (which overhangs ~7px per edge) lands pixel-identical to the clean button-maximize.
    /// Zero inset when floating or cleanly maximized. Computed geometry — Avalonia's OffScreenMargin stays 0 here.</summary>
    private void SyncMaximizeMargin()
    {
        // WINDOWS ONLY. Aero Snap's ~7px-per-edge overhang is a Win32 WS_THICKFRAME artefact; nothing
        // off Windows overhangs, so there is nothing to compensate for. Worse, the geometry this relies
        // on does not mean the same thing on macOS — WorkingArea, Position and ClientSize do not all
        // agree on logical vs physical units on a Retina screen — so a zoomed window produced a bogus
        // inset hundreds of points deep and squeezed the whole UI into the top-left corner, scrollbars
        // and all (tester report on zoom/tile).
        if (!OperatingSystem.IsWindows()) { Host.Margin = default; return; }
        if (WindowState != WindowState.Maximized ||
            (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } scr)
        {
            Host.Margin = default;
            return;
        }

        Host.Margin = WindowGeometry.MaximizeInset(scr.WorkingArea, Position, ClientSize, RenderScaling);
    }

    /// <summary>Drop any content inset and force a fresh layout pass against the window's CURRENT size.
    /// macOS resizes the window itself when it zooms or tiles — the app never asks — and the content was
    /// left laid out to the old size, crammed into a corner with scrollbars along where the previous
    /// window edge had been. Cheap and idempotent, so it is safe to fire repeatedly through the resize
    /// animation alongside the glass re-arm.</summary>
    private void RelayoutMac()
    {
        Host.Margin = default;
        Host.InvalidateMeasure();
        Host.InvalidateArrange();
    }

    /// <summary>Re-apply all native chrome that a state change can silently reset: sizing styles (snap),
    /// rounded corners, and the acrylic backdrop.</summary>
    private void ReassertChrome()
    {
        WinChrome.EnableSnap(this);
        WinChrome.RoundCorners(this, true);
        Services.ThemeManager.ApplyChrome(this);   // acrylic + immersive dark per the active theme
    }
}
