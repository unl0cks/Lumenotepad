using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Lumenotepad.Editor;
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
            ["fonts"] = FontsPanel,
            ["bullets"] = BulletsPanel,
            ["data"] = DataPanel,
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

        NavList.SelectionChanged += async (_, _) =>
        {
            if (_navGuard) return;
            if (NavList.SelectedItem is not ListBoxItem { Tag: string key }) return;
            if (IsGated(key) && Vm is { AdvancedUnlocked: false } vm)
            {
                _navGuard = true;
                bool ok;
                try
                {
                    ok = await ConfirmDialog.Show(this, "Unlock advanced settings?",
                        "Advanced settings change how notes are stored, exported, and rendered. " +
                        "They're meant for power users — the defaults are right for most people.",
                        "Unlock", danger: false);
                }
                finally { _navGuard = false; }
                if (!ok) { NavList.SelectedItem = _lastNav; return; }
                vm.AdvancedUnlocked = true;
                UpdateGateVisuals();
            }
            _lastNav = NavList.SelectedItem;
            if (key == "data") RefreshDataPanel();
            if (key == "fonts") RefreshFontChoices();
            ShowPanel(key);
        };
        NavList.SelectedIndex = 0;
        _lastNav = NavList.SelectedItem;

        OpenDataBtn.Click += (_, _) =>
        {
            if (Vm?.SettingsDir is { } d)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{d}\"") { UseShellExecute = true });
        };
        ResetBtn.Click += async (_, _) =>
        {
            if (Vm is not { } vm) return;
            if (!await ConfirmDialog.Show(this, "Reset settings?",
                "Every preference returns to its default. Your notebooks and notes are not touched.",
                "Reset")) return;
            vm.ResetSettingsToDefaults();
            SyncFromVm();
            UpdateGateVisuals();
            _navGuard = true; NavList.SelectedIndex = 0; _navGuard = false;
            _lastNav = NavList.SelectedItem;
            ShowPanel("appearance");
        };
        RelockBtn.Click += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.AdvancedUnlocked = false;
            UpdateGateVisuals();
            _navGuard = true; NavList.SelectedIndex = 0; _navGuard = false;
            _lastNav = NavList.SelectedItem;
            ShowPanel("appearance");
        };

        AccentHexBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Vm is not { } vm) return;
            if (string.IsNullOrWhiteSpace(AccentHexBox.Text)) vm.CustomAccent = null;
            else if (ThemePalettes.NormalizeHex(AccentHexBox.Text) is { } norm) vm.CustomAccent = norm;
            AccentHexBox.Text = vm.CustomAccent ?? "";
        };
        GlassTintSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && Math.Abs(vm.GlassTint - e.NewValue) > 1e-6) vm.GlassTint = e.NewValue;
            GlassTintValue.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
        };
        MotionSpeedBox.ItemsSource = new[] { "Calm", "Normal", "Snappy" };
        MotionSpeedBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && MotionSpeedBox.SelectedItem is string speed) vm.MotionSpeed = speed;
        };

        foreach (var (box, get, set) in new (ComboBox, Func<MainViewModel, bool?>, Action<MainViewModel, bool?>)[]
        {
            (NumBoldBox, vm => vm.NumBoldDefault, (vm, v) => vm.NumBoldDefault = v),
            (NumItalicBox, vm => vm.NumItalicDefault, (vm, v) => vm.NumItalicDefault = v),
            (NumUnderlineBox, vm => vm.NumUnderlineDefault, (vm, v) => vm.NumUnderlineDefault = v),
            (NumStrikeBox, vm => vm.NumStrikeDefault, (vm, v) => vm.NumStrikeDefault = v),
        })
        {
            box.ItemsSource = new[] { "Match text", "Always on", "Always off" };
            box.SelectionChanged += (_, _) =>
            {
                if (Vm is { } vm && box.SelectedItem is string s)
                {
                    bool? v = s switch { "Always on" => true, "Always off" => false, _ => null };
                    if (get(vm) != v) set(vm, v);
                }
            };
        }
        DataContextChanged += (_, _) => HookVmChanges();
        HookVmChanges();
        // The window subscribes to the long-lived VM; unhook on close or the VM pins the dead
        // window (and keeps invoking its handler) until the next prefs open re-hooks.
        Closed += (_, _) =>
        {
            if (_hookedVm is not null) _hookedVm.PropertyChanged -= OnVmChanged;
            _hookedVm = null;
        };
    }

    private bool _navGuard;
    private object? _lastNav;
    private MainViewModel? _hookedVm;

    /// <summary>Track VM changes that redraw prefs-local visuals (swatch ring, gate padlock).</summary>
    private void HookVmChanges()
    {
        if (_hookedVm is not null) _hookedVm.PropertyChanged -= OnVmChanged;
        _hookedVm = Vm;
        if (_hookedVm is not null) _hookedVm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CustomAccent))
        {
            AccentHexBox.Text = Vm?.CustomAccent ?? "";   // swatch picks echo into the hex field
            BuildAccentSwatches();
        }
        // Theme switches swap the token brush INSTANCES — rebuild so the active ring re-resolves.
        else if (e.PropertyName is nameof(MainViewModel.Theme)
                 or nameof(MainViewModel.FullTheme) or nameof(MainViewModel.PaperLight))
            BuildAccentSwatches();
        else if (e.PropertyName == nameof(MainViewModel.AdvancedUnlocked)) UpdateGateVisuals();
        else if (e.PropertyName == nameof(MainViewModel.BulletPrefsVersion)) BuildBulletRows();
        else if (e.PropertyName == nameof(MainViewModel.ExtendedFonts))
        {
            if (FontsPanel.IsVisible) RefreshFontChoices();
        }
    }

    /// <summary>The accent row: an "auto" chip (theme's own accent) + the six notebook colors.
    /// Rebuilt on every change — the active swatch carries the ring.</summary>
    private void BuildAccentSwatches()
    {
        AccentSwatches.Children.Clear();
        AccentSwatches.Children.Add(MakeSwatch(null, "Theme default"));
        foreach (var (hex, name) in MainViewModel.NotebookColors)
            AccentSwatches.Children.Add(MakeSwatch(hex, name));
    }

    private Control MakeSwatch(string? hex, string tip)
    {
        string? current = ThemePalettes.NormalizeHex(Vm?.CustomAccent);
        bool active = hex is null ? current is null
                                  : string.Equals(current, hex, StringComparison.OrdinalIgnoreCase);
        var ring = active
            ? this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White
            : new SolidColorBrush(Color.Parse("#66808080"));
        var b = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 2, 8, 4),
            Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = ring, BorderThickness = new Thickness(active ? 2 : 1),
            Child = hex is null
                ? new TextBlock
                {
                    Text = "A", FontSize = 11,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                }
                : null,
        };
        ToolTip.SetTip(b, tip);
        b.PointerPressed += (_, _) => { if (Vm is { } vm) vm.CustomAccent = hex; };
        return b;
    }

    private static readonly (string Key, string Name)[] BulletStyles =
    {
        ("dot", "Bullet"), ("arrow", "Arrow"), ("star", "Star"),
        ("heart", "Heart"), ("flower", "Flower"), ("spark", "Spark"),
    };

    private static readonly FontFamily BulletGlyphFont = new("Segoe UI Symbol, Segoe UI Emoji, Segoe UI");

    /// <summary>One row per bullet style: glyph (in its effective color), name, color button whose
    /// flyout offers the notebook palette + a reset to the built-in default.</summary>
    private void BuildBulletRows()
    {
        BulletColorRows.Children.Clear();
        foreach (var (key, name) in BulletStyles)
        {
            if (RichTextEditor.BulletGlyphInfo(key) is not { } info) continue;
            string effective = Vm?.BulletColorFor(key) ?? info.Color;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto") };
            var glyph = new TextBlock
            {
                Text = info.Glyph, FontFamily = BulletGlyphFont, FontSize = 15,
                Foreground = new SolidColorBrush(Color.Parse(effective)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var label = new TextBlock { Text = name, FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(label, 1);

            var swatch = new Button
            {
                Width = 34, Height = 22, CornerRadius = new CornerRadius(6), Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(effective)),
                BorderBrush = new SolidColorBrush(Color.Parse("#66808080")), BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(swatch, Vm?.BulletColorFor(key) is null ? "Default color" : "Custom color");
            swatch.Flyout = BuildBulletColorFlyout(key);
            Grid.SetColumn(swatch, 2);

            row.Children.Add(glyph);
            row.Children.Add(label);
            row.Children.Add(swatch);
            BulletColorRows.Children.Add(row);
        }
    }

    /// <summary>The palette flyout for one bullet style: 9 hue families × 5 shades + default reset.</summary>
    private Flyout BuildBulletColorFlyout(string styleKey)
    {
        var shades = new WrapPanel { MaxWidth = 190 };
        foreach (var (_, familyShades) in MainViewModel.NotebookPalette)
            foreach (var (shadeName, hex) in familyShades)
            {
                var chip = new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 3, 3),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                };
                ToolTip.SetTip(chip, shadeName);
                chip.PointerPressed += (_, _) => Vm?.SetBulletColor(styleKey, hex);
                shades.Children.Add(chip);
            }
        var reset = new Button { Content = "Reset to default", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        reset.Click += (_, _) => Vm?.SetBulletColor(styleKey, null);
        var panel = new StackPanel();
        panel.Children.Add(shades);
        panel.Children.Add(reset);
        return new Flyout { Content = panel, Placement = PlacementMode.Bottom };
    }

    private static string NumOpt(bool? v) => v switch { true => "Always on", false => "Always off", _ => "Match text" };

    /// <summary>Categories behind the Advanced confirmation ("bullets"/"fonts" arrive in later parts).</summary>
    private static bool IsGated(string key) => key is "data" or "bullets" or "fonts";

    /// <summary>Locked = the small padlock shows on the ADVANCED group header.</summary>
    private void UpdateGateVisuals() => GateLock.IsVisible = Vm is not { AdvancedUnlocked: true };

    /// <summary>Fill the Data &amp; tools facts (folder path + size) when the panel shows. The size
    /// walk runs off-thread — a workspace with embedded images can be thousands of files.</summary>
    private void RefreshDataPanel()
    {
        var dir = Vm?.SettingsDir;
        DataFolderText.Text = dir ?? "—";
        if (dir is null) { WorkspaceSizeText.Text = "—"; return; }
        WorkspaceSizeText.Text = "…";
        System.Threading.Tasks.Task.Run(() => FolderSize(dir)).ContinueWith(
            t => WorkspaceSizeText.Text = t.IsCompletedSuccessfully ? t.Result : "—",
            System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string FolderSize(string dir)
    {
        const long MB = 1 << 20;
        try
        {
            long b = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);   // FileInfo from the enumeration — no re-stat per file
            return b < MB ? $"{b / 1024.0:0.#} KB" : $"{(double)b / MB:0.#} MB";
        }
        catch { return "—"; }
    }

    /// <summary>One checklist row's state (mutable — the checkbox writes back through the VM).</summary>
    private sealed class FontChoice
    {
        public string Name = "";
        public bool Enabled = true;
        public bool Bundled;
    }

    private bool _fontTemplateSet;

    /// <summary>(Re)build the fonts checklist: every candidate the menu COULD offer (current
    /// master-switch mode), bundled faces locked on. Lazy — runs when the panel shows, and again
    /// when the master switch flips while it's visible.</summary>
    private void RefreshFontChoices()
    {
        if (Vm is not { } vm) return;
        if (!_fontTemplateSet)
        {
            _fontTemplateSet = true;
            // Virtualized recycling briefly rebuilds rows with a NULL datum, and FontFamily
            // helpers throw on null — the null-guard is load-bearing (known gotcha).
            FontsList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FontChoice?>((item, _) =>
            {
                var cb = new CheckBox { FontSize = 13, MinHeight = 0 };
                if (item is null) return cb;
                cb.Content = new TextBlock
                {
                    Text = item.Name,
                    FontFamily = AppFonts.Family(item.Name),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                cb.IsChecked = item.Enabled;             // set BEFORE subscribing — no spurious write
                cb.IsEnabled = !item.Bundled;
                if (item.Bundled) ToolTip.SetTip(cb, "Bundled fonts are always available");
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.IsChecked is { } v && v != item.Enabled)
                    {
                        item.Enabled = v;
                        Vm?.SetFontEnabled(item.Name, v);
                    }
                };
                return cb;
            });
        }
        var choices = AppFonts.ListNames(vm.ExtendedFonts)   // unfiltered candidates
            .Select(n => new FontChoice
            {
                Name = n,
                Bundled = AppFonts.Bundled.Contains(n, StringComparer.OrdinalIgnoreCase),
                Enabled = vm.IsFontEnabled(n),
            })
            .ToList();
        FontsList.ItemsSource = choices;
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
        AccentHexBox.Text = vm.CustomAccent ?? "";
        BuildAccentSwatches();
        GlassTintSlider.Value = vm.GlassTint;
        GlassTintValue.Text = $"{(int)Math.Round(vm.GlassTint * 100)}%";
        MotionSpeedBox.SelectedItem = vm.MotionSpeed;
        NumBoldBox.SelectedItem = NumOpt(vm.NumBoldDefault);
        NumItalicBox.SelectedItem = NumOpt(vm.NumItalicDefault);
        NumUnderlineBox.SelectedItem = NumOpt(vm.NumUnderlineDefault);
        NumStrikeBox.SelectedItem = NumOpt(vm.NumStrikeDefault);
        BuildBulletRows();
        if (FontsPanel.IsVisible) RefreshFontChoices();
        UpdateGateVisuals();
    }
}
