using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumenotepad.Editor;
using Lumenotepad.ViewModels;
using Lumenotepad.Views;

internal static class Program
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [STAThread]
    private static void Main(string[] args)
    {
        AppBuilder.Configure<Lumenotepad.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var ud = Path.Combine(AppContext.BaseDirectory, "userdata");
        if (Directory.Exists(ud)) Directory.Delete(ud, true);

        // ---------- session 1 ----------
        var (w, vm) = Boot();
        var nb = vm.SelectedNotebook ?? vm.Notebooks.First();
        vm.SelectedNotebook = nb; Pump(10);
        var sec = vm.SelectedSection!;
        var rel = vm.ImportPageAsset(args[0])!;
        var pg = new Lumenotepad.Models.Page { Title = "pdf", PdfPath = rel };
        sec.Pages.Add(pg); vm.SelectedPage = pg; vm.Save();
        var v = Viewer(w);
        WaitLoaded(v);
        var side = PdfAnnotationDoc.SidecarPath(Path.Combine(vm.SelectedNotebookDir!, rel));
        Console.WriteLine($"s1 rel={rel} loaded={Get<bool>(v, "_loaded")} sidecar={Side(side)}");
        v.GetType().GetMethod("AddTextHighlight", F)!.Invoke(v, new object[] { 0, 0, 40 });
        Pump(5);
        Console.WriteLine($"s1 after highlight items={Get<PdfAnnotationDoc>(v, "_annos").Items.Count} sidecar={Side(side)}");
        CloseApp(w);
        Console.WriteLine($"s1 after close sidecar={Side(side)}  path={side}");

        // ---------- session 2 (restart) ----------
        PdfAnnotationHub.Reset();
        var (w2, vm2) = Boot();
        Console.WriteLine($"s2 boot page={vm2.SelectedPage?.Title} pdf={vm2.SelectedPage?.PdfPath} sidecar={Side(side)}");
        var nb2 = vm2.Notebooks.First(n => n.Id == nb.Id);
        vm2.SelectedNotebook = nb2; Pump(5);
        var pg2 = nb2.Sections.SelectMany(s => s.Pages).First(p => p.Id == pg.Id);
        vm2.SelectedSection = nb2.Sections.First(s => s.Pages.Contains(pg2)); Pump(5);
        vm2.SelectedPage = pg2;
        var v2 = Viewer(w2);
        WaitLoaded(v2);
        Console.WriteLine($"s2 opened items={Get<PdfAnnotationDoc>(v2, "_annos").Items.Count} path={Get<string>(v2, "_pdfPath")} sidecar={Side(side)}");
        CloseApp(w2);
        Console.WriteLine($"s2 after close sidecar={Side(side)}");
    }

    static (Window, MainViewModel) Boot()
    {
        var vm = new MainViewModel();
        var w = new MainWindow { DataContext = vm };
        w.Show(); Pump(60);
        return (w, vm);
    }

    static void CloseApp(Window w)
    {
        w.GetType().GetField("_exiting", F)?.SetValue(w, true);
        w.Close(); Pump(60);
    }

    static PdfViewer Viewer(Window w) => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.Name == "PagePdfViewer");

    static void WaitLoaded(PdfViewer v)
    {
        for (int i = 0; i < 400 && !(Get<bool>(v, "_loaded") && Get<bool>(v, "_textReady")); i++) Pump(1, 25);
    }

    static T Get<T>(object o, string f) => (T)o.GetType().GetField(f, F)!.GetValue(o)!;
    static string Side(string p) => File.Exists(p) ? $"{PdfAnnotationDoc.FromJson(File.ReadAllText(p)).Items.Count} items" : "MISSING";

    static void Pump(int n, int sleepMs = 5)
    {
        for (int i = 0; i < n; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(sleepMs);
        }
    }
}
