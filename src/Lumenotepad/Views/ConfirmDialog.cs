using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lumenotepad.Views;

/// <summary>Small Lumen-styled confirm prompt: borderless rounded dark card, Cancel + a red
/// destructive button. Enter confirms, Escape cancels, the card itself can be dragged.</summary>
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
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
        };

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

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F5171922")),
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #66000000"),
            Margin = new Thickness(14),           // room for the shadow inside the transparent window
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
