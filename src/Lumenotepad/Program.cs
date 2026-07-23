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
            // Per-codepoint safety net: when the resolved face lacks a glyph (the Segoe icon fonts
            // don't exist off Windows), the shaper consults this before giving up — so every PUA
            // icon codepoint finds the bundled LumenIcons shapes even via hard-coded Segoe names.
            // On Windows Segoe covers all icon codepoints, so this is never consulted for icons.
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
