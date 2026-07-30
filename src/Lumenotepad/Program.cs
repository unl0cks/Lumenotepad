using Avalonia;

namespace Lumenotepad;

sealed class Program
{
    [System.STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new Avalonia.Media.Fonts.EmbeddedFontCollection(
                new System.Uri(Services.AppFonts.CollectionUri),
                new System.Uri("avares://Lumenotepad/Assets/Fonts"))))

            .With(new Avalonia.Media.FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new Avalonia.Media.FontFallback
                    {
                        FontFamily = new Avalonia.Media.FontFamily(Services.AppFonts.CollectionUri + "#Lumen Icons"),
                    },
                },
            })
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface },
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
