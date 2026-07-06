using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Editor;

namespace Lumenotepad.Views;

/// <summary>The formatting toolbar: bold/italic/underline/strike, highlight + text-color swatches,
/// font size, a font picker, and the dock menu (top/left/right/bottom). Drives a target
/// <see cref="RichTextEditor"/> and mirrors its caret format via SelectionChanged.</summary>
public partial class FormatToolbar : UserControl
{
    private static readonly (string? Hex, string Name)[] Highlights =
    {
        (null, "None"),
        ("#66FFD666", "Yellow"), ("#6699E28A", "Green"), ("#66FF8FAB", "Pink"),
        ("#664DA6FF", "Blue"), ("#66C9A0FF", "Purple"),
    };

    private static readonly (string? Hex, string Name)[] TextColors =
    {
        (null, "Default"),
        ("#FF8FAB", "Pink"), ("#FFD666", "Yellow"), ("#8AE29B", "Green"),
        ("#4DA6FF", "Blue"), ("#C9A0FF", "Purple"), ("#FF6B6B", "Red"),
    };

    private RichTextEditor? _target;
    private bool _syncing;

    /// <summary>Raised when the user picks a dock side ("Top"/"Left"/"Right"/"Bottom").</summary>
    public event Action<string>? DockRequested;

    public RichTextEditor? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.SelectionChanged -= UpdateFromEditor;
            _target = value;
            if (_target is not null) _target.SelectionChanged += UpdateFromEditor;
            UpdateFromEditor();
        }
    }

    public FormatToolbar()
    {
        InitializeComponent();

        BoldBtn.Click += (_, _) => Do(e => e.ToggleBold());
        ItalicBtn.Click += (_, _) => Do(e => e.ToggleItalic());
        UnderBtn.Click += (_, _) => Do(e => e.ToggleUnderline());
        StrikeBtn.Click += (_, _) => Do(e => e.ToggleStrike());
        SizeMinus.Click += (_, _) => NudgeSize(-1);
        SizePlus.Click += (_, _) => NudgeSize(+1);

        BuildSwatches(HighlightSwatches, Highlights, hex => Do(e => e.ApplyHighlight(hex)), HighlightBtn);
        BuildSwatches(ColorSwatches, TextColors, hex => Do(e => e.ApplyColor(hex)), ColorBtn);
        BuildFontList();
        BuildDockMenu();
    }

    /// <summary>Reflow for a side dock: vertical strip, flyouts open sideways.</summary>
    public void SetVertical(bool vertical)
    {
        Panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        SizeGroup.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var placement = vertical ? PlacementMode.Right : PlacementMode.Bottom;
        foreach (var b in new[] { HighlightBtn, ColorBtn, FontBtn })
            if (b.Flyout is PopupFlyoutBase pf) pf.Placement = placement;
        if (DockBtn.Flyout is PopupFlyoutBase df) df.Placement = placement;
    }

    private void Do(Action<RichTextEditor> action)
    {
        if (_target is null) return;
        action(_target);
        _target.Focus();          // formatting shouldn't steal the caret
    }

    private void NudgeSize(int delta)
    {
        if (_target is null) return;
        double cur = _target.CurrentFormat.Size ?? _target.FontSize;
        double next = Math.Clamp(cur + delta, 8, 72);
        Do(e => e.ApplySize(Math.Abs(next - e.FontSize) < 0.01 ? null : next));
        UpdateFromEditor();
    }

    private void BuildSwatches(StackPanel host, (string? Hex, string Name)[] items, Action<string?> apply, Button owner)
    {
        foreach (var (hex, name) in items)
        {
            var b = new Button
            {
                Classes = { "swatch" },
                Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
                Content = hex is null ? new TextBlock { Text = "∅", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center } : null,
            };
            ToolTip.SetTip(b, name);
            b.Click += (_, _) => { apply(hex); owner.Flyout?.Hide(); };
            host.Children.Add(b);
        }
    }

    private void BuildFontList()
    {
        var names = FontManager.Current.SystemFonts.Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
        names.Insert(0, "(Default)");
        FontList.ItemsSource = names;
        FontList.SelectionChanged += (_, _) =>
        {
            if (_syncing || FontList.SelectedItem is not string name) return;
            Do(e => e.ApplyFont(name == "(Default)" ? null : name));
            FontBtn.Flyout?.Hide();
        };
    }

    private void BuildDockMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var pos in new[] { "Top", "Left", "Right", "Bottom" })
        {
            var item = new MenuItem { Header = $"Dock {pos.ToLowerInvariant()}" };
            item.Click += (_, _) => DockRequested?.Invoke(pos);
            flyout.Items.Add(item);
        }
        DockBtn.Flyout = flyout;
        DockBtn.Click += (_, _) => flyout.ShowAt(DockBtn);
    }

    private void UpdateFromEditor()
    {
        if (_target is null) return;
        _syncing = true;
        try
        {
            var f = _target.CurrentFormat;
            BoldBtn.Classes.Set("on", f.Bold);
            ItalicBtn.Classes.Set("on", f.Italic);
            UnderBtn.Classes.Set("on", f.Underline);
            StrikeBtn.Classes.Set("on", f.Strike);
            HighlightBtn.Classes.Set("on", f.Highlight is not null);
            ColorBtn.Classes.Set("on", f.Color is not null);
            SizeText.Text = (f.Size ?? _target.FontSize).ToString("0");
            FontList.SelectedItem = f.Font ?? "(Default)";
        }
        finally { _syncing = false; }
    }
}
