using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.VisualTree;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace CardRepro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppBuilder.Configure<Lumenotepad.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var target = new Border { Width = 100, Height = 100, Background = Brushes.Red, Opacity = 1 };
        var window = new Window { Width = 220, Height = 220, Content = target };
        window.Show();
        Pump();

        Console.WriteLine("=== 1) Transition on Opacity (declarative) ===");
        target.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
        };
        Pump();
        target.Opacity = 0.0;
        Tick(9);
        Console.WriteLine($"   Opacity after ~150ms (expect ~0.5 if animating): {target.Opacity:0.###}");
        target.Opacity = 1; target.Transitions = null; Pump();

        Console.WriteLine("=== 2) Animation.RunAsync on Opacity (keyframe) ===");
        var opAnim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300), Easing = new LinearEasing(), FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
            },
        };
        _ = opAnim.RunAsync(target);
        Tick(9);
        Console.WriteLine($"   Opacity after ~150ms (expect ~0.5 if animating): {target.Opacity:0.###}");
        target.Opacity = 1; Pump();

        Console.WriteLine("=== 3) Animation.RunAsync on RenderTransform (keyframe TransformOperations) ===");
        target.RenderTransformOrigin = RelativePoint.Center;
        var trAnim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300), Easing = new LinearEasing(), FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(1)")) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(2)")) } },
            },
        };
        try { _ = trAnim.RunAsync(target); Tick(9); Console.WriteLine($"   scale after ~150ms: {target.RenderTransform?.Value.M11:0.###}"); }
        catch (Exception ex) { Console.WriteLine("   THROWS: " + ex.Message.Split('.')[0]); }

        Console.WriteLine("=== 4) TransitioningContentControl cross-fade ===");
        var tcc = new TransitioningContentControl
        {
            Width = 120, Height = 120,
            PageTransition = new CrossFade(TimeSpan.FromMilliseconds(300)),
            Content = new Border { Background = Brushes.Blue },
        };
        window.Content = tcc; Pump();
        tcc.Content = new Border { Background = Brushes.Green };
        Tick(9);

        var opacities = tcc.GetVisualDescendants().OfType<Control>()
            .Select(c => c.Opacity).Where(o => o < 0.999).ToList();
        Console.WriteLine($"   mid-fade child opacities <1 (expect some ~0.5 if animating): [{string.Join(", ", opacities.Select(o => o.ToString("0.##")))}]");

        Console.WriteLine("done");
    }

    private static void Tick(int frames)
    {
        try { AvaloniaHeadlessPlatform.ForceRenderTimerTick(frames); }
        catch (Exception ex) { Console.WriteLine("   [ForceRenderTimerTick failed: " + ex.Message + "]"); }
        Pump();
    }

    private static void Pump() { for (int i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
}
