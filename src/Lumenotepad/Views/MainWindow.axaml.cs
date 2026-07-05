using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WinChrome.EnableSnap(this);
            WinChrome.RoundCorners(this, true);
            DwmAcrylic.Apply(this, DwmAcrylic.Backdrop.Acrylic, dark: true);
            Host.Opacity = 1;
            Host.RenderTransform = null;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
