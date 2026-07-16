using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumenotepad.ViewModels;
using Lumenotepad.Views;

namespace Lumenotepad;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ToggleFx.Install();   // Motion-driven ToggleSwitch knob (one global hook pair)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The MainViewModel ctor points FontInstaller.FontsDir at the real userdata folder;
            // load whatever fonts the user has already downloaded so they appear in every menu.
            var vm = new MainViewModel();
            Services.AppFonts.RegisterInstalled();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
