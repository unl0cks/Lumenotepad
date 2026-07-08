using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;
using Lumenotepad.Views;

namespace CardRepro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // The REAL app (App.axaml: FluentTheme + Theme.axaml + Dark variant), the REAL MainView —
        // rendered headlessly so homepage pixels can be inspected without guessing.
        AppBuilder.Configure<Lumenotepad.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var dir = Path.Combine(Path.GetTempPath(), "lnp-repro-" + Guid.NewGuid().ToString("N"));
        var vm = new MainViewModel(new WorkspaceStore(dir));
        vm.AddNotebookCommand.Execute(null);
        vm.AddNotebookCommand.Execute(null);
        vm.SetNotebookColor(vm.Notebooks[0], "#4DA6FF");
        vm.SetNotebookColor(vm.Notebooks[1], "#F5E3A3");
        vm.SetNotebookColor(vm.Notebooks[2], "#1F4A8F");
        vm.GoHomeCommand.Execute(null);

        // Mid-gray stand-in for the acrylic backdrop, so shadows are visible in the capture.
        var window = new Window
        {
            Width = 1180, Height = 720,
            Background = new SolidColorBrush(Color.Parse("#4A505E")),
            Content = new MainView { DataContext = vm },
        };
        window.Show();

        foreach (var scale in new[] { 1.0, 1.5 })
        {
            window.SetRenderScaling(scale);
            var frame = window.CaptureRenderedFrame();
            var path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", $"home-{scale:0.00}.png"));
            frame!.Save(path);
            Console.WriteLine("saved: " + path);
        }
    }
}
