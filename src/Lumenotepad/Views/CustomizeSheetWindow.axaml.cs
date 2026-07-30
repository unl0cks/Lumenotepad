using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lumenotepad.Platform;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

public partial class CustomizeSheetWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Models.Page? _page;
    private readonly Models.Section? _section;

    private const string PickKeep = "keep", PickInherit = "inherit";
    private string _pick;

    public CustomizeSheetWindow(MainViewModel vm, Models.Page page) : this(vm, page, null) { }
    public CustomizeSheetWindow(MainViewModel vm, Models.Section section) : this(vm, null, section) { }

    private CustomizeSheetWindow(MainViewModel vm, Models.Page? page, Models.Section? section)
    {
        _vm = vm;
        _page = page;
        _section = section;
        _pick = section is not null ? PickKeep : page!.PageStyle ?? PickInherit;
        InitializeComponent();

        bool sec = section is not null;
        Title = sec ? "Customize section" : "Customize page";
        SheetTitle.Text = Title;
        ScopeHint.IsVisible = sec;
        NameBox.Text = sec ? section!.Name : page!.Title;
        FootHint.Text = sec ? "Pages that already have notes keep them." : "";

        BuildChips();

        int mode = page?.PageStyleMode ?? Editor.PageStyles.ModeGuides;
        (mode switch
        {
            Editor.PageStyles.ModeStartersOnly => ModeStarters,
            Editor.PageStyles.ModeRigid => ModeRigid,
            _ => ModeGuides,
        }).IsChecked = true;
        foreach (var radio in new[] { ModeGuides, ModeStarters, ModeRigid })
            radio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true } r) Motion.ScaleIn(r, 0.96, Motion.Fast);
            };
        UpdateModeEnabled();

        var gridChoices = sec
            ? new[] { "Keep current", "Inherit", "Blank", "Ruled", "Grid", "Dots" }
            : new[] { "Inherit", "Blank", "Ruled", "Grid", "Dots" };
        GridBox.ItemsSource = gridChoices;
        GridBox.SelectedIndex = sec ? 0 : Math.Max(0, Array.IndexOf(gridChoices, page!.GridStyle));
        MenuFx.AttachDropDown(GridBox);
        SmoothScroll.Attach(SheetScroll);

        Opened += (_, _) =>
        {
            Services.ThemeManager.UseMacNativeChrome(this, SheetTitleBar);
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);
            NameBox.Focus();
        };
        bool closing = false;
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
            else Close();
        };
        SheetTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        CloseBtn.Click += (_, _) => Close();
        CancelBtn.Click += (_, _) => Close();
        SaveBtn.Click += (_, _) => SaveAndClose();
    }

    private void UpdateModeEnabled() => ModePanel.IsEnabled = _pick is not (PickKeep or PickInherit);

    private void BuildChips()
    {
        StyleChips.Children.Clear();
        var entries = (_section is not null
                ? new[] { (Key: PickKeep, Label: "Keep current") }
                : Array.Empty<(string Key, string Label)>())
            .Concat(new[] { (Key: PickInherit, Label: "Inherit") })
            .Concat(Editor.PageStyles.Styles.Select(s => (Key: s, Label: s)));
        foreach (var (key, label) in entries)
            StyleChips.Children.Add(MakeChip(key, label));
    }

    private Control MakeChip(string key, string label)
    {
        bool selected = string.Equals(_pick, key, StringComparison.Ordinal);
        Control preview;
        if (key is PickKeep or PickInherit)
        {
            preview = new TextBlock
            {
                Text = "—", FontSize = 20,
                Foreground = this.FindResource("TextMutedBrush") as IBrush ?? Brushes.Gray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }
        else
        {
            var layer = new Editor.GuideLayer
            {
                Width = 104, Height = 62, Viewport = new Avalonia.Size(104, 62),
                PreviewMotif = true,
            };
            layer.SetStyles(Editor.PageStyles.Blank, key, Editor.PageStyles.ModeGuides);
            preview = layer;
        }
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Avalonia.Thickness(6),
            BorderThickness = new Avalonia.Thickness(selected ? 2 : 1),
            BorderBrush = selected
                ? (this.FindResource("AccentBrush") as IBrush ?? Brushes.White)
                : (this.FindResource("FrameBorderBrush") as IBrush ?? Brushes.Gray),
            Margin = new Avalonia.Thickness(0, 0, 8, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = key,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Width = 104, Height = 62, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                        Background = this.FindResource("PaperBackgroundBrush") as IBrush ?? Brushes.Black,
                        Child = preview,
                    },
                    new TextBlock
                    {
                        Text = label, FontSize = 11.5,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    },
                },
            },
        };
        chip.PointerPressed += (_, _) =>
        {
            _pick = key;
            BuildChips();
            UpdateModeEnabled();
            if (StyleChips.Children.OfType<Border>().FirstOrDefault(c => Equals(c.Tag, key)) is { } picked)
                Motion.ScaleIn(picked, 0.94, Motion.Fast);
        };
        return chip;
    }

    private async void SaveAndClose()
    {
        var name = (NameBox.Text ?? "").Trim();
        int mode = ModeStarters.IsChecked == true ? Editor.PageStyles.ModeStartersOnly
                 : ModeRigid.IsChecked == true ? Editor.PageStyles.ModeRigid
                 : Editor.PageStyles.ModeGuides;

        if (_section is { } sec)
        {
            if (name.Length > 0) sec.Name = name;
            bool setStyle = _pick != PickKeep;
            string? style = _pick == PickInherit ? null : _pick;
            bool setGrid = GridBox.SelectedIndex > 0;
            string? grid = GridBox.SelectedIndex <= 1 ? null : GridBox.SelectedItem as string;
            _vm.ApplySectionStyle(sec, setStyle, setStyle ? style : null, mode, setGrid, grid);
            Close();
            return;
        }

        var pg = _page!;
        if (name.Length > 0) pg.Title = name;
        string? newStyle = _pick == PickInherit ? null : _pick;
        bool styleChanged = !string.Equals(pg.PageStyle, newStyle, StringComparison.Ordinal)
                            || (newStyle is not null && pg.PageStyleMode != mode);
        _vm.SetPageStyleChoice(pg, newStyle, mode);
        _vm.SetPageGridStyle(pg, GridBox.SelectedIndex <= 0 ? null : GridBox.SelectedItem as string);

        if (newStyle is not null && newStyle != Editor.PageStyles.Freeform && styleChanged)
        {
            var doc = _vm.DocumentFor(pg);
            bool stamp = doc.Boxes.Count == 0
                || await Views.ConfirmDialog.Show(this, "Add starter layout?",
                    $"Add the {newStyle} starter containers to this page? Your existing notes stay untouched.",
                    "Add", danger: false);
            if (stamp) _vm.StampPageStyle(pg);
        }
        Close();
    }
}
