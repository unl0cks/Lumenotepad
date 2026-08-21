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

            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 44;

        }
        DataContextChanged += (_, _) => HookThemeVm();
    }

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

        string? seed = vm.AccentFollowsNotebook && !vm.IsHomeVisible && vm.SelectedNotebook is { } nb
            ? Services.ThemePalettes.NormalizeHex(nb.Color)
            : Services.ThemePalettes.NormalizeHex(vm.CustomAccent);
        if (seed is { } accent) tokens = Services.ThemePalettes.WithAccent(tokens, accent);
        Services.ThemeManager.Apply(app, tokens);
        Services.ThemeManager.ApplyChrome(this);

        if (_shellReady && !OperatingSystem.IsWindows()) RearmMacGlass();
    }

    private bool _shellReady;

    private bool _closingAnimated;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as ViewModels.MainViewModel)?.FlushDirtyDocs();
        if (_exiting) return;
        if (Vm is { CloseToTray: true })
        {
            e.Cancel = true;
            HideToTray(animate: true);
            return;
        }
        if (_closingAnimated) return;
        e.Cancel = true;
        _closingAnimated = true;
        Motion.CollapseOut(Host, 150, Close);
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
        Services.StartupLog.Mark("window opened");
        _shellReady = true;
        ReassertChrome();
        Services.StartupLog.Mark("chrome");
        ResizeBorder.IsVisible = UseResizeOverlay && WindowState == WindowState.Normal;
        SyncMaximizeMargin();

        Activated += (_, _) =>
        {
            if (WindowState == WindowState.Normal) WinChrome.RoundCorners(this, true);
            else if (WindowState == WindowState.Maximized) Services.ThemeManager.RefreshBackdrop(this);
        };
        Motion.ScaleIn(Host, 0.97, 220);
        SyncTrayEnabled();
        SyncHotkey();

        if (!OperatingSystem.IsWindows())
        {

            RearmMacGlass();
            Services.StartupLog.Mark("mac glass");
            MacVibrancy.DisableNativeFullScreen(this);
            Services.StartupLog.Mark("mac fullscreen off");
            DispatcherTimer.RunOnce(RearmMacGlass, TimeSpan.FromMilliseconds(200));
            DispatcherTimer.RunOnce(WriteMacChromeDiagnostics, TimeSpan.FromMilliseconds(1200));
        }
        Services.StartupLog.Mark("ready");
    }

    internal void RearmMacGlass()
    {
        Services.ThemeManager.ApplyMacGlass(this, Services.ThemeManager.Current.GlassWindow
            ? Services.ThemeManager.MacGlass.Frost
            : Services.ThemeManager.MacGlass.Opaque);
        MacVibrancy.KeepFrostActive(this);

        MacVibrancy.DisableNativeFullScreen(this);
    }

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

                $"frostLayers             = {MacVibrancy.FrostLayerCount(this)}\n" +
                $"glassMaterial           = {MacVibrancy.Material}\n");
        }
        catch {  }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (Host is null || ResizeBorder is null) return;

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
            return;
        }

        ResizeBorder.IsVisible = UseResizeOverlay && state == WindowState.Normal;

        WinChrome.EnableSnap(this);

        if (_shellReady && !OperatingSystem.IsWindows())
        {

            RearmMacGlass();
            RelayoutMac();
            foreach (var ms in new[] { 150, 500, 950, 1500 })
                DispatcherTimer.RunOnce(() => { RearmMacGlass(); RelayoutMac(); },
                    TimeSpan.FromMilliseconds(ms));
        }

        SyncMaximizeMargin();
        Dispatcher.UIThread.Post(SyncMaximizeMargin, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(SyncMaximizeMargin, TimeSpan.FromMilliseconds(120));
        DispatcherTimer.RunOnce(SyncMaximizeMargin, TimeSpan.FromMilliseconds(400));

        if (state == WindowState.Normal)
        {

            ReassertChrome();
            Dispatcher.UIThread.Post(ReassertChrome, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(ReassertChrome, TimeSpan.FromMilliseconds(150));
            DispatcherTimer.RunOnce(ReassertChrome, TimeSpan.FromMilliseconds(450));
        }
        else if (state == WindowState.Maximized)
        {

            foreach (var ms in new[] { 120, 450, 900, 1400 })
                DispatcherTimer.RunOnce(() => Services.ThemeManager.RefreshBackdrop(this), TimeSpan.FromMilliseconds(ms));
        }
    }

    private void SyncMaximizeMargin()
    {

        if (!OperatingSystem.IsWindows()) { Host.Margin = default; return; }
        if (WindowState != WindowState.Maximized ||
            (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } scr)
        {
            Host.Margin = default;
            return;
        }

        Host.Margin = WindowGeometry.MaximizeInset(scr.WorkingArea, Position, ClientSize, RenderScaling);
    }

    private void RelayoutMac()
    {
        Host.Margin = default;
        Host.InvalidateMeasure();
        Host.InvalidateArrange();
    }

    private void ReassertChrome()
    {
        WinChrome.EnableSnap(this);
        WinChrome.RoundCorners(this, true);
        Services.ThemeManager.ApplyChrome(this);
    }
}
