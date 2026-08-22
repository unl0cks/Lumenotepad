using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumenotepad.Setup.Platform;
using Lumenotepad.Setup.Services;

namespace Lumenotepad.Setup.Views;

public partial class SetupWindow : Window
{
    private readonly StringBuilder _log = new();
    private readonly InstallOptions _options = new();
    private CancellationTokenSource? _cts;
    private Page _page = Page.Options;
    private bool _closing;
    private readonly bool _uninstalling = Program.Uninstalling;
    private string _installDir = "";

    private enum Page { Options, Working, Done }

    public SetupWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            try { DwmAcrylic.Apply(this, DwmAcrylic.Backdrop.Acrylic); } catch { }
            try { Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://Lumenotepad.Setup/Assets/lumenotepad-icon-256.png")))); }
            catch { }
            RootCard.RenderTransform = TransformOperations.Parse("scale(0.97)");
            Opacity = 1;
            Dispatcher.UIThread.Post(() => RootCard.RenderTransform = TransformOperations.Parse("scale(1)"));
        };

        TitleBar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        DetailsToggle.Click += (_, _) =>
        {
            LogPanel.IsVisible = !LogPanel.IsVisible;
            DetailsToggle.Content = LogPanel.IsVisible ? "Hide details" : "Show details";
            if (LogPanel.IsVisible) Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Loaded);
        };
        BrowseButton.Click += async (_, _) => await PickFolder();
        NextButton.Click += async (_, _) => await OnPrimary();
        BackButton.Click += (_, _) => { if (_cts is { } c && !c.IsCancellationRequested) c.Cancel(); else Close(); };
        DirBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ValidateDir(); };

        HookGrip(GripNW, WindowEdge.NorthWest); HookGrip(GripN, WindowEdge.North);
        HookGrip(GripNE, WindowEdge.NorthEast); HookGrip(GripW, WindowEdge.West);
        HookGrip(GripE, WindowEdge.East); HookGrip(GripSW, WindowEdge.SouthWest);
        HookGrip(GripS, WindowEdge.South); HookGrip(GripSE, WindowEdge.SouthEast);

        InitFields();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closing) { base.OnClosing(e); return; }
        _closing = true;
        e.Cancel = true;
        _cts?.Cancel();
        Opacity = 0;
        RootCard.RenderTransform = TransformOperations.Parse("scale(0.97)");
        DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(150));
    }

    private void HookGrip(Border grip, WindowEdge edge) =>
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            BeginResizeDrag(edge, e);
        };

    private void InitFields()
    {
        string? existing = InstallEngine.ExistingInstall();

        if (_uninstalling)
        {
            _installDir = Program.Args.InstallDir ?? existing ?? AppContext.BaseDirectory;
            TitleText.Text = "Lumenotepad Uninstall";
            Heading.Text = "Remove Lumenotepad";
            Subheading.Text = $"Lumenotepad will be removed from {_installDir}. Your notes can stay behind in " +
                              "case you reinstall later.";
            DirBox.IsEnabled = false;
            DirBox.Text = _installDir;
            DirNote.Text = "";
            StartMenuCheck.Content = "Keep my notes";
            StartMenuCheck.IsChecked = true;
            DesktopCheck.IsVisible = false;
            NotesNote.Text = "Your notes live in the userdata folder inside the install folder. Keeping them " +
                             "means a later install picks them up exactly where you left off.";
            NextButton.Content = "Remove";
            return;
        }

        _options.InstallDir = existing ?? InstallOptions.DefaultInstallDir;
        DirBox.Text = _options.InstallDir;
        StartMenuCheck.IsChecked = _options.StartMenuShortcut;
        DesktopCheck.IsChecked = _options.DesktopShortcut;

        bool downloads = InstallEngine.SourceOfFiles == InstallEngine.FileSource.Download;
        string version = SetupInfo.Version;
        string incoming = downloads ? "the latest version" : $"version {version}";

        if (existing is not null && InstallEngine.ExistingVersion() is { } have)
        {
            Heading.Text = "Update Lumenotepad";
            Subheading.Text = $"Version {have} is installed. This will replace it with {incoming}. Your notes " +
                              "are kept.";
            NextButton.Content = "Update";
        }
        else
        {
            Heading.Text = "Install Lumenotepad";
            Subheading.Text = downloads
                ? "A freeform note organizer. The latest version is downloaded when you install."
                : $"Version {version}. A freeform note organizer: drop containers anywhere on the page.";
        }

        NotesNote.Text = "Your notes are stored in a userdata folder beside the app, and updates from inside " +
                         "Lumenotepad never touch them.";
        ValidateDir();
    }

    private void ValidateDir()
    {
        if (_uninstalling) return;
        _options.InstallDir = DirBox.Text?.Trim() ?? "";
        string? why = _options.Validate();
        DirNote.Text = why ?? "Installs for you only, so it needs no administrator rights.";
        DirNote.Foreground = why is null ? Brush("TextMutedBrush") : Brush("CloseHoverBrush");
        NextButton.IsEnabled = why is null && InstallEngine.SourceOfFiles != InstallEngine.FileSource.None;
    }

    private IBrush Brush(string key) => this.TryFindResource(key, out object? v) && v is IBrush b ? b : Brushes.White;

    private async Task GoTo(Page page, bool forward = true)
    {
        PageHost.Opacity = 0;
        PageHost.RenderTransform = TransformOperations.Parse(forward ? "translateX(26px)" : "translateX(-26px)");
        await Task.Delay(150);

        _page = page;
        OptionsPage.IsVisible = page == Page.Options;
        ProgressPage.IsVisible = page == Page.Working;
        DonePage.IsVisible = page == Page.Done;

        PageHost.RenderTransform = TransformOperations.Parse(forward ? "translateX(-26px)" : "translateX(26px)");
        await Task.Delay(1);
        PageHost.Opacity = 1;
        PageHost.RenderTransform = TransformOperations.Parse("translateX(0px)");
    }

    private async Task OnPrimary()
    {
        switch (_page)
        {
            case Page.Options:
                await Work();
                break;
            case Page.Done:
                if (!_uninstalling && LaunchCheck.IsChecked == true)
                {
                    string exe = Path.Combine(_options.InstallDir, InstallEngine.ExeName);
                    try { if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _options.InstallDir }); }
                    catch { }
                }
                Close();
                break;
        }
    }

    private async Task Work()
    {
        _options.InstallDir = DirBox.Text?.Trim() ?? _options.InstallDir;
        _options.StartMenuShortcut = StartMenuCheck.IsChecked == true;
        _options.DesktopShortcut = DesktopCheck.IsChecked == true;

        await GoTo(Page.Working);
        NextButton.IsEnabled = false;
        BackButton.Content = "Cancel";
        ProgressHeading.Text = _uninstalling ? "Removing Lumenotepad" : "Installing Lumenotepad";
        _cts = new CancellationTokenSource();

        var progress = new Progress<InstallEngine.Progress>(p => Dispatcher.UIThread.Post(() =>
        {
            Bar.Value = p.Fraction;
            ProgressStage.Text = p.Stage;
            Status.Text = p.Stage;
        }));

        try
        {
            if (_uninstalling)
                await InstallEngine.UninstallAsync(_installDir, StartMenuCheck.IsChecked == true, progress, Log, _cts.Token);
            else
                await InstallEngine.InstallAsync(_options, SetupInfo.Version, progress, Log, _cts.Token);

            await GoTo(Page.Done);
            DoneHeading.Text = _uninstalling ? "Lumenotepad has been removed" : "Lumenotepad is installed";
            DoneNote.Text = _uninstalling
                ? "Thanks for trying it. You can reinstall any time by running the setup again."
                : $"Installed to {_options.InstallDir}. Updates come through the app itself from here on: " +
                  "Preferences, About, Check for updates.";
            LaunchCheck.IsVisible = !_uninstalling;
            LaunchCheck.IsChecked = !_uninstalling;
            NextButton.Content = "Finish";
            NextButton.IsEnabled = true;
            BackButton.IsVisible = false;
            Status.Text = "";
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Cancelled.";
            Log("Cancelled.");
            await GoTo(Page.Options, forward: false);
            RestoreOptionsButtons();
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message.Split('\n')[0];
            Log("ERROR: " + ex);
            LogPanel.IsVisible = true;
            DetailsToggle.Content = "Hide details";
            NextButton.Content = "Try again";
            NextButton.IsEnabled = true;
            BackButton.Content = "Close";
            _page = Page.Options;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RestoreOptionsButtons()
    {
        NextButton.Content = _uninstalling ? "Remove" : "Install";
        NextButton.IsEnabled = true;
        BackButton.Content = "Cancel";
    }

    private async Task PickFolder()
    {
        try
        {
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where to install Lumenotepad", AllowMultiple = false,
            });
            if (dirs.Count > 0 && dirs[0].TryGetLocalPath() is { } path)
            {
                if (!Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)).Equals("Lumenotepad", StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(path, "Lumenotepad");
                DirBox.Text = path;
            }
        }
        catch { }
    }

    private void Log(string line) => Dispatcher.UIThread.Post(() =>
    {
        _log.AppendLine(line);
        if (_log.Length > 120_000) _log.Remove(0, 60_000);
        LogText.Text = _log.ToString();
        LogScroll.ScrollToEnd();
    });
}
