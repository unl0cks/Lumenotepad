using Avalonia.Controls;
using Avalonia.Input;
using Lumenotepad.Platform;
using Lumenotepad.Services;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

/// <summary>The simple preferences window (non-modal, one instance, themed via the token brushes).
/// Binds straight to the MainViewModel's persisted settings properties; the theme picker and the
/// toolbar combos are wired in code because they map strings, not booleans.</summary>
public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();

        Opened += (_, _) => WinChrome.RoundCorners(this, true);
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PrefsTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        ThemeList.ItemsSource = ThemePalettes.Themes;
        DataContextChanged += (_, _) => SyncFromVm();
        ThemeList.SelectionChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm && ThemeList.SelectedItem is string theme && vm.Theme != theme)
                vm.Theme = theme;
        };

        ToolbarPosBox.ItemsSource = new[] { "Top", "Left", "Right", "Bottom" };
        ToolbarScopeBox.ItemsSource = new[] { "Window", "Page" };
        ToolbarPosBox.SelectionChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm && ToolbarPosBox.SelectedItem is string pos) vm.ToolbarPosition = pos;
        };
        ToolbarScopeBox.SelectionChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm && ToolbarScopeBox.SelectedItem is string scope) vm.ToolbarScope = scope;
        };
    }

    private void SyncFromVm()
    {
        if (DataContext is not MainViewModel vm) return;
        ThemeList.SelectedItem = vm.Theme;
        ToolbarPosBox.SelectedItem = vm.ToolbarPosition;
        ToolbarScopeBox.SelectedItem = vm.ToolbarScope;
    }
}
