using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;
using Lumenotepad.Views;

namespace CardRepro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppBuilder.Configure<Lumenotepad.App>()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new Avalonia.Media.Fonts.EmbeddedFontCollection(
                new System.Uri(AppFonts.CollectionUri),
                new System.Uri("avares://Lumenotepad/Assets/Fonts"))))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var dir = Path.Combine(Path.GetTempPath(), "lnp-repro-" + Guid.NewGuid().ToString("N"));
        var vm = new MainViewModel(new WorkspaceStore(dir));
        vm.AddNotebookCommand.Execute(null);
        vm.GoHomeCommand.Execute(null);

        var window = new Window { Width = 1180, Height = 720, Content = new MainView { DataContext = vm } };
        window.Show();
        window.SetRenderScaling(1.0);
        Dispatch();

        // Hover the first card → confirm scale applies now.
        var card = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("nbcard"));
        if (card is not null)
        {
            card.Transitions = null;   // headless clock doesn't tick transitions — read the target directly
            window.MouseMove(new Point(-5, -5));
            Dispatch();
            var mid = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window) ?? default;
            window.MouseMove(mid);
            Dispatch();
            Console.WriteLine($"[card] pointerover={card.IsPointerOver} transform={card.RenderTransform?.Value}");
        }
        Save(window, "home-hover");

        // Editor: the selected section/page/notebook should look selected by default.
        window.MouseMove(new Point(-5, -5));
        vm.OpenNotebookCommand.Execute(vm.Notebooks[0]);
        vm.SelectedSection ??= vm.Notebooks[0].Sections[0];
        vm.SelectedPage ??= vm.SelectedSection?.Pages[0];
        Dispatch();
        Save(window, "editor-selection");

        // Same under the Light theme (glow color follows accent).
        vm.Theme = "Light";
        ThemeManager.Apply(Application.Current!, ThemePalettes.Resolve("Light", false, false));
        Dispatch();
        Save(window, "editor-selection-light");
        Console.WriteLine("done");
    }

    private static void Dispatch()
    {
        for (int i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", name + ".png"));
        frame!.Save(path);
        Console.WriteLine("saved: " + path);
    }
}
