using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

/// <summary>A tiny modal for reordering a list (sections or pages): each row has up/down buttons that
/// call back into the owner's collection. Chromeless + Lumen-styled like the app's other dialogs.</summary>
public sealed class ReorderDialog : Window
{
    private readonly List<string> _names;
    private readonly Action<int, int> _move;   // (from, to) — mutates the real collection live
    private readonly StackPanel _rows = new() { Spacing = 4 };

    private ReorderDialog(string title, IReadOnlyList<string> names, Action<int, int> move)
    {
        _names = new List<string>(names);
        _move = move;

        Title = title;
        Width = 360; Height = 460; MinWidth = 300; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Background = this.FindResource("WindowSurfaceBrush") as IBrush;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent };
        FontFamily = Application.Current?.FindResource("UiFont") as FontFamily ?? FontFamily.Default;
        Foreground = this.FindResource("TextPrimaryBrush") as IBrush;

        var titleBar = new Grid { Height = 38 };
        titleBar.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
        });
        titleBar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var doneBtn = new Button
        {
            Theme = this.FindResource("LumenButton") as Avalonia.Styling.ControlTheme,
            Content = "Done", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right,
        };
        doneBtn.Click += (_, _) => Close();

        var dock = new DockPanel { Margin = new Thickness(0) };
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);
        var footer = new Border { Padding = new Thickness(16, 8, 16, 14), Child = doneBtn };
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = _rows, Margin = new Thickness(14, 2, 14, 2) });
        Content = dock;

        Rebuild();
        Opened += (_, _) => WinChrome.RoundCorners(this, true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public static Task Show(Window owner, string title, IReadOnlyList<string> names, Action<int, int> move)
        => new ReorderDialog(title, names, move).ShowDialog(owner);

    private void Rebuild()
    {
        _rows.Children.Clear();
        for (int i = 0; i < _names.Count; i++)
        {
            int idx = i;
            var name = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_names[i]) ? "Untitled" : _names[i],
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var up = MoveButton("", idx > 0, () => Move(idx, idx - 1));
            var down = MoveButton("", idx < _names.Count - 1, () => Move(idx, idx + 1));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(name);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            _rows.Children.Add(new Border
            {
                Background = this.FindResource("ControlHoverBrush") as IBrush,
                CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 7), Child = grid,
            });
        }
    }

    private Button MoveButton(string glyph, bool enabled, Action act)
    {
        var b = new Button
        {
            Theme = this.FindResource("IconButton") as Avalonia.Styling.ControlTheme,
            Width = 28, Height = 28, FontSize = 12, IsEnabled = enabled,
            FontFamily = Application.Current?.FindResource("IconFont") as FontFamily, Content = glyph,
        };
        b.Click += (_, _) => act();
        return b;
    }

    private void Move(int from, int to)
    {
        if (to < 0 || to >= _names.Count) return;
        _move(from, to);                                  // reorder the real collection
        (_names[from], _names[to]) = (_names[to], _names[from]);
        Rebuild();
    }
}
