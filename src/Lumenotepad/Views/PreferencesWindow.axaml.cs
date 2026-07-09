using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Lumenotepad.Platform;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

/// <summary>The preferences window (non-modal, one instance, themed via the token brushes): a left
/// category nav swapping keyed panels on the right. Binds straight to the MainViewModel's persisted
/// settings properties; the theme picker and combos are wired in code because they map strings.</summary>
public partial class PreferencesWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Nav Tag → panel. Later tasks add entries as their categories land.</summary>
    private readonly Dictionary<string, Control> _panels;

    public PreferencesWindow()
    {
        InitializeComponent();
        _panels = new()
        {
            ["appearance"] = AppearancePanel,
            ["layout"] = LayoutPanel,
            ["canvas"] = CanvasPanel,
        };

        Opened += (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.97);   // fade + scale in on open
        };
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        // Fade + scale out before actually closing (covers the X, Escape, and outside-close paths).
        bool closing = false;
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            if (Content is Control root) Motion.CollapseOut(root, Motion.Fast, Close);
            else Close();
        };
        PrefsTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        ThemeList.ItemsSource = ThemePalettes.Themes;
        DataContextChanged += (_, _) => SyncFromVm();
        ThemeList.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && ThemeList.SelectedItem is string theme && vm.Theme != theme)
                vm.Theme = theme;
        };

        ToolbarPosBox.ItemsSource = new[] { "Top", "Left", "Right", "Bottom" };
        ToolbarScopeBox.ItemsSource = new[] { "Window", "Page" };
        ToolbarPosBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && ToolbarPosBox.SelectedItem is string pos) vm.ToolbarPosition = pos;
        };
        ToolbarScopeBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && ToolbarScopeBox.SelectedItem is string scope) vm.ToolbarScope = scope;
        };

        NavList.SelectionChanged += (_, _) =>
        {
            if (NavList.SelectedItem is ListBoxItem { Tag: string key }) ShowPanel(key);
        };
        NavList.SelectedIndex = 0;
    }

    /// <summary>Show one category panel and rise it in (the others hide).</summary>
    private void ShowPanel(string key)
    {
        if (!_panels.TryGetValue(key, out var panel)) return;
        foreach (var (k, p) in _panels) p.IsVisible = k == key;
        Motion.RiseIn(panel, Motion.Fast);
    }

    private void SyncFromVm()
    {
        if (Vm is not { } vm) return;
        ThemeList.SelectedItem = vm.Theme;
        ToolbarPosBox.SelectedItem = vm.ToolbarPosition;
        ToolbarScopeBox.SelectedItem = vm.ToolbarScope;
    }
}
