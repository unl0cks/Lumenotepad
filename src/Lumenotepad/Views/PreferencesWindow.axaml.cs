using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;   // SetTextAsync is an IClipboard extension, not a member
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
            ["about"] = AboutPanel,
        };

        Opened += (_, _) =>
        {
            Services.ThemeManager.UseMacNativeChrome(this, PrefsTitleBar);   // mac: native frame = rounded + frosted
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);   // quick fade + scale in
        };
        SmoothScroll.Attach(PrefsScroll);   // wheel-eased scrolling instead of the line-at-a-time jump
        // The fonts checklist needs a BOUNDED height to virtualize (the outer ScrollViewer measures
        // with infinite height) — anchored to its real on-screen offset (see SizeFontsList) rather
        // than a fixed guess, so it fills the window down to the bottom.
        PrefsScroll.SizeChanged += (_, _) => SizeFontsList();
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
        // Windows-only features stay off the prefs sheet elsewhere: the startup toggle writes the HKCU
        // Run key and the summon shortcut is a Win32 RegisterHotKey — both are inert no-ops off Windows,
        // so showing dead switches would only confuse (macOS tester feedback loop).
        if (!OperatingSystem.IsWindows())
        {
            StartupRow.IsVisible = false;
            SummonRow.IsVisible = false;
        }
        WireAbout();
    }

    /// <summary>The About page: identity, build facts, and the hand-off to the updater window. This is where
    /// updating lives now - the same place Lumen puts it.</summary>
    private void WireAbout()
    {
        AboutName.Text = Services.AppInfo.Name;
        AboutTagline.Text = Services.AppInfo.Tagline;
        AboutVersion.Text = $"Version {Services.AppInfo.Version}  ·  Build {Services.AppInfo.Build}"
            + (Services.AppInfo.Commit is { } sha ? $"  ·  {sha}" : "");
        AboutDetails.Text = Services.AppInfo.Details();

        bool canUpdate = Services.UpdateService.IsSupported;
        AboutUpdateBtn.IsEnabled = canUpdate;
        AboutAutoRow.IsVisible = canUpdate;
        AboutUpdateNote.Text = canUpdate
            ? (OperatingSystem.IsMacOS()
                ? "Updates download inside the app, so macOS does not ask you to approve them the way it did "
                  + "the first install. Your notebooks are never touched."
                : "This is a portable build: updating replaces the program files and leaves your userdata "
                  + "folder alone.")
            : "This copy cannot update itself in place - it is running from a development tree, or from a "
              + "folder it cannot write to. Download builds from the releases page instead.";
        ToolTip.SetTip(AboutUpdateBtn, canUpdate
            ? "Opens the updater: finds the right build for this computer, downloads it, and restarts into it."
            : AboutUpdateNote.Text);

        AboutUpdateBtn.Click += (_, _) => new UpdaterWindow().ShowDialog(this);

        AboutCopyBtn.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard is { } cb)
                {
                    await cb.SetTextAsync(Services.AppInfo.Details());
                    AboutCopyBtn.Content = "Copied";
                    await System.Threading.Tasks.Task.Delay(1400);
                    AboutCopyBtn.Content = "Copy build details";
                }
            }
            catch { /* no clipboard on this platform */ }
        };

        // Launch-time check: quiet, so a version behind a flaky connection says nothing at all. Only the
        // note changes; nothing pops up over the user's work.
        if (canUpdate && Vm is { AutoCheckUpdates: true }) _ = QuietCheck();
    }

    private async System.Threading.Tasks.Task QuietCheck()
    {
        if (await Services.UpdateService.CheckAsync() is not { } f) return;
        AboutUpdateNote.Text = $"Version {f.Version} is available — press “Check for updates” to install it.";
        AboutUpdateBtn.Content = $"Update to {f.Version}";
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
                // SwatchButton: the default theme's hover repaints Background gray, hiding the color.
                // App-level lookup — rebuilt rows may not be attached when this runs.
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("SwatchButton")!,
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

    /// <summary>The "How often" choices → days (0 = off).</summary>
    private static readonly (string Label, int Days)[] BackupIntervals =
    {
        ("Off", 0), ("Daily", 1), ("Weekly", 7), ("Every 2 weeks", 14), ("Monthly", 30),
    };

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

    /// <summary>One slider + live value label bound to a double VM pref (epsilon write-guard). The
    /// slider no longer snaps its thumb to ticks (it glides with the cursor) — round the STORED
    /// value and the label to <paramref name="step"/> here instead.</summary>
    private void WireScaleSlider(Slider slider, TextBlock label, Func<double, string> fmt,
                                 Func<MainViewModel, double> get, Action<MainViewModel, double> set, double step)
    {
        slider.ValueChanged += (_, e) =>
        {
            double v = Math.Round(e.NewValue / step) * step;
            if (Vm is { } vm && Math.Abs(get(vm) - v) > 1e-6) set(vm, v);
            label.Text = fmt(v);
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

    // Which action is currently listening for its new keys (null = none), and its button.
    private (string Action, Button Btn)? _capturing;

    /// <summary>The Shortcuts page: an editable row per rebindable action (Keymap), then the fixed
    /// keyboard reference. Rebuilt on every visit so the buttons always show the live combos.</summary>
    private void BuildShortcutRows()
    {
        ShortcutEditRows.Children.Clear();
        foreach (var (action, label, _) in Services.Keymap.Actions)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var name = new TextBlock
            {
                Text = label, FontSize = 12.5,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var btn = new Button
            {
                Content = Services.Keymap.DisplayFor(action),
                FontSize = 12, MinWidth = 150,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                FontWeight = Services.Keymap.IsDefault(action) ? FontWeight.Normal : FontWeight.SemiBold,
            };
            ToolTip.SetTip(btn, "Click, then press the keys you want");
            var captured = action;
            btn.Click += (_, _) => BeginShortcutCapture(captured, btn);
            Grid.SetColumn(btn, 1);
            row.Children.Add(name);
            row.Children.Add(btn);
            ShortcutEditRows.Children.Add(row);
        }

        if (ShortcutRows.Children.Count > 0) return;               // the fixed table is static
        foreach (var (keys, what) in new[]
        {
            ("Ctrl+A", "Select all"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo"),
            ("Ctrl+C / Ctrl+X / Ctrl+V", "Copy / cut / paste"),
            ("Ctrl+Left / Ctrl+Right", "Jump by word"),
            ("Ctrl+Backspace / Ctrl+Delete", "Delete previous / next word"),
            ("Ctrl+scroll wheel / Ctrl+0", "Zoom the page in and out / back to normal"),
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

    /// <summary>Arm the capture: the next key press becomes the shortcut. Esc keeps the current
    /// combo, Backspace/Delete restores the default. One window-level tunnel handler, unhooked
    /// as soon as the capture resolves.</summary>
    private void BeginShortcutCapture(string action, Button btn)
    {
        if (_capturing is { } prev) EndShortcutCapture(prev.Btn, prev.Action);   // only one at a time
        _capturing = (action, btn);
        btn.Content = "Press keys…";

        void Handler(object? s, KeyEventArgs e)
        {
            e.Handled = true;
            if (e.Key == Key.Escape) { Finish(); return; }
            if (e.Key is Key.Back or Key.Delete)
            {
                Vm?.SetKeyBinding(action, null);                  // back to the default
                Finish();
                return;
            }
            var gesture = Services.Keymap.FromEvent(e);
            if (gesture is null) return;                          // bare modifier / unbindable — keep waiting
            Vm?.SetKeyBinding(action, gesture);
            Finish();
        }

        void Finish()
        {
            RemoveHandler(KeyDownEvent, Handler);
            _capturing = null;
            EndShortcutCapture(btn, action);
        }

        AddHandler(KeyDownEvent, Handler, RoutingStrategies.Tunnel);
    }

    private void EndShortcutCapture(Button btn, string action)
    {
        btn.Content = Services.Keymap.DisplayFor(action);
        btn.FontWeight = Services.Keymap.IsDefault(action) ? FontWeight.Normal : FontWeight.SemiBold;
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

        BackupFolderText.Text = string.IsNullOrEmpty(Vm?.BackupFolder)
            ? "Not set — automatic backups are off." : Vm!.BackupFolder;
        BackupEveryBox.SelectedIndex = System.Math.Max(0,
            System.Array.FindIndex(BackupIntervals, b => b.Days == (Vm?.BackupEveryDays ?? 0)));
        BackupKeepSlider.Value = Vm?.BackupKeep ?? 5;
        BackupKeepValue.Text = (Vm?.BackupKeep ?? 5).ToString();
        LastBackupText.Text = Vm?.LastBackupUtc is { } t2
            ? $"Last backup {t2.ToLocalTime():yyyy-MM-dd HH:mm}." : "Never backed up.";
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
    private bool _fontsScrollSmoothed;

    // ---- font installer (M11): search Google Fonts / Fontshare, download into userdata/fonts ----

    private bool _fontSearchBusy;

    private async System.Threading.Tasks.Task RunFontSearch()
    {
        if (_fontSearchBusy) return;
        var query = FontSearchBox.Text?.Trim() ?? "";
        if (query.Length == 0) return;
        _fontSearchBusy = true;
        FontSearchBtn.IsEnabled = false;
        FontResults.Children.Clear();
        SetFontStatus("Searching…");
        try
        {
            var hits = await FontInstaller.SearchAsync(query);
            FontResults.Children.Clear();
            if (hits.Count == 0)
            {
                SetFontStatus("No matches. Check the spelling, or try another name.");
                return;
            }
            SetFontStatus(null);
            foreach (var hit in hits) FontResults.Children.Add(BuildFontResultRow(hit));
        }
        catch
        {
            SetFontStatus("Couldn't reach the font services. Check your connection and try again.");
        }
        finally
        {
            _fontSearchBusy = false;
            FontSearchBtn.IsEnabled = true;
        }
    }

    private Control BuildFontResultRow(FontInstaller.Hit hit)
    {
        bool already = AppFonts.Installed.Contains(hit.Name, StringComparer.OrdinalIgnoreCase);
        var name = new TextBlock
        {
            Text = hit.Name, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var source = new TextBlock
        {
            Text = hit.Source, FontSize = 11, Foreground = (IBrush)this.FindResource("TextMutedBrush")!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var labels = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        labels.Children.Add(name);
        labels.Children.Add(source);

        var action = new Button
        {
            Theme = (Avalonia.Styling.ControlTheme)this.FindResource(already ? "LumenButtonGray" : "LumenButton")!,
            Content = already ? "Installed" : "Install", FontSize = 12.5, Padding = new Thickness(12, 5),
            IsEnabled = !already, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        action.Click += async (_, _) =>
        {
            action.IsEnabled = false;
            action.Content = "Installing…";
            try
            {
                int files = await FontInstaller.InstallAsync(hit);
                if (files > 0)
                {
                    AppFonts.RegisterInstalled();     // same-key re-register → usable now, no restart
                    action.Content = "Installed";
                    if (FontsPanel.IsVisible) RefreshFontChoices();
                }
                else
                {
                    action.Content = "Install";
                    action.IsEnabled = true;
                    SetFontStatus("That font had no usable files to install.");
                }
            }
            catch
            {
                action.Content = "Install";
                action.IsEnabled = true;
                SetFontStatus("Download failed. Check your connection and try again.");
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(labels);
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return new Border
        {
            Background = (IBrush)this.FindResource("ControlHoverBrush")!,
            CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8),
            Child = grid,
        };
    }

    private void SetFontStatus(string? text)
    {
        FontSearchStatus.Text = text ?? "";
        FontSearchStatus.IsVisible = text is not null;
    }

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

        // The checklist scrolls via the ListBox's own inner ScrollViewer (the outer SmoothScroll
        // deliberately defers to it) — give it the same wheel easing, once it's templated.
        if (!_fontsScrollSmoothed)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (FontsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
                {
                    SmoothScroll.Attach(sv);
                    _fontsScrollSmoothed = true;
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        // Re-anchor the height now that the panel has real content and layout has (or is about to
        // have) run — posted at Background so it happens after the panel becomes visible/measured.
        Avalonia.Threading.Dispatcher.UIThread.Post(SizeFontsList, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Size the fonts checklist to fill the viewport below its actual header (a fixed
    /// guess undershoots as the header wraps/grows) — still bounded, so it keeps virtualizing.</summary>
    private void SizeFontsList()
    {
        if (!FontsPanel.IsVisible) return;
        double top = FontsList.TranslatePoint(default, PrefsScroll)?.Y ?? 200;
        FontsList.Height = Math.Max(240, PrefsScroll.Bounds.Height - top - 22);
    }

    /// <summary>Show one category panel and rise it in (the others hide).</summary>
    private void ShowPanel(string key)
    {
        if (!_panels.TryGetValue(key, out var panel)) return;
        foreach (var (k, p) in _panels) p.IsVisible = k == key;
        Motion.RiseIn(panel, Motion.Fast);
    }

    // ---- settings search (declutter): filter every category at once ------------------------------

    private static readonly Dictionary<string, string> CategoryNames = new()
    {
        ["general"] = "General", ["appearance"] = "Appearance", ["layout"] = "Layout",
        ["canvas"] = "Canvas", ["editor"] = "Editor", ["shortcuts"] = "Shortcuts",
        ["fonts"] = "Fonts", ["bullets"] = "Bullets & numbers", ["data"] = "Data & tools",
    };

    private readonly List<(string Key, Panel Panel, TextBlock Header)> _searchIndex = new();
    private readonly Dictionary<Control, bool> _origVisible = new();
    private bool _searching;
    private bool _searchPrimed;

    /// <summary>Inject a per-panel category heading (shown only during search) and wire the box.</summary>
    private void SetupSettingsSearch()
    {
        foreach (var (key, ctrl) in _panels)
        {
            if (ctrl is not Panel panel) continue;
            var header = new TextBlock { Text = CategoryNames.GetValueOrDefault(key, key), IsVisible = false };
            header.Classes.Add("searchcat");
            panel.Children.Insert(0, header);
            _searchIndex.Add((key, panel, header));
        }
        SearchBox.TextChanged += (_, _) => ApplySearch(SearchBox.Text ?? "");
    }

    /// <summary>Lazily-built panels (shortcuts/fonts/data) must have their rows realized before a
    /// search can match them; build once on first use.</summary>
    private void PrimeSearchPanels()
    {
        if (_searchPrimed) return;
        _searchPrimed = true;
        BuildShortcutRows();
        if (Vm is not null) RefreshFontChoices();
        RefreshDataPanel();
    }

    private void ApplySearch(string raw)
    {
        string q = raw.Trim();
        if (q.Length == 0)
        {
            _searching = false;
            SearchEmptyNote.IsVisible = false;
            foreach (var (c, vis) in _origVisible) c.IsVisible = vis;   // restore every row we touched
            _origVisible.Clear();
            foreach (var (_, _, header) in _searchIndex) header.IsVisible = false;
            if (NavList.SelectedItem is ListBoxItem { Tag: string curKey }) ShowPanel(curKey);
            return;
        }

        if (!_searching) { _searching = true; PrimeSearchPanels(); }

        // Search reaches EVERY category — including Advanced — so any setting stays findable
        // (the nav gate still guards browsing). Owner may prefer gating search too; easy to flip.
        int total = 0;
        foreach (var (_, panel, header) in _searchIndex)
            total += FilterPanel(panel, header, q);
        SearchEmptyNote.IsVisible = total == 0;
        PrefsScroll.Offset = new Avalonia.Vector(0, 0);
    }

    /// <summary>Show only the rows in <paramref name="panel"/> that match; a SECTION header shows
    /// only when a row beneath it (before the next section) matched. Returns the match count; the
    /// panel + its category heading hide entirely when nothing matched.</summary>
    private int FilterPanel(Panel panel, TextBlock catHeader, string q)
    {
        var kids = panel.Children;
        int n = kids.Count, matches = 0;
        var isSection = new bool[n];
        var rowMatched = new bool[n];

        for (int i = 0; i < n; i++)
        {
            var child = kids[i];
            if (ReferenceEquals(child, catHeader)) continue;
            if (child is TextBlock { } tb && tb.Classes.Contains("section")) { isSection[i] = true; continue; }
            if (!OrigVisible(child)) { SetSearchVis(child, false); continue; }  // never reveal a designed-hidden row
            bool m = MatchesQuery(child, q);
            rowMatched[i] = m;
            SetSearchVis(child, m);
            if (m) matches++;
        }
        for (int i = 0; i < n; i++)
        {
            if (!isSection[i]) continue;
            bool any = false;
            for (int j = i + 1; j < n && !isSection[j]; j++)
                if (rowMatched[j]) { any = true; break; }
            SetSearchVis(kids[i], any);
        }
        catHeader.IsVisible = matches > 0;
        panel.IsVisible = matches > 0;
        return matches;
    }

    private bool OrigVisible(Control c)
    {
        if (!_origVisible.TryGetValue(c, out var v)) { v = c.IsVisible; _origVisible[c] = v; }
        return v;
    }

    private void SetSearchVis(Control c, bool vis)
    {
        if (!_origVisible.ContainsKey(c)) _origVisible[c] = c.IsVisible;
        c.IsVisible = vis;
    }

    private static bool MatchesQuery(Control block, string q)
    {
        if (TipContains(block, q)) return true;
        foreach (var d in block.GetLogicalDescendants())
        {
            if (d is TextBlock tb && tb.Text is { } t && t.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (d is Control c && TipContains(c, q)) return true;
        }
        return false;
    }

    private static bool TipContains(Control c, string q) =>
        ToolTip.GetTip(c) is string tip && tip.Contains(q, StringComparison.OrdinalIgnoreCase);

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
        RoundnessSlider.Value = vm.CornerRoundness;
        RoundnessValue.Text = $"{(int)Math.Round(vm.CornerRoundness * 100)}%";
        MacMaterialBox.SelectedIndex = Math.Clamp(vm.MacGlassMaterial, 0, 4);
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
        // The combo only offers None/Ruled/Grid/Dots; map legacy stored values ("Lines"/"Blank")
        // and anything unrecognized to their display equivalent so the picker always shows something valid.
        PageGridBox.SelectedItem = vm.PageGrid switch
        {
            "Lines" => "Grid",
            "Blank" => "None",
            "None" or "Ruled" or "Grid" or "Dots" => vm.PageGrid,
            _ => "None",
        };
        TidyLayoutBox.SelectedItem = vm.MindmapTidyLayout switch
        {
            "Hybrid" => "Hybrid",
            "TopDown" => "Top-down",
            _ => "Radial",
        };
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
