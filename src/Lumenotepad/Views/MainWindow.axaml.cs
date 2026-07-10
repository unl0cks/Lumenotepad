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
        DataContextChanged += (_, _) => HookThemeVm();
    }

    private ViewModels.MainViewModel? _themeVm;

    private void HookThemeVm()
    {
        if (_themeVm is not null) _themeVm.PropertyChanged -= OnThemePropertyChanged;
        _themeVm = DataContext as ViewModels.MainViewModel;
        if (_themeVm is not null)
        {
            _themeVm.PropertyChanged += OnThemePropertyChanged;
            ApplyTheme();
        }
    }

    private void OnThemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.MainViewModel.Theme)
            or nameof(ViewModels.MainViewModel.FullTheme)
            or nameof(ViewModels.MainViewModel.PaperLight)
            or nameof(ViewModels.MainViewModel.CustomAccent))
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_themeVm is not { } vm || Application.Current is not { } app) return;
        var tokens = Services.ThemePalettes.Resolve(vm.Theme, vm.FullTheme, vm.PaperLight);
        if (Services.ThemePalettes.NormalizeHex(vm.CustomAccent) is { } accent)
            tokens = Services.ThemePalettes.WithAccent(tokens, accent);
        Services.ThemeManager.Apply(app, tokens);
        Services.ThemeManager.ApplyChrome(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as ViewModels.MainViewModel)?.FlushDirtyDocs();   // never lose the last keystrokes
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ReassertChrome();
        ResizeBorder.IsVisible = WindowState == WindowState.Normal;
        SyncMaximizeMargin();
        // Some events (display-mode flips, DWM resets) silently drop the corner preference WITHOUT a
        // WindowState change; re-assert it whenever the floating window regains focus — self-heals square corners.
        Activated += (_, _) => { if (WindowState == WindowState.Normal) WinChrome.RoundCorners(this, true); };
        Host.Opacity = 1;
        Host.RenderTransform = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;

        var state = change.GetNewValue<WindowState>();

        // A maximized / full-screen window must NOT expose the custom resize grips — they bypass the OS and
        // would let you drag-resize an edge a maximized window should treat as fixed. Only float them in Normal.
        ResizeBorder.IsVisible = state == WindowState.Normal;

        // Snapping / maximizing strips the sizing styles (WS_THICKFRAME), so the SECOND snap after a
        // snap-to-top silently fails unless we re-assert them on every state change.
        WinChrome.EnableSnap(this);

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
            // Snap-maximize can leave the acrylic backdrop showing DWM's snap-animation surface
            // (a stuck bright wash). Re-assert the backdrop once the maximize settles.
            DispatcherTimer.RunOnce(() => Services.ThemeManager.ApplyChrome(this), TimeSpan.FromMilliseconds(120));
            DispatcherTimer.RunOnce(() => Services.ThemeManager.ApplyChrome(this), TimeSpan.FromMilliseconds(450));
        }
    }

    /// <summary>Inset the content by however much the window overhangs the monitor work area when maximized, so
    /// a native snap-maximize (which overhangs ~7px per edge) lands pixel-identical to the clean button-maximize.
    /// Zero inset when floating or cleanly maximized. Computed geometry — Avalonia's OffScreenMargin stays 0 here.</summary>
    private void SyncMaximizeMargin()
    {
        if (WindowState != WindowState.Maximized ||
            (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } scr)
        {
            Host.Margin = default;
            return;
        }

        var work = scr.WorkingArea;                              // physical px
        double s = RenderScaling <= 0 ? 1 : RenderScaling;
        double physW = ClientSize.Width * s, physH = ClientSize.Height * s;
        double left   = Math.Max(0, work.X - Position.X);
        double top    = Math.Max(0, work.Y - Position.Y);
        double right  = Math.Max(0, (Position.X + physW) - (work.X + work.Width));
        double bottom = Math.Max(0, (Position.Y + physH) - (work.Y + work.Height));
        Host.Margin = new Thickness(left / s, top / s, right / s, bottom / s);
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
