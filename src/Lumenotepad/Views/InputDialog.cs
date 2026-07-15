using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

/// <summary>A small Lumen-styled text-input prompt (same opaque, DWM-rounded shell as ConfirmDialog):
/// one or more labelled fields, Enter confirms, Escape cancels. Returns the entered values in order,
/// or null when cancelled. Used for the hyperlink prompt (M10) and other quick text asks.</summary>
public static class InputDialog
{
    public static async Task<string[]?> Show(Window owner, string title,
                                             IReadOnlyList<(string Label, string Placeholder, string Initial)> fields,
                                             string confirmText = "OK")
    {
        var win = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.Parse("#1B1D27")),
        };
        win.Opened += (_, _) => WinChrome.RoundCorners(win, true);

        var fieldTheme = Application.Current?.FindResource("RoundedFieldTextBox") as ControlTheme;
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 16), Spacing = 8, Width = 320 };
        stack.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var boxes = new List<TextBox>();
        foreach (var (label, placeholder, initial) in fields)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#B3FFFFFF")),
            });
            var box = new TextBox
            {
                Theme = fieldTheme, FontSize = 13, Padding = new Thickness(8, 6), Text = initial,
                PlaceholderText = placeholder, VerticalContentAlignment = VerticalAlignment.Center,
            };
            boxes.Add(box);
            stack.Children.Add(box);
        }

        var lumenTheme = Application.Current?.FindResource("LumenButton") as ControlTheme;
        var grayTheme = Application.Current?.FindResource("LumenButtonGray") as ControlTheme;
        var cancel = new Button { Content = "Cancel", Theme = grayTheme, FontSize = 12.5, Padding = new Thickness(16, 7) };
        var ok = new Button { Content = confirmText, Theme = lumenTheme, FontSize = 12.5, Padding = new Thickness(16, 7) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = stack,
        };
        card.PointerPressed += (_, e) =>
        {
            if (e.Source is not TextBox && e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                win.BeginMoveDrag(e);
        };

        win.Content = card;
        win.Opened += (_, _) => { Motion.ScaleIn(card, 0.96, 160); if (boxes.Count > 0) boxes[0].Focus(); };

        bool closing = false;
        void CloseWith(string[]? result)
        {
            if (closing) return;
            closing = true;
            Motion.CollapseOut(card, 130, () => win.Close(result));
        }
        cancel.Click += (_, _) => CloseWith(null);
        ok.Click += (_, _) => CloseWith(boxes.ConvertAll(b => b.Text ?? "").ToArray());
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseWith(null);
            else if (e.Key == Key.Enter) CloseWith(boxes.ConvertAll(b => b.Text ?? "").ToArray());
        };

        return await win.ShowDialog<string[]?>(owner);
    }
}
