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

        // A chromeless WS_THICKFRAME window maximized via native Aero Snap overhangs each screen edge by ~8px,
        // clipping the title bar / caption buttons (the maximize BUTTON goes through Avalonia's own path, which
        // constrains it cleanly). OffScreenMargin is exactly that off-screen amount (0 when floating / cleanly
        // maximized), so insetting the content by it aligns both paths. Bounds settle a beat late — restagger.
        SyncMaximizeMargin();
        Dispatcher.UIThread.Post(SyncMaximizeMargin, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(SyncMaximizeMargin, TimeSpan.FromMilliseconds(120));

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
    }

    /// <summary>Inset content by the amount of the window that sits off-screen when maximized (0 when
    /// floating), so a snap-maximized chromeless window doesn't clip its title bar past the screen edge.</summary>
    private void SyncMaximizeMargin() => Host.Margin = OffScreenMargin;

    /// <summary>Re-apply all native chrome that a state change can silently reset: sizing styles (snap),
    /// rounded corners, and the acrylic backdrop.</summary>
    private void ReassertChrome()
    {
        WinChrome.EnableSnap(this);
        WinChrome.RoundCorners(this, true);
        DwmAcrylic.Apply(this, DwmAcrylic.Backdrop.Acrylic, dark: true);
    }
}
