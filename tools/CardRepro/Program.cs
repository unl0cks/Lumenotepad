using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;

namespace CardRepro;

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        // Redesigned card: ONE continuous overlay (top gloss → fade → dark scrim), labels sit
        // directly on the scrim — no separate strip, so the gradient never hard-cuts mid-card.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Margin = new Thickness(26) };
        row.Children.Add(FinalCard("#4DA6FF", "KUK", "2 sections · 7 pages"));
        row.Children.Add(FinalCard("#F5E3A3", "Pastel worst case", "1 section · 1 page"));
        row.Children.Add(FinalCard("#1F4A8F", "Navy", "3 sections · 9 pages"));
        row.Children.Add(FinalCard("#3E9C6B", "FITTA", "1 section · 1 page"));

        var window = new Window
        {
            Width = 1180, Height = 200,
            Background = new SolidColorBrush(Color.Parse("#101218")),
            Content = row,
        };
        window.Show();

        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            window.SetRenderScaling(scale);
            var frame = window.CaptureRenderedFrame();
            var path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", $"cards-{scale:0.00}.png"));
            frame!.Save(path);
            Console.WriteLine("saved: " + path);
        }
    }

    private static LinearGradientBrush VGrad(params (string Hex, double Off)[] stops)
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        };
        foreach (var (hex, off) in stops) b.GradientStops.Add(new GradientStop(Color.Parse(hex), off));
        return b;
    }

    private static Color Shade(Color c, double f)
    {
        byte Mix(byte ch) => (byte)Math.Clamp(f >= 0 ? ch + (255 - ch) * f : ch * (1 + f), 0, 255);
        return new Color(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static Control FinalCard(string hex, string name, string stats)
    {
        var c = Color.Parse(hex);
        var card = new Border
        {
            Width = 196, Height = 132, CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Shade(c, -0.38)),
            Background = new SolidColorBrush(c),
            BoxShadow = BoxShadows.Parse("0 3 12 0 #59000000"),
        };

        var labels = new StackPanel { Spacing = 2, Margin = new Thickness(14, 0, 14, 10), VerticalAlignment = VerticalAlignment.Bottom };
        labels.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
        labels.Children.Add(new TextBlock { Text = stats, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#CCFFFFFF")) });

        var grid = new Panel();
        grid.Children.Add(new TextBlock
        {
            Text = name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant(),
            FontSize = 30, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#59FFFFFF")),
            Margin = new Thickness(16, 12, 0, 0),
        });
        grid.Children.Add(labels);

        card.Child = new Border
        {
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#38FFFFFF")),
            Background = VGrad(("#2BFFFFFF", 0), ("#00FFFFFF", 0.40), ("#40000000", 0.68), ("#73000000", 1)),
            Child = grid,
        };
        return card;
    }
}
