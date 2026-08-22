using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lumenotepad.Setup.Views;

namespace Lumenotepad.Setup;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try { desktop.MainWindow = new SetupWindow(); Program.Note("window constructed"); }
            catch (Exception ex)
            {
                Program.Crash("window", ex);
                desktop.MainWindow = new Window
                {
                    Title = "Lumenotepad Setup failed to start",
                    Width = 720, Height = 420,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)),
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(22),
                        Content = new SelectableTextBlock
                        {
                            Text = ex.ToString(), Foreground = Brushes.White, FontSize = 11.5,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
