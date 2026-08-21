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

        Services.StartupLog.Mark("framework init");
        if (!System.OperatingSystem.IsWindows())
            Resources["IconFont"] = new Avalonia.Media.FontFamily(
                $"{Services.AppFonts.CollectionUri}#Lumen Icons");

        Services.StartupLog.Mark("icon font");
        ToggleFx.Install();
        Services.StartupLog.Mark("toggle fx");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            var vm = new MainViewModel();
            Services.StartupLog.Mark("viewmodel");
            Services.AppFonts.RegisterInstalled();
            Services.StartupLog.Mark("installed fonts");
            try
            {
                var window = new MainWindow();
                Services.StartupLog.Mark("window built");
                window.DataContext = vm;
                Services.StartupLog.Mark("window themed");
                desktop.MainWindow = window;
                Services.StartupLog.Mark("main window created");
            }
            catch (System.Exception ex)
            {
                Services.StartupLog.Crash("main window", ex);
                throw;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
