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
        // The REAL app + MainView rendered headlessly — here: sample theme-matrix cells to PNG.
        AppBuilder.Configure<Lumenotepad.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var dir = Path.Combine(Path.GetTempPath(), "lnp-repro-" + Guid.NewGuid().ToString("N"));
        var vm = new MainViewModel(new WorkspaceStore(dir));
        vm.ToolbarScope = "Window";
        vm.AddNotebookCommand.Execute(null);
        vm.SetNotebookColor(vm.Notebooks[0], "#4DA6FF");
        vm.SetNotebookColor(vm.Notebooks[1], "#FB6F92");

        var window = new Window
        {
            Width = 1180, Height = 720,
            Background = new SolidColorBrush(Color.Parse("#4A505E")),   // stand-in for the acrylic backdrop
            Content = new MainView { DataContext = vm },
        };
        window.Show();
        window.SetRenderScaling(1.5);

        foreach (var (theme, full, paperLight, name) in new[]
        {
            ("Lumen", false, false, "lumen-off"),
            ("Dark", false, false, "dark-off"),
            ("Lumen", false, true, "lumen-lightpaper"),
            ("Light", true, false, "light-full"),
            ("Light", false, false, "light-off"),
            ("Pink", true, false, "pink-full"),
            ("Light blue", true, false, "blue-full"),
        })
        {
            vm.Theme = theme;
            vm.FullTheme = full;
            vm.PaperLight = paperLight;
            ThemeManager.Apply(Application.Current!, ThemePalettes.Resolve(theme, full, paperLight));

            // one shot on the editor, one on the homepage. Headless synchronous bindings let the
            // sections ListBox null-push the cascaded selection — re-assert it by hand here.
            vm.OpenNotebookCommand.Execute(vm.Notebooks[0]);
            vm.SelectedSection ??= vm.Notebooks[0].Sections.FirstOrDefault();
            vm.SelectedPage ??= vm.SelectedSection?.Pages.FirstOrDefault();
            var doc = vm.DocumentFor(vm.SelectedPage!);
            if (doc.Boxes.Count == 0)
                doc.AddBox(30, 20, 420).Doc.InsertText(new Lumenotepad.Editor.DocPos(0, 0), "Theme check: the quick brown fox.");
            Save(window, $"theme-{name}-editor");

            vm.GoHomeCommand.Execute(null);
            Save(window, $"theme-{name}-home");
        }
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", name + ".png"));
        frame!.Save(path);
        Console.WriteLine("saved: " + path);
    }
}
