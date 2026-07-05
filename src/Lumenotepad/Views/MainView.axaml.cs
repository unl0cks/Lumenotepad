using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lumenotepad.Platform;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                TopLevel.GetTopLevel(this) is Window w && !WinChrome.BeginNativeMoveDrag(w))
                w.BeginMoveDrag(e);
        };

        MinBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = WindowState.Minimized; };
        MaxBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
        CloseBtn.Click += (_, _) => Window?.Close();

        // Rename fields commit (persist) when they lose focus or on Enter.
        foreach (var box in new[] { NotebookName, SectionName, PageTitle })
        {
            box.LostFocus += SaveEdit;
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { SaveEdit(s, e); ((Control?)s)?.Focus(); } };
        }
    }

    private void SaveEdit(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.Save();

    private Window? Window => TopLevel.GetTopLevel(this) as Window;
}
