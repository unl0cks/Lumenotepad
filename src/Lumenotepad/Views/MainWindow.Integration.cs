using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

/// <summary>System-tray integration: a generated tray icon with Open/Exit, plus close-to-tray and
/// minimize-to-tray hide/restore. (The global summon hotkey is added in a second step.)</summary>
public partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _exiting;      // set by the tray Exit path so OnClosing lets the real close through

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Create the tray icon on demand (whenever a tray feature is on).</summary>
    private void EnsureTray()
    {
        if (_tray is not null) return;
        var open = new NativeMenuItem("Open Lumenotepad");
        open.Click += (_, _) => RestoreFromTray();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        var menu = new NativeMenu();
        menu.Add(open);
        menu.Add(exit);
        _tray = new TrayIcon
        {
            Icon = BuildTrayIcon(), ToolTipText = "Lumenotepad", IsVisible = true, Menu = menu,
        };
        _tray.Clicked += (_, _) => RestoreFromTray();     // left-click opens
    }

    private void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    /// <summary>Show the tray icon while either tray feature is on; remove it otherwise.</summary>
    private void SyncTrayEnabled()
    {
        if (Vm is { } vm && (vm.CloseToTray || vm.MinimizeToTray)) EnsureTray();
        else DisposeTray();
    }

    /// <summary>Hide the window into the tray (making sure the icon exists first). The close path
    /// animates the shrink; the minimize path is already visually gone, so it just hides.</summary>
    private void HideToTray(bool animate)
    {
        EnsureTray();
        if (animate) Motion.CollapseOut(Host, 150, Hide);
        else Hide();
    }

    /// <summary>Bring the window back from the tray: show, un-minimize, focus, and re-scale it in.</summary>
    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Motion.ScaleIn(Host, 0.97, 220);
        ReassertChrome();
    }

    /// <summary>Real quit (tray Exit): flush, drop the icon, and shut the app down. `_exiting` makes
    /// OnClosing skip both the close-to-tray hide and the close animation.</summary>
    private void ExitApp()
    {
        _exiting = true;
        Vm?.FlushDirtyDocs();
        DisposeTray();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        else Close();
    }

    /// <summary>A simple placeholder tray icon (accent rounded square + "L") rendered to a bitmap —
    /// stands in until the real app icon ships. Uses the proven RenderTargetBitmap.Render path.</summary>
    private static WindowIcon BuildTrayIcon()
    {
        var accent = Color.Parse(Services.ThemeManager.Current.Accent);
        var visual = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(accent),
            Child = new TextBlock
            {
                Text = "L", FontSize = 40, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        visual.Measure(new Size(64, 64));
        visual.Arrange(new Rect(0, 0, 64, 64));
        var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        rtb.Render(visual);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    // Temporary stubs — REPLACED with the real hotkey implementation in Task 3. Present so this task
    // builds on its own (OnOpened/OnClosed/OnThemePropertyChanged reference them).
    private void SyncHotkey() { }
    private void UnregisterSummon() { }
}
