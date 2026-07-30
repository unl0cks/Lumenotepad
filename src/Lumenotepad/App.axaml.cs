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

        if (!System.OperatingSystem.IsWindows())
            Resources["IconFont"] = new Avalonia.Media.FontFamily(
                $"{Services.AppFonts.CollectionUri}#Lumen Icons");

        ToggleFx.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            var vm = new MainViewModel();
            Services.AppFonts.RegisterInstalled();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
