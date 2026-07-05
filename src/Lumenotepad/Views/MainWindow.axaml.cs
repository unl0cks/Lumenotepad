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

        // Snapping / maximizing strips the sizing styles (WS_THICKFRAME), so the SECOND snap after a
        // snap-to-top silently fails unless we re-assert them on every state change.
        WinChrome.EnableSnap(this);

        if (change.GetNewValue<WindowState>() == WindowState.Normal)
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

    /// <summary>Re-apply all native chrome that a state change can silently reset: sizing styles (snap),
    /// rounded corners, and the acrylic backdrop.</summary>
    private void ReassertChrome()
    {
        WinChrome.EnableSnap(this);
        WinChrome.RoundCorners(this, true);
        DwmAcrylic.Apply(this, DwmAcrylic.Backdrop.Acrylic, dark: true);
    }
}
