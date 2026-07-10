using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
                bool ok = await ConfirmDialog.Show(this, "Unlock advanced settings?",
                    "Advanced settings change how notes are stored, exported, and rendered. " +
                    "They're meant for power users — the defaults are right for most people.",
                    "Unlock", danger: false);
                if (!ok) { NavList.SelectedItem = _lastNav; _navGuard = false; return; }
                vm.AdvancedUnlocked = true;
                _navGuard = false;
                UpdateGateVisuals();
            }
            _lastNav = NavList.SelectedItem;
            if (key == "data") RefreshDataPanel();
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
    }

    private bool _navGuard;
    private object? _lastNav;

    /// <summary>Categories behind the Advanced confirmation ("bullets"/"fonts" arrive in later parts).</summary>
    private static bool IsGated(string key) => key is "data" or "bullets" or "fonts";

    /// <summary>Locked = the small padlock shows on the ADVANCED group header.</summary>
    private void UpdateGateVisuals() => GateLock.IsVisible = Vm is not { AdvancedUnlocked: true };

    /// <summary>Fill the Data &amp; tools facts (folder path + size) when the panel shows.</summary>
    private void RefreshDataPanel()
    {
        var dir = Vm?.SettingsDir;
        DataFolderText.Text = dir ?? "—";
        WorkspaceSizeText.Text = dir is null ? "—" : FolderSize(dir);
    }

    private static string FolderSize(string dir)
    {
        try
        {
            long b = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            return b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";
        }
        catch { return "—"; }
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
        UpdateGateVisuals();
    }
}
