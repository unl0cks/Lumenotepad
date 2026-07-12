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
            ["general"] = GeneralPanel,
            ["appearance"] = AppearancePanel,
            ["layout"] = LayoutPanel,
            ["canvas"] = CanvasPanel,
            ["editor"] = EditorPanel,
            ["shortcuts"] = ShortcutsPanel,
            ["fonts"] = FontsPanel,
            ["bullets"] = BulletsPanel,
            ["data"] = DataPanel,
        };

        Opened += (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);   // quick fade + scale in
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
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
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

        LaunchTargetBox.ItemsSource = new[] { "Home page", "Last page" };
        LaunchTargetBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && LaunchTargetBox.SelectedItem is string s)
            {
                var v = s == "Last page" ? "LastPage" : "Home";
                if (vm.LaunchTarget != v) vm.LaunchTarget = v;
            }
        };
        StartupToggle.IsCheckedChanged += (_, _) =>
        {
            if (_syncingStartup) return;
            Platform.StartupRegistry.SetEnabled(StartupToggle.IsChecked == true);
        };
        AutosaveSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && vm.AutosaveMs != (int)e.NewValue) vm.AutosaveMs = (int)e.NewValue;
            AutosaveValue.Text = $"{e.NewValue / 1000.0:0.#}s";
        };
        RecentCountSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && vm.RecentCount != (int)e.NewValue) vm.RecentCount = (int)e.NewValue;
            RecentCountValue.Text = ((int)e.NewValue).ToString();
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
            if (key == "shortcuts") BuildShortcutRows();
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
            ShowPanel("general");
        };
        RelockBtn.Click += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.AdvancedUnlocked = false;
            UpdateGateVisuals();
            _navGuard = true; NavList.SelectedIndex = 0; _navGuard = false;
            _lastNav = NavList.SelectedItem;
            ShowPanel("general");
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
        CaretColorBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Vm is not { } vm) return;
            if (string.IsNullOrWhiteSpace(CaretColorBox.Text)) vm.CaretColor = null;
            else if (ThemePalettes.NormalizeHex(CaretColorBox.Text) is { } norm) vm.CaretColor = norm;
            CaretColorBox.Text = vm.CaretColor ?? "";
        };
        CaretWidthSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && Math.Abs(vm.CaretWidth - e.NewValue) > 1e-6) vm.CaretWidth = e.NewValue;
            CaretWidthValue.Text = $"{e.NewValue:0.0}";
        };
        BuildHighlightChoices();
        DateFormatBox.ItemsSource = DateFormats.Select(f => DateTime.Now.ToString(f)).ToArray();
        DateFormatBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && DateFormatBox.SelectedIndex is >= 0 and var i && i < DateFormats.Length
                && vm.DateFormat != DateFormats[i]) vm.DateFormat = DateFormats[i];
        };
        UserNameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Vm is { } vm) vm.UserName = UserNameBox.Text ?? "";
        };
        UserNameBox.LostFocus += (_, _) => { if (Vm is { } vm) vm.UserName = UserNameBox.Text ?? ""; };
        CardSizeBox.ItemsSource = new[] { "Small", "Medium", "Large" };
        CardSizeBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && CardSizeBox.SelectedItem is string cs && vm.CardSize != cs) vm.CardSize = cs;
        };
        NewNoteWidthSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && Math.Abs(vm.NewNoteWidth - e.NewValue) > 0.5) vm.NewNoteWidth = e.NewValue;
            NewNoteWidthValue.Text = ((int)e.NewValue).ToString();
        };

        // ItemsSource is (re)built in RefreshEditorFontList — the ctor runs before the DataContext
        // lands, so building it here would permanently miss the Extended-fonts master switch.
        EditorFontBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && EditorFontBox.SelectedItem is string f)
            {
                var v = f == "(Default)" ? null : f;
                if (vm.EditorFont != v) vm.EditorFont = v;
            }
        };
        WireScaleSlider(EditorFontSizeSlider, EditorFontSizeValue, v => v.ToString("0"),
            vm => vm.EditorFontSize, (vm, v) => vm.EditorFontSize = v);
        WireScaleSlider(LineSpacingSlider, LineSpacingValue, v => $"{v:0.0}×",
            vm => vm.LineSpacingScale, (vm, v) => vm.LineSpacingScale = v);
        WireScaleSlider(ParaSpacingSlider, ParaSpacingValue, v => $"{v:0.0}×",
            vm => vm.ParagraphSpacingScale, (vm, v) => vm.ParagraphSpacingScale = v);
        WireScaleSlider(IndentScaleSlider, IndentScaleValue, v => $"{v:0.0}×",
            vm => vm.IndentScale, (vm, v) => vm.IndentScale = v);
        WirePaletteEditor(TextPaletteChips, TextPaletteHexBox, TextPaletteReset, highlight: false);
        WirePaletteEditor(HighlightPaletteChips, HighlightPaletteHexBox, HighlightPaletteReset, highlight: true);

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
    private bool _syncingStartup;

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
            RefreshEditorFontList();               // the Note-font combo offers the same candidates
        }
        else if (e.PropertyName == nameof(MainViewModel.PalettePrefsVersion))
        {
            BuildPaletteChips(TextPaletteChips, false);
            BuildPaletteChips(HighlightPaletteChips, true);
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

    /// <summary>The Ctrl+Shift+T format presets (shown rendered with today's date).</summary>
    private static readonly string[] DateFormats =
        { "yyyy-MM-dd", "MMMM d, yyyy", "dd/MM/yyyy", "yyyy-MM-dd HH:mm", "HH:mm" };

    /// <summary>The quick-highlight choices — the toolbar's own highlight palette
    /// (FormatToolbar.Highlights' non-null hexes, verbatim).</summary>
    private void BuildHighlightChoices()
    {
        foreach (var hex in new[] { "#66FFD666", "#6699E28A", "#66FF8FAB", "#664DA6FF", "#66C9A0FF" })
        {
            var chip = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#66808080")), BorderThickness = new Thickness(1),
                Tag = hex,
            };
            chip.PointerPressed += (_, _) => { if (Vm is { } vm) vm.DefaultHighlight = hex; UpdateHighlightRings(); };
            HighlightChoices.Children.Add(chip);
        }
        UpdateHighlightRings();
    }

    /// <summary>Ring the chip whose Tag matches the current pref — tag-based, no color reconstruction.</summary>
    private void UpdateHighlightRings()
    {
        foreach (var child in HighlightChoices.Children)
            if (child is Border { Tag: string hex } b)
                b.BorderThickness = new Thickness(
                    string.Equals(Vm?.DefaultHighlight, hex, StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }

    /// <summary>One slider + live value label bound to a double VM pref (epsilon write-guard).</summary>
    private void WireScaleSlider(Slider slider, TextBlock label, Func<double, string> fmt,
                                 Func<MainViewModel, double> get, Action<MainViewModel, double> set)
    {
        slider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && Math.Abs(get(vm) - e.NewValue) > 1e-6) set(vm, e.NewValue);
            label.Text = fmt(e.NewValue);
        };
    }

    private void WirePaletteEditor(WrapPanel chips, TextBox hexBox, Button reset, bool highlight)
    {
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Vm is not { } vm) return;
            // highlights carry alpha — accept #AARRGGBB too (8 hex digits), else normalize 6
            var raw = (hexBox.Text ?? "").Trim().TrimStart('#');
            string? hex = raw.Length == 8 && raw.All(Uri.IsHexDigit) ? "#" + raw.ToUpperInvariant()
                        : ThemePalettes.NormalizeHex(hexBox.Text);
            if (hex is null) return;
            if (highlight && hex.Length == 7) hex = "#66" + hex[1..];   // default highlight alpha
            vm.AddPaletteColor(highlight, hex,
                highlight ? FormatToolbar.BuiltInHighlights : FormatToolbar.BuiltInTextColors);
            hexBox.Text = "";
        };
        reset.Click += (_, _) => Vm?.ResetPalette(highlight);
        BuildPaletteChips(chips, highlight);
    }

    private void BuildPaletteChips(WrapPanel chips, bool highlight)
    {
        chips.Children.Clear();
        if (Vm is not { } vm) return;
        foreach (var hex in vm.PaletteFor(highlight,
                     highlight ? FormatToolbar.BuiltInHighlights : FormatToolbar.BuiltInTextColors))
        {
            var chip = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 4, 4),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#66808080")), BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(chip, $"{hex} — right-click to remove");
            var captured = hex;
            chip.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(chip).Properties.IsRightButtonPressed && Vm is { } v)
                    v.RemovePaletteColor(highlight, captured,
                        highlight ? FormatToolbar.BuiltInHighlights : FormatToolbar.BuiltInTextColors);
            };
            chips.Children.Add(chip);
        }
    }

    /// <summary>The keyboard reference table: build once on first nav to "shortcuts" (static content).</summary>
    private void BuildShortcutRows()
    {
        if (ShortcutRows.Children.Count > 0) return;
        foreach (var (keys, what) in new[]
        {
            ("Ctrl+B / Ctrl+I / Ctrl+U", "Bold / italic / underline"),
            ("Ctrl+Shift+S", "Strikethrough"),
            ("Ctrl+Shift+H", "Quick highlight (color set above)"),
            ("Ctrl+Shift+8", "Bullet list"),
            ("Ctrl+Shift+7", "Numbered list"),
            ("Ctrl+Shift+T", "Insert date & time"),
            ("Ctrl+A", "Select all"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo"),
            ("Ctrl+C / Ctrl+X / Ctrl+V", "Copy / cut / paste"),
            ("Ctrl+Left / Ctrl+Right", "Jump by word"),
            ("Ctrl+Backspace / Ctrl+Delete", "Delete previous / next word"),
            ("Escape", "Close dialogs and the preferences window"),
        })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
            var k = new TextBlock { Text = keys, FontSize = 12.5, FontWeight = FontWeight.SemiBold };
            var w = new TextBlock { Text = what, FontSize = 12.5, Opacity = 0.8 };
            Grid.SetColumn(w, 1);
            row.Children.Add(k);
            row.Children.Add(w);
            ShortcutRows.Children.Add(row);
        }
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
        LaunchTargetBox.SelectedItem = vm.LaunchTarget == "LastPage" ? "Last page" : "Home page";
        AutosaveSlider.Value = vm.AutosaveMs;
        AutosaveValue.Text = $"{vm.AutosaveMs / 1000.0:0.#}s";
        RecentCountSlider.Value = vm.RecentCount;
        RecentCountValue.Text = vm.RecentCount.ToString();
        _syncingStartup = true;
        StartupToggle.IsChecked = Platform.StartupRegistry.IsEnabled();
        _syncingStartup = false;
        CaretColorBox.Text = vm.CaretColor ?? "";
        CaretWidthSlider.Value = vm.CaretWidth;
        CaretWidthValue.Text = $"{vm.CaretWidth:0.0}";
        DateFormatBox.SelectedIndex = Math.Max(0, Array.IndexOf(DateFormats, vm.DateFormat));
        UserNameBox.Text = vm.UserName;
        CardSizeBox.SelectedItem = vm.CardSize;
        NewNoteWidthSlider.Value = vm.NewNoteWidth;
        NewNoteWidthValue.Text = ((int)vm.NewNoteWidth).ToString();
        UpdateHighlightRings();
        RefreshEditorFontList();
        EditorFontSizeSlider.Value = vm.EditorFontSize;
        EditorFontSizeValue.Text = vm.EditorFontSize.ToString("0");
        LineSpacingSlider.Value = vm.LineSpacingScale;
        LineSpacingValue.Text = $"{vm.LineSpacingScale:0.0}×";
        ParaSpacingSlider.Value = vm.ParagraphSpacingScale;
        ParaSpacingValue.Text = $"{vm.ParagraphSpacingScale:0.0}×";
        IndentScaleSlider.Value = vm.IndentScale;
        IndentScaleValue.Text = $"{vm.IndentScale:0.0}×";
        BuildPaletteChips(TextPaletteChips, false);
        BuildPaletteChips(HighlightPaletteChips, true);
        UpdateGateVisuals();
    }

    /// <summary>(Re)build the Note-font combo. The ctor runs before the DataContext lands (so a
    /// ctor-built list would always read ExtendedFonts=false), and the Extended-fonts master switch
    /// changes the candidate list — rebuild on sync and on the switch.</summary>
    private void RefreshEditorFontList()
    {
        EditorFontBox.ItemsSource =
            new[] { "(Default)" }.Concat(AppFonts.ListNames(Vm?.ExtendedFonts ?? false)).ToArray();
        EditorFontBox.SelectedItem = Vm?.EditorFont ?? "(Default)";
    }
}
