using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

/// <summary>The update window — Lumenotepad's equivalent of Lumen Launcher, and reached the same way
/// (About → "Check for updates"). It finds the right build for whichever OS is running, downloads it with
/// progress, verifies it, and restarts into it.
///
/// Deliberately a window inside the app rather than a separate launcher executable, which is where this
/// parts company with Lumen. Lumen needs a separate process because its Windows install goes into Program
/// Files with an uninstaller and file associations, so the installer must run elevated and outside the app.
/// Lumenotepad needs neither: on Windows it is portable (replace the files, keep `userdata`), and on macOS
/// it is a bundle swap. Shipping a second macOS .app would also mean a SECOND unsigned app for the user to
/// approve through System Settings — the exact friction the updater exists to remove.</summary>
public sealed class UpdaterWindow : Window
{
    private readonly TextBlock _headline;
    private readonly TextBlock _detail;
    private readonly ProgressBar _bar;
    private readonly Button _action;
    private readonly Button _notes;
    private readonly CancellationTokenSource _cts = new();

    private (string Version, UpdateBuild Build)? _pending;

    /// <summary>Local alias so the record type does not leak the service namespace into the UI.</summary>
    private sealed record UpdateBuild(Services.UpdateService.Build Inner);

    public UpdaterWindow()
    {
        Title = "Lumenotepad Updates";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.WindowBackground));

        _headline = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TextPrimaryBrush"),
        };
        _detail = new TextBlock
        {
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Foreground = Res("TextSecondaryBrush"),
        };
        _bar = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Height = 4, IsVisible = false, Margin = new Thickness(0, 14, 0, 0),
        };

        _action = new Button { Content = "Check now", FontSize = 12.5, Padding = new Thickness(16, 8) };
        _notes = new Button
        {
            Content = "Release notes", FontSize = 12.5, Padding = new Thickness(14, 8), IsVisible = false,
        };
        var close = new Button { Content = "Close", FontSize = 12.5, Padding = new Thickness(14, 8) };
        close.Click += (_, _) => Close();

        _action.Click += async (_, _) => await OnAction();
        _notes.Click += (_, _) => OpenUrl(Services.UpdateService.ReleasesPage);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        buttons.Children.Add(_notes);
        buttons.Children.Add(close);
        buttons.Children.Add(_action);

        var body = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        body.Children.Add(_headline);
        body.Children.Add(_detail);
        body.Children.Add(_bar);
        body.Children.Add(buttons);
        Content = body;

        // Same chrome recipe as every other secondary window: chromeless + DWM rounding on Windows, the
        // native frame (rounded, frosted, no traffic lights) on macOS.
        Services.ThemeManager.ConfigureDialogChrome(this);
        Opened += (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            Services.ThemeManager.ApplyChildChrome(this);
        };

        // Dragging anywhere moves it — it has no title bar of its own.
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _cts.Cancel();

        ShowIdle();
        // Check immediately: nobody opens this window to then press "Check".
        _ = Check();
    }

    private IBrush? Res(string key) =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as IBrush : null;

    private void ShowIdle()
    {
        _headline.Text = $"Lumenotepad {Services.AppInfo.Version}";
        _detail.Text = "Checking for a newer version…";
        _action.IsEnabled = true;
    }

    /// <summary>Why this copy cannot install an update itself. Checking always works — only replacing the
    /// files needs a real, writable install — so this explains the gap rather than disabling the button.</summary>
    private static string CannotInstallReason() => OperatingSystem.IsMacOS()
        ? "This copy is running from a development tree, or from somewhere it cannot write to, so it "
          + "cannot replace itself. Move Lumenotepad.app into Applications, or download the build by hand."
        : "This copy is running from a development tree, or from a folder it cannot write to, so it "
          + "cannot replace itself. Move the Lumenotepad folder somewhere writable, or download by hand.";

    private async Task OnAction()
    {
        if (_pending is { } p) await Install(p.Version, p.Build.Inner);
        else await Check();
    }

    private async Task Check()
    {
        _action.IsEnabled = false;
        _headline.Text = "Checking for updates…";
        _detail.Text = $"You have {Services.AppInfo.Version}.";
        var found = await Services.UpdateService.CheckAsync(_cts.Token);
        if (_cts.IsCancellationRequested) return;
        _action.IsEnabled = true;

        if (found is not { } f)
        {
            _pending = null;
            _headline.Text = "You're up to date";
            _detail.Text = $"Lumenotepad {Services.AppInfo.Version} is the latest version.";
            _action.Content = "Check again";
            return;
        }

        _pending = (f.Version, new UpdateBuild(f.Build));
        _headline.Text = $"Version {f.Version} is available";
        _detail.Text = (f.Notes is { Length: > 0 } ? f.Notes + "\n\n" : "")
            + $"Download is {f.Build.Size / 1024.0 / 1024.0:F0} MB. Lumenotepad will restart to finish. "
            + "Your notebooks are not touched.";
        _action.Content = "Download and install";
        _notes.IsVisible = true;
    }

    private async Task Install(string version, Services.UpdateService.Build build)
    {
        _action.IsEnabled = false;
        _notes.IsVisible = false;
        _bar.IsVisible = true;
        _headline.Text = $"Installing {version}";
        var progress = new Progress<double>(v =>
        {
            _bar.Value = v;
            _detail.Text = $"Downloading… {(int)(v * 100)}%";
        });

        bool ok = await Services.UpdateService.DownloadAndApplyAsync(build, version, progress, _cts.Token);
        if (_cts.IsCancellationRequested) return;

        if (!ok)
        {
            _bar.IsVisible = false;
            _headline.Text = "That didn't work";
            _detail.Text = "The download did not finish or did not verify, so nothing was changed. "
                           + "Your current version is untouched — try again, or grab the build by hand from "
                           + "the release page.";
            _action.Content = "Try again";
            _action.IsEnabled = true;
            _notes.IsVisible = true;
            return;
        }

        // The swap script is already waiting on this process to exit.
        _detail.Text = "Restarting…";
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch { /* no browser, or a policy blocks it — the About page shows the URL as text anyway */ }
    }
}
