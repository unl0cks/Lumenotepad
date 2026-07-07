using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

/// <summary>Small Lumen-styled confirm prompt. A solid, opaque, DWM-rounded window (NOT a transparent
/// one — a chromeless transparent window paints its unpainted area black, which showed as a dark square
/// around the card). Cancel + a red destructive button; Enter confirms, Escape cancels, drag anywhere.</summary>
public static class ConfirmDialog
{
    public static async Task<bool> Show(Window owner, string title, string message,
                                        string confirmText = "Delete", string cancelText = "Cancel")
    {
        var win = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.Parse("#1B1D27")),   // opaque: no transparent ring to go black
        };
        win.Opened += (_, _) => WinChrome.RoundCorners(win, true);

        var titleText = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
        };
        var msgText = new TextBlock
        {
            Text = message, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
            Foreground = new SolidColorBrush(Color.Parse("#B3FFFFFF")), Margin = new Thickness(0, 8, 0, 18),
        };

        static Button MakeButton(string text, string bg) => new()
        {
            Content = text, FontSize = 12.5, Padding = new Thickness(16, 7),
            Background = new SolidColorBrush(Color.Parse(bg)), Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8), Cursor = new Cursor(StandardCursorType.Hand),
        };
        var cancel = MakeButton(cancelText, "#22FFFFFF");
        var confirm = MakeButton(confirmText, "#CCC42B3A");
        cancel.Click += (_, _) => win.Close(false);
        confirm.Click += (_, _) => win.Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        stack.Children.Add(titleText);
        stack.Children.Add(msgText);
        stack.Children.Add(buttons);

        // A hairline top border reads as a lifted edge; the window itself carries the fill + rounded corners.
        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = stack,
        };
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) win.BeginMoveDrag(e);
        };

        win.Content = card;
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close(false);
            else if (e.Key == Key.Enter) win.Close(true);
        };

        var result = await win.ShowDialog<bool?>(owner);
        return result == true;
    }
}
