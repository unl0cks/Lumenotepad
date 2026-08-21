using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public static class ConfirmDialog
{
    public static async Task<bool> Show(Window owner, string title, string message,
                                        string confirmText = "Delete", string cancelText = "Cancel",
                                        bool danger = true)
    {
        var t = Services.ThemeManager.Current;
        var win = new Window
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.Parse(t.WindowBackground)),
        };
        win.Opened += (_, _) => WinChrome.RoundCorners(win, true);
        Services.ThemeManager.ConfigureDialogChrome(win);

        var titleText = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse(t.TextPrimary)),
        };
        var msgText = new TextBlock
        {
            Text = message, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
            Foreground = new SolidColorBrush(Color.Parse(t.TextSecondary)), Margin = new Thickness(0, 8, 0, 18),
        };

        var lumenTheme = Avalonia.Application.Current?.FindResource("LumenButton") as Avalonia.Styling.ControlTheme;
        Button MakeButton(string text, string top, string bottom, string border, string fg) => new()
        {
            Content = text, FontSize = 12.5, Padding = new Thickness(16, 7),
            Theme = lumenTheme, Foreground = new SolidColorBrush(Color.Parse(fg)),
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(top), 0),
                    new GradientStop(Color.Parse(bottom), 1),
                },
            },
            BorderBrush = new SolidColorBrush(Color.Parse(border)),
        };
        var cancel = t.DarkChrome
            ? MakeButton(cancelText, "#33FFFFFF", "#1AFFFFFF", "#40FFFFFF", t.TextPrimary)
            : MakeButton(cancelText, "#12000000", "#0A000000", "#2E000000", t.TextPrimary);
        var confirm = danger
            ? MakeButton(confirmText, "#D64258", "#A62A3C", "#7E1F2D", "#FFFFFFFF")
            : MakeButton(confirmText, t.AccentGradTop, t.AccentGradBottom, t.AccentDeep, t.AccentText);

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
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse(t.DarkChrome ? "#26FFFFFF" : "#24000000")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = stack,
        };
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) win.BeginMoveDrag(e);
        };

        win.Content = card;
        win.Opened += (_, _) => Motion.ScaleIn(card, 0.96, 160);

        bool closing = false;
        void CloseWith(bool result)
        {
            if (closing) return;
            closing = true;
            Motion.CollapseOut(card, 130, () => win.Close(result));
        }
        cancel.Click += (_, _) => CloseWith(false);
        confirm.Click += (_, _) => CloseWith(true);
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseWith(false);
            else if (e.Key == Key.Enter) CloseWith(true);
        };

        var result = await win.ShowDialog<bool?>(owner);
        return result == true;
    }
}
