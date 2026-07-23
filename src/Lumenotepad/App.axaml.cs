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
        // Off Windows the Segoe icon fonts don't exist, so every toolbar/menu glyph would render as
        // tofu. Swap the IconFont resource for the bundled LumenIcons face (same PUA codepoints,
        // MIT Fluent shapes) BEFORE any window is built — app-level Resources win over the merged
        // Theme.axaml entry, so Windows keeps Segoe untouched and macOS gets real icons.
        if (!System.OperatingSystem.IsWindows())
            Resources["IconFont"] = new Avalonia.Media.FontFamily(
                $"{Services.AppFonts.CollectionUri}#Lumen Icons");

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
