# Advanced Preferences — Part 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the Preferences window into a left-nav categorized surface, add the Advanced
confirmation gate with a working Data & tools category, and land the Appearance additions (custom
accent, glass tint, motion controls).

**Architecture:** The window becomes a nav `ListBox` (category items keyed by `Tag`) + a keyed panel
dictionary swapped in code-behind with `Motion.RiseIn`. Gated categories intercept nav selection with
a `ConfirmDialog` before unlocking (`AppSettings.AdvancedUnlocked`). New knobs follow the established
pattern: `AppSettings` field → `MainViewModel` `[ObservableProperty]` + `OnXChanged → Save` →
consumer applies (MainWindow for theme tokens, MainView for veil/motion statics).

**Tech Stack:** Avalonia 12.0.4 / .NET 10, CommunityToolkit.Mvvm, xUnit. No web components.

**Covers spec phases 1–3** of `docs/superpowers/specs/2026-07-09-advanced-preferences-design.md`,
plus two small pulls-forward noted there but unphased: the Layout start-visible toggles, and the
cheap Data & tools rows (open folder / size / reset) moved from phase 7 into the gate task so
unlocking reveals something real. Phases 4–8 (bullets & numbers, fonts, editor defaults,
export/hash, optional roundness) get their own plan docs after the owner reviews this shell.

**Known facts (verified against the codebase):**
- `AppSettings` has dead scaffold fields `AccentColor` ("#4DA6FF") and `BlurStrength` (0.6) — Tasks
  5/6 repurpose them as `CustomAccent` (null = theme default) and `GlassTint` (0). The existing
  `AppSettingsTests.SaveThenLoad_RoundTripsValues` references both and MUST be updated in the same
  tasks. Old JSON keys deserialize-ignore harmlessly.
- Theme tokens are applied in `MainWindow.ApplyTheme()` (MainWindow.axaml.cs:38) on
  Theme/FullTheme/PaperLight VM changes — the accent override hooks there.
- MainView's root is `<Grid RowDefinitions="38,*">` (MainView.axaml:177) — the glass-tint veil goes
  in as its FIRST child (bottom z-order) with `Grid.RowSpan="2"`.
- `{StaticResource IconFont}` = "Segoe Fluent Icons, Segoe MDL2 Assets" (Theme.axaml:46). The only
  glyph this plan uses is `&#xE72E;` (Lock).
- `ConfirmDialog.Show(Window owner, string title, string message, string confirmText = "Delete",
  string cancelText = "Cancel")` has a hardcoded RED confirm button — Task 3 adds a trailing
  `bool danger = true` param (backward compatible) with an accent-gradient variant.
- Build gotcha: the running app locks the exe. **Always** `taskkill //F //IM Lumenotepad.exe` before
  building. Pointer/hover/compositor behavior can NOT be verified headlessly — relaunch the real app.

---

## Task 1: Left-nav shell restructure (no behavior change)

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` (full rewrite below)
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml.cs` (full rewrite below)

- [ ] **Step 1: Rewrite the window XAML**

Replace the entire contents of `src/Lumenotepad/Views/PreferencesWindow.axaml` with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Lumenotepad.ViewModels"
        x:Class="Lumenotepad.Views.PreferencesWindow"
        x:DataType="vm:MainViewModel"
        Title="Preferences"
        Width="640" Height="500" CanResize="False"
        WindowDecorations="None" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        FontFamily="{StaticResource UiFont}"
        Foreground="{DynamicResource TextPrimaryBrush}"
        TextOptions.TextRenderingMode="Antialias"
        Background="{DynamicResource WindowBackgroundBrush}">

    <Window.Styles>
        <Style Selector="TextBlock.section">
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="Margin" Value="0,14,0,2"/>
        </Style>
        <Style Selector="TextBlock.hint">
            <Setter Property="FontSize" Value="11.5"/>
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="TextWrapping" Value="Wrap"/>
        </Style>
        <Style Selector="TextBlock.label">
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
        <Style Selector="ToggleSwitch">
            <Setter Property="OnContent" Value="{x:Null}"/>
            <Setter Property="OffContent" Value="{x:Null}"/>
            <Setter Property="MinWidth" Value="0"/>
        </Style>
        <!-- theme picker chips -->
        <Style Selector="ListBox.themepick ListBoxItem">
            <Setter Property="Padding" Value="10,6"/>
            <Setter Property="Margin" Value="0,0,6,6"/>
            <Setter Property="CornerRadius" Value="9"/>
        </Style>
        <!-- left category nav -->
        <Style Selector="ListBox.prefnav ListBoxItem">
            <Setter Property="CornerRadius" Value="9"/>
            <Setter Property="Padding" Value="10,7"/>
            <Setter Property="Margin" Value="0,1"/>
        </Style>
        <Style Selector="ListBox.prefnav ListBoxItem /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="CornerRadius" Value="9"/>
        </Style>
        <Style Selector="ListBox.prefnav ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="{DynamicResource ControlHoverBrush}"/>
        </Style>
        <Style Selector="ListBox.prefnav ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}"/>
        </Style>
        <Style Selector="ListBox.prefnav ListBoxItem:selected:pointerover /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}"/>
        </Style>
    </Window.Styles>

    <DockPanel>
        <!-- mini title bar -->
        <Grid DockPanel.Dock="Top" Height="36" ColumnDefinitions="*,Auto" x:Name="PrefsTitleBar"
              Background="Transparent">
            <TextBlock Text="Preferences" FontSize="13" FontWeight="SemiBold"
                       VerticalAlignment="Center" Margin="16,0,0,0"/>
            <Button x:Name="CloseBtn" Grid.Column="1" Theme="{StaticResource CloseCaptionButton}" Content="&#xE8BB;"/>
        </Grid>

        <Grid ColumnDefinitions="158,*">

            <!-- category nav -->
            <Border Grid.Column="0" BorderBrush="{DynamicResource FrameBorderBrush}"
                    BorderThickness="0,0,1,0" Padding="8,4">
                <ListBox x:Name="NavList" Classes="prefnav" Background="Transparent"
                         BorderThickness="0" Padding="0">
                    <ListBoxItem Tag="appearance"><TextBlock Classes="label" Text="Appearance"/></ListBoxItem>
                    <ListBoxItem Tag="layout"><TextBlock Classes="label" Text="Layout"/></ListBoxItem>
                    <ListBoxItem Tag="canvas"><TextBlock Classes="label" Text="Canvas"/></ListBoxItem>
                </ListBox>
            </Border>

            <!-- category panels (one visible at a time; ShowPanel swaps + animates) -->
            <ScrollViewer Grid.Column="1" HorizontalScrollBarVisibility="Disabled">
                <Panel Margin="20,4,20,18">

                    <StackPanel x:Name="AppearancePanel" Spacing="6">
                        <TextBlock Classes="section" Text="THEME" Margin="0,4,0,2"/>
                        <ListBox x:Name="ThemeList" Classes="themepick" Background="Transparent" BorderThickness="0" Padding="0">
                            <ListBox.ItemsPanel>
                                <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                            </ListBox.ItemsPanel>
                        </ListBox>

                        <Grid ColumnDefinitions="*,Auto" Margin="0,4,0,0">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Full theme"/>
                                <TextBlock Classes="hint" Text="The page canvas matches the frame material instead of contrasting it."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding FullTheme, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <Grid ColumnDefinitions="*,Auto" IsEnabled="{Binding PaperToggleEnabled}">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Light paper"/>
                                <TextBlock Classes="hint" Text="Lumen with Full theme off: write on light paper instead of dark."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding PaperLight, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Flat covers"/>
                                <TextBlock Classes="hint" Text="Notebook covers use their plain color — no gloss, shadows stay."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding FlatCovers, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Glossy accents"/>
                                <TextBlock Classes="hint" Text="Selected tabs, rows and homepage chips get the top-lit gloss."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding GlossyAccents, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Extended font list"/>
                                <TextBlock Classes="hint" Text="Offer every installed font in the toolbar's font menu instead of the essentials."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ExtendedFonts, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>

                    <StackPanel x:Name="LayoutPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="TOOLBAR" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Toolbar position"/>
                            <ComboBox x:Name="ToolbarPosBox" Grid.Column="1" Width="120"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Attach toolbar to"/>
                                <TextBlock Classes="hint" Text="The window edge, or the page box itself."/>
                            </StackPanel>
                            <ComboBox x:Name="ToolbarScopeBox" Grid.Column="1" Width="120" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>

                    <StackPanel x:Name="CanvasPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="PAGES" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Resizable pages"/>
                                <TextBlock Classes="hint" Text="Note containers show width, height and corner resize handles."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ResizablePages, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Deleted pages history"/>
                                <TextBlock Classes="hint" Text="Deleted containers are kept per page and can be dragged back."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding DeletedHistory, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>

                </Panel>
            </ScrollViewer>
        </Grid>
    </DockPanel>
</Window>
```

Notes: the old bottom hint ("Advanced settings … arrive in a later update") is gone — Task 3 ships
the real gate. `Height=500` is fixed; the right side scrolls if a panel outgrows it.

- [ ] **Step 2: Rewrite the code-behind**

Replace the entire contents of `src/Lumenotepad/Views/PreferencesWindow.axaml.cs` with:

```csharp
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
```

- [ ] **Step 3: Build + full test suite**

Run: `cd /e/CLAUDE/Lumenotepad && (taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: build succeeds, all 69 tests PASS (pure restructure — nothing behavioral changed).

- [ ] **Step 4: Commit**

```bash
git add src/Lumenotepad/Views/PreferencesWindow.axaml src/Lumenotepad/Views/PreferencesWindow.axaml.cs
git commit -m "feat(m7): preferences left-nav shell — Appearance/Layout/Canvas categories, animated panel swap"
```

---

## Task 2: Layout start-visible toggles

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` (LayoutPanel)
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/Lumenotepad.Tests/AppSettingsTests.cs` (inside the class):

```csharp
    [Fact]
    public void StartVisible_DefaultsTrue_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.True(new AppSettings().StartRailVisible);
            Assert.True(new AppSettings().StartPagesVisible);

            var s = new AppSettings { StartRailVisible = false, StartPagesVisible = false };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.False(loaded.StartRailVisible);
            Assert.False(loaded.StartPagesVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter StartVisible_DefaultsTrue_AndRoundTrips -v q --nologo`
Expected: FAIL to compile — `AppSettings` has no `StartRailVisible`.

- [ ] **Step 3: Add the settings fields**

In `src/Lumenotepad/Services/AppSettings.cs`, after the `DeletedHistory` line add:

```csharp
    public bool StartRailVisible { get; set; } = true;      // notebooks rail shown at launch
    public bool StartPagesVisible { get; set; } = true;     // pages panel shown at launch
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter StartVisible_DefaultsTrue_AndRoundTrips -v q --nologo`
Expected: PASS.

- [ ] **Step 5: VM plumbing**

In `src/Lumenotepad/ViewModels/MainViewModel.cs`:

After the `_extendedFonts` observable field add:

```csharp
    [ObservableProperty] private bool _startRailVisible = true;    // prefs: rail shown at launch
    [ObservableProperty] private bool _startPagesVisible = true;   // prefs: pages panel shown at launch
```

In the constructor's settings-load block (after `ExtendedFonts = _settings.ExtendedFonts;`):

```csharp
            StartRailVisible = _settings.StartRailVisible;
            StartPagesVisible = _settings.StartPagesVisible;
            IsRailVisible = _settings.StartRailVisible;
            IsPagesVisible = _settings.StartPagesVisible;
```

After `OnExtendedFontsChanged` add (the pref also applies to the live window immediately;
runtime rail toggles stay transient and do NOT write back):

```csharp
    partial void OnStartRailVisibleChanged(bool value)
    {
        IsRailVisible = value;
        if (_settings is null || _settingsDir is null) return;
        _settings.StartRailVisible = value;
        _settings.Save(_settingsDir);
    }

    partial void OnStartPagesVisibleChanged(bool value)
    {
        IsPagesVisible = value;
        if (_settings is null || _settingsDir is null) return;
        _settings.StartPagesVisible = value;
        _settings.Save(_settingsDir);
    }
```

- [ ] **Step 6: Prefs rows**

In `PreferencesWindow.axaml`, append inside `LayoutPanel` (after the "Attach toolbar to" Grid):

```xml
                        <TextBlock Classes="section" Text="PANELS"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Show notebooks rail"/>
                                <TextBlock Classes="hint" Text="The rail starts visible; the title-bar toggle still hides it per session."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding StartRailVisible, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Show pages panel"/>
                                <TextBlock Classes="hint" Text="The pages list starts visible; the title-bar toggle still hides it per session."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding StartPagesVisible, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
```

- [ ] **Step 7: Build + full suite + commit**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: all tests PASS (70 now).

```bash
git add -A src tests
git commit -m "feat(m7): Layout prefs — rail/pages start-visible toggles (persisted, apply live)"
```

---

## Task 3: Advanced gate + Data & tools category

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/ConfirmDialog.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test**

Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void AdvancedUnlocked_DefaultsFalse_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().AdvancedUnlocked);
            var s = new AppSettings { AdvancedUnlocked = true };
            s.Save(dir);
            Assert.True(AppSettings.Load(dir).AdvancedUnlocked);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter AdvancedUnlocked_DefaultsFalse_AndRoundTrips -v q --nologo`
Expected: FAIL to compile.

- [ ] **Step 3: Settings field + VM property**

`AppSettings.cs` — after `StartPagesVisible`:

```csharp
    public bool AdvancedUnlocked { get; set; }              // advanced prefs gate accepted
```

`MainViewModel.cs` — after `_startPagesVisible` field:

```csharp
    [ObservableProperty] private bool _advancedUnlocked;            // prefs: advanced gate accepted
```

Constructor load block: `AdvancedUnlocked = _settings.AdvancedUnlocked;`

After `OnStartPagesVisibleChanged`:

```csharp
    partial void OnAdvancedUnlockedChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AdvancedUnlocked = value;
        _settings.Save(_settingsDir);
    }
```

Also add near `Save()` (the view needs the folder for "Open data folder" / size):

```csharp
    /// <summary>The portable userdata folder backing this workspace (null in the designer).</summary>
    public string? SettingsDir => _settingsDir;
```

And the reset method (extended by Tasks 5–7 as their fields land):

```csharp
    /// <summary>Reset every preference to its default (notebooks and notes untouched). Each setter
    /// persists and re-applies through its own OnChanged hook.</summary>
    public void ResetSettingsToDefaults()
    {
        var d = new AppSettings();
        Theme = d.Theme; FullTheme = d.FullTheme; PaperLight = d.PaperLight;
        FlatCovers = d.FlatCovers; GlossyAccents = d.GlossyAccents; ExtendedFonts = d.ExtendedFonts;
        ToolbarPosition = d.ToolbarPosition; ToolbarScope = d.ToolbarScope;
        ResizablePages = d.ResizablePages; DeletedHistory = d.DeletedHistory;
        StartRailVisible = d.StartRailVisible; StartPagesVisible = d.StartPagesVisible;
        AdvancedUnlocked = d.AdvancedUnlocked;
    }
```

- [ ] **Step 4: VM reset test**

Add to `MainViewModelTests.cs` (match the file's existing temp-dir construction pattern):

```csharp
    [Fact]
    public void ResetSettingsToDefaults_RestoresDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.Theme = "Pink";
            vm.FlatCovers = true;
            vm.AdvancedUnlocked = true;
            vm.StartRailVisible = false;

            vm.ResetSettingsToDefaults();

            Assert.Equal("Lumen", vm.Theme);
            Assert.False(vm.FlatCovers);
            Assert.False(vm.AdvancedUnlocked);
            Assert.True(vm.StartRailVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Run: `dotnet test --filter ResetSettingsToDefaults_RestoresDefaults -v q --nologo` → PASS
(and the Step 1 test now passes too).

- [ ] **Step 5: ConfirmDialog accent variant**

In `src/Lumenotepad/Views/ConfirmDialog.cs`, change the signature to:

```csharp
    public static async Task<bool> Show(Window owner, string title, string message,
                                        string confirmText = "Delete", string cancelText = "Cancel",
                                        bool danger = true)
```

and replace the `confirm` construction line with:

```csharp
        var t = Services.ThemeManager.Current;
        var confirm = danger
            ? MakeButton(confirmText, "#D64258", "#A62A3C", "#7E1F2D")                  // destructive red
            : MakeButton(confirmText, t.AccentGradTop, t.AccentGradBottom, t.AccentDeep); // affirmative accent
```

(Existing call sites pass positional args only — fully backward compatible.)

- [ ] **Step 6: Nav group + Data & tools panel XAML**

In `PreferencesWindow.axaml`, append inside `NavList` after the "canvas" item:

```xml
                    <ListBoxItem IsEnabled="False" Margin="0,10,0,0" Padding="10,2">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <TextBlock x:Name="GateLock" Text="&#xE72E;" FontFamily="{StaticResource IconFont}"
                                       FontSize="10" Foreground="{DynamicResource TextMutedBrush}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Text="ADVANCED" FontSize="10.5" FontWeight="SemiBold" LetterSpacing="1"
                                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center"/>
                        </StackPanel>
                    </ListBoxItem>
                    <ListBoxItem Tag="data"><TextBlock Classes="label" Text="Data &amp; tools"/></ListBoxItem>
```

Append inside the panels `Panel` after `CanvasPanel`:

```xml
                    <StackPanel x:Name="DataPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="STORAGE" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Data folder"/>
                                <TextBlock x:Name="DataFolderText" Classes="hint" Text="—"/>
                            </StackPanel>
                            <Button x:Name="OpenDataBtn" Grid.Column="1" Content="Open" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Workspace size"/>
                            <TextBlock x:Name="WorkspaceSizeText" Grid.Column="1" Classes="label" Text="—"/>
                        </Grid>

                        <TextBlock Classes="section" Text="MAINTENANCE"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Reset settings"/>
                                <TextBlock Classes="hint" Text="Every preference returns to its default. Notebooks and notes stay."/>
                            </StackPanel>
                            <Button x:Name="ResetBtn" Grid.Column="1" Content="Reset…" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto" Margin="0,10,0,0">
                            <StackPanel Spacing="2" VerticalAlignment="Center" Margin="0,0,10,0">
                                <TextBlock Classes="label" Text="Lock advanced settings"/>
                                <TextBlock Classes="hint" Text="Hide the advanced categories behind the confirmation again."/>
                            </StackPanel>
                            <Button x:Name="RelockBtn" Grid.Column="1" Content="Lock" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>
```

- [ ] **Step 7: Gate flow in code-behind**

In `PreferencesWindow.axaml.cs`:

Add usings: `using System;`, `using System.ComponentModel;`, `using System.IO;`, `using System.Linq;`.

Register the panel — in the `_panels` initializer add: `["data"] = DataPanel,`

REPLACE the Task-1 `NavList.SelectionChanged` wiring (both the handler and the
`NavList.SelectedIndex = 0;` line) with:

```csharp
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
```

Add the fields + helpers to the class:

```csharp
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
```

And call `UpdateGateVisuals();` at the end of `SyncFromVm()`.

- [ ] **Step 8: Build + full suite + commit**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: all tests PASS (72 now).

```bash
git add -A src tests
git commit -m "feat(m7): advanced gate — confirm-to-unlock, Data & tools (open folder, size, reset, re-lock)"
```

---

## Task 4: ThemePalettes.WithAccent + NormalizeHex (pure, TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/ThemePalettes.cs`
- Test: `tests/Lumenotepad.Tests/ThemePalettesTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ThemePalettesTests.cs`:

```csharp
    [Fact]
    public void WithAccent_RecomputesEveryAccentDerivedToken()
    {
        var t = ThemePalettes.Resolve("Lumen", false, false);
        var seeded = ThemePalettes.WithAccent(t, "#E27BA6");

        Assert.Equal("#E27BA6", seeded.Accent);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", 0.15), seeded.AccentHover);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x38), seeded.AccentSoft);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", -0.28), seeded.AccentDeep);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", 0.12), seeded.AccentGradTop);
        Assert.Equal(ThemePalettes.Shade("#E27BA6", -0.10), seeded.AccentGradBottom);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x55), seeded.FieldSelection);
        Assert.Equal(ThemePalettes.Alpha("#E27BA6", 0x4D), seeded.NoteChromeFocus);
        // non-accent tokens untouched
        Assert.Equal(t.FrameBackground, seeded.FrameBackground);
        Assert.Equal(t.PaperBackground, seeded.PaperBackground);
        Assert.Equal(t.TextPrimary, seeded.TextPrimary);
    }

    [Theory]
    [InlineData("4da6ff", "#4DA6FF")]
    [InlineData("#4DA6FF", "#4DA6FF")]
    [InlineData("  #e27ba6 ", "#E27BA6")]
    public void NormalizeHex_AcceptsSixHexDigits(string input, string expected) =>
        Assert.Equal(expected, ThemePalettes.NormalizeHex(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyzxyz")]
    [InlineData("#4DA6FF00")]
    [InlineData("#4DA")]
    public void NormalizeHex_RejectsInvalid(string? input) =>
        Assert.Null(ThemePalettes.NormalizeHex(input));
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "WithAccent_RecomputesEveryAccentDerivedToken|NormalizeHex" -v q --nologo`
Expected: FAIL to compile — no `WithAccent`/`NormalizeHex`.

- [ ] **Step 3: Implement**

In `ThemePalettes.cs`, before the "tiny color math" section add:

```csharp
    /// <summary>Recompute every accent-derived token from a new seed color — the custom-accent
    /// preference. Pure: same Shade/Alpha math the palettes use, everything else untouched.</summary>
    public static ThemeTokens WithAccent(ThemeTokens t, string seed) => t with
    {
        Accent = seed,
        AccentHover = Shade(seed, 0.15),
        AccentSoft = Alpha(seed, 0x38),
        AccentDeep = Shade(seed, -0.28),
        AccentGradTop = Shade(seed, 0.12),
        AccentGradBottom = Shade(seed, -0.10),
        FieldSelection = Alpha(seed, 0x55),
        NoteChromeFocus = Alpha(seed, 0x4D),
    };

    /// <summary>Normalize user hex input ("4da6ff", " #4DA6FF ") to "#RRGGBB"; null when invalid.</summary>
    public static string? NormalizeHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().TrimStart('#');
        if (t.Length != 6) return null;
        foreach (char ch in t)
            if (!Uri.IsHexDigit(ch)) return null;
        return "#" + t.ToUpperInvariant();
    }
```

- [ ] **Step 4: Run to verify pass, then full suite**

Run: `dotnet test -v q --nologo`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Services/ThemePalettes.cs tests/Lumenotepad.Tests/ThemePalettesTests.cs
git commit -m "feat(m7): ThemePalettes.WithAccent + NormalizeHex — pure accent reseeding"
```

---

## Task 5: Custom accent end-to-end

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs` (replace dead `AccentColor`)
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/MainWindow.axaml.cs` (apply-site)
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs` (canvas rebuild on accent change)
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs` (picker UI)
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Repurpose the settings field (and fix the existing test)**

In `AppSettings.cs` REPLACE the line
`public string AccentColor { get; set; } = "#4DA6FF";` with:

```csharp
    public string? CustomAccent { get; set; }               // accent override; null = theme's own
```

In `AppSettingsTests.cs` `SaveThenLoad_RoundTripsValues`, replace `AccentColor = "#4DA6FF"` with
`CustomAccent = "#E27BA6"` in the initializer and the matching assert with
`Assert.Equal("#E27BA6", loaded.CustomAccent);`.

Run: `dotnet test --filter SaveThenLoad_RoundTripsValues -v q --nologo` → PASS.

- [ ] **Step 2: VM property**

`MainViewModel.cs` — after `_advancedUnlocked`:

```csharp
    [ObservableProperty] private string? _customAccent;             // prefs: accent override; null = theme's own
```

Constructor load block: `CustomAccent = _settings.CustomAccent;`

After `OnAdvancedUnlockedChanged`:

```csharp
    partial void OnCustomAccentChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CustomAccent = value;
        _settings.Save(_settingsDir);
    }
```

In `ResetSettingsToDefaults()` add: `CustomAccent = d.CustomAccent;`

- [ ] **Step 3: Apply-site in MainWindow**

In `MainWindow.axaml.cs` `OnThemePropertyChanged`, extend the filter:

```csharp
        if (e.PropertyName is nameof(ViewModels.MainViewModel.Theme)
            or nameof(ViewModels.MainViewModel.FullTheme)
            or nameof(ViewModels.MainViewModel.PaperLight)
            or nameof(ViewModels.MainViewModel.CustomAccent))
            ApplyTheme();
```

In `ApplyTheme()`, replace the `ThemeManager.Apply` line with:

```csharp
        var tokens = Services.ThemePalettes.Resolve(vm.Theme, vm.FullTheme, vm.PaperLight);
        if (Services.ThemePalettes.NormalizeHex(vm.CustomAccent) is { } accent)
            tokens = Services.ThemePalettes.WithAccent(tokens, accent);
        Services.ThemeManager.Apply(app, tokens);
```

- [ ] **Step 4: Canvas rebuild in MainView**

In `MainView.axaml.cs` `OnVmPropertyChanged`, extend the theme-rebuild branch (currently
`Theme or FullTheme or PaperLight`, around line 610) to include the accent:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.Theme)
                 or nameof(MainViewModel.FullTheme) or nameof(MainViewModel.PaperLight)
                 or nameof(MainViewModel.CustomAccent))
```

(The branch body — canvas rebuild + trash refresh + soft fade — is already right.)

- [ ] **Step 5: Picker UI**

In `PreferencesWindow.axaml`, append inside `AppearancePanel` (after the "Extended font list" Grid):

```xml
                        <TextBlock Classes="section" Text="ACCENT"/>
                        <WrapPanel x:Name="AccentSwatches"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="hint" VerticalAlignment="Center" Margin="0,0,10,0"
                                       Text="Custom hex — Enter applies; blank returns to the theme's own accent."/>
                            <TextBox x:Name="AccentHexBox" Grid.Column="1" Width="110" FontSize="12.5"
                                     PlaceholderText="#RRGGBB" VerticalAlignment="Center"/>
                        </Grid>
```

In `PreferencesWindow.axaml.cs`:

Add using: `using Avalonia.Media;` and `using Avalonia;`.

In the constructor (after the Relock wiring), add:

```csharp
        AccentHexBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Vm is not { } vm) return;
            if (string.IsNullOrWhiteSpace(AccentHexBox.Text)) vm.CustomAccent = null;
            else if (ThemePalettes.NormalizeHex(AccentHexBox.Text) is { } norm) vm.CustomAccent = norm;
            AccentHexBox.Text = vm.CustomAccent ?? "";
        };
        DataContextChanged += (_, _) => HookVmChanges();
        HookVmChanges();
```

Add to the class:

```csharp
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
        if (e.PropertyName == nameof(MainViewModel.CustomAccent)) BuildAccentSwatches();
        else if (e.PropertyName == nameof(MainViewModel.AdvancedUnlocked)) UpdateGateVisuals();
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
```

In `SyncFromVm()` add at the end:

```csharp
        AccentHexBox.Text = vm.CustomAccent ?? "";
        BuildAccentSwatches();
```

- [ ] **Step 6: Build + full suite + commit**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: all PASS.

```bash
git add -A src tests
git commit -m "feat(m7): custom accent — swatches + hex in prefs, WithAccent applied through the theme engine"
```

---

## Task 6: Glass tint

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs` (replace dead `BlurStrength`)
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml` (veil element) + `.axaml.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs` (slider)
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Repurpose the settings field (and fix the existing test)**

In `AppSettings.cs` REPLACE `public double BlurStrength { get; set; } = 0.6;` with:

```csharp
    public double GlassTint { get; set; }                   // -1..1: darken / lighten the glass; 0 = off
```

In `AppSettingsTests.cs` `SaveThenLoad_RoundTripsValues`, replace `BlurStrength = 0.7` with
`GlassTint = 0.4` and the assert with `Assert.Equal(0.4, loaded.GlassTint, 3);`.

Run: `dotnet test --filter SaveThenLoad_RoundTripsValues -v q --nologo` → PASS.

- [ ] **Step 2: VM property**

`MainViewModel.cs` — after `_customAccent`:

```csharp
    [ObservableProperty] private double _glassTint;                 // prefs: -1..1 glass veil; 0 = off
```

Constructor load: `GlassTint = _settings.GlassTint;`
Changed hook (after `OnCustomAccentChanged`):

```csharp
    partial void OnGlassTintChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.GlassTint = value;
        _settings.Save(_settingsDir);
    }
```

`ResetSettingsToDefaults()` add: `GlassTint = d.GlassTint;`

- [ ] **Step 3: The veil in MainView**

In `MainView.axaml`, immediately after `<Grid RowDefinitions="38,*">` (BEFORE the title bar), insert:

```xml
        <!-- Glass tint: a veil UNDER all content that lightens/darkens what the acrylic shows
             through (prefs slider). First child = bottom of z-order; hidden on solid themes. -->
        <Border x:Name="GlassTintVeil" Grid.RowSpan="2" IsVisible="False" IsHitTestVisible="False"/>
```

In `MainView.axaml.cs`:

Add to `HookVm()`'s init chain (after `ApplyPanels();`): `ApplyGlassTint();`

Add the method (near `ApplyGlossyAccents`):

```csharp
    /// <summary>"Glass tint": white/black veil under all content, tinting whatever the acrylic
    /// backdrop shows through. Hidden entirely on solid (non-glass) themes and at zero.</summary>
    private void ApplyGlassTint()
    {
        if (Vm is not { } vm) return;
        double t = Math.Clamp(vm.GlassTint, -1, 1);
        bool on = Services.ThemeManager.Current.GlassWindow && Math.Abs(t) > 0.01;
        GlassTintVeil.IsVisible = on;
        if (on) GlassTintVeil.Background =
            new SolidColorBrush(t >= 0 ? Colors.White : Colors.Black, Math.Abs(t) * 0.35);
    }
```

In `OnVmPropertyChanged` add a branch (before the theme-rebuild branch):

```csharp
        else if (e.PropertyName == nameof(MainViewModel.GlassTint))
            ApplyGlassTint();
```

And INSIDE the theme-rebuild branch body (Theme/FullTheme/PaperLight/CustomAccent), append —
posted because MainWindow's own handler updates `ThemeManager.Current` and subscription order
between the two windows isn't guaranteed:

```csharp
            Dispatcher.UIThread.Post(ApplyGlassTint, DispatcherPriority.Background);
```

- [ ] **Step 4: The slider in prefs**

In `PreferencesWindow.axaml`, append inside `AppearancePanel` (after the ACCENT rows):

```xml
                        <TextBlock Classes="section" Text="GLASS"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Glass tint"/>
                            <TextBlock x:Name="GlassTintValue" Grid.Column="1" Classes="hint" Text="0%"/>
                        </Grid>
                        <Slider x:Name="GlassTintSlider" Minimum="-1" Maximum="1"
                                TickFrequency="0.05" IsSnapToTickEnabled="True"/>
                        <TextBlock Classes="hint"
                                   Text="Darken or lighten what shows through the glass. Zero is untinted; solid themes ignore it."/>
```

In `PreferencesWindow.axaml.cs` constructor (after the accent wiring):

```csharp
        GlassTintSlider.ValueChanged += (_, e) =>
        {
            if (Vm is { } vm && Math.Abs(vm.GlassTint - e.NewValue) > 1e-6) vm.GlassTint = e.NewValue;
            GlassTintValue.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
        };
```

In `SyncFromVm()` add:

```csharp
        GlassTintSlider.Value = vm.GlassTint;
        GlassTintValue.Text = $"{(int)Math.Round(vm.GlassTint * 100)}%";
```

- [ ] **Step 5: Build + full suite + commit**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: all PASS.

```bash
git add -A src tests
git commit -m "feat(m7): glass tint — darken/lighten veil under the acrylic, prefs slider"
```

---

## Task 7: Motion preferences (reduce motion + speed)

**Files:**
- Modify: `src/Lumenotepad/Views/Motion.cs`
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`
- Test: `tests/Lumenotepad.Tests/MotionTests.cs`, `tests/Lumenotepad.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Failing Motion test**

Add to `MotionTests.cs`:

```csharp
    [Fact]
    public void Ms_ScalesWithSpeedScale()
    {
        var old = Motion.SpeedScale;
        try
        {
            Motion.SpeedScale = 1.4;
            Assert.Equal(308, Motion.Ms(220));
            Motion.SpeedScale = 0.6;
            Assert.Equal(132, Motion.Ms(220));
            Motion.SpeedScale = 1.0;
            Assert.Equal(220, Motion.Ms(220));
        }
        finally { Motion.SpeedScale = old; }
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter Ms_ScalesWithSpeedScale -v q --nologo`
Expected: FAIL to compile — no `SpeedScale`/`Ms`.

- [ ] **Step 3: Motion engine additions**

In `Motion.cs`, after the `Fast/Base/Slow/Rise` constants add:

```csharp
    /// <summary>Prefs: master motion switch + global speed. When disabled every tween snaps
    /// straight to its final frame (and still fires onDone) so callers never special-case it.</summary>
    public static bool Enabled { get; set; } = true;
    public static double SpeedScale { get; set; } = 1.0;    // Calm 1.4 / Normal 1.0 / Snappy 0.6
    public static int Ms(int ms) => Math.Max(1, (int)Math.Round(ms * SpeedScale));
```

In `Tween`, right after `ease ??= EaseOut;` insert the short-circuit, and scale the step count:

```csharp
        if (!Enabled)
        {
            c.RenderTransform = Make(tx, ty, ts);
            bool restNow = Math.Abs(ts - 1) < 1e-3 && Math.Abs(tx) < 1e-3 && Math.Abs(ty) < 1e-3;
            if (restNow) { c.ClearValue(Visual.RenderTransformProperty); c.ClearValue(Animatable.TransitionsProperty); }
            if (toOpacity is double o) c.Opacity = o;
            onDone?.Invoke();
            return;
        }
        int step = 0, steps = Steps(Ms(ms));
```

(This REPLACES the existing `int step = 0, steps = Steps(ms);` line.)

In `Reveal`, after the `toW/fromO/toO` line insert the same idea, and scale the steps:

```csharp
        if (!Enabled) { c.Width = toW; c.Opacity = toO; return; }
        int step = 0, steps = Steps(Ms(ms));
```

(Again replacing its `int step = 0, steps = Steps(ms);` line.)

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter Ms_ScalesWithSpeedScale -v q --nologo` → PASS.

- [ ] **Step 5: Settings + VM + push-site**

`AppSettings.cs` — after `GlassTint`:

```csharp
    public bool ReduceMotion { get; set; }                  // skip animations entirely
    public string MotionSpeed { get; set; } = "Normal";     // "Calm" | "Normal" | "Snappy"
```

Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void MotionPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().ReduceMotion);
            Assert.Equal("Normal", new AppSettings().MotionSpeed);
            var s = new AppSettings { ReduceMotion = true, MotionSpeed = "Snappy" };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.True(loaded.ReduceMotion);
            Assert.Equal("Snappy", loaded.MotionSpeed);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

`MainViewModel.cs` — after `_glassTint`:

```csharp
    [ObservableProperty] private bool _reduceMotion;                // prefs: skip animations
    [ObservableProperty] private string _motionSpeed = "Normal";    // prefs: Calm | Normal | Snappy
```

Constructor load: `ReduceMotion = _settings.ReduceMotion;` and `MotionSpeed = _settings.MotionSpeed;`
Changed hooks (after `OnGlassTintChanged`):

```csharp
    partial void OnReduceMotionChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ReduceMotion = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMotionSpeedChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.MotionSpeed = value;
        _settings.Save(_settingsDir);
    }
```

`ResetSettingsToDefaults()` add: `ReduceMotion = d.ReduceMotion; MotionSpeed = d.MotionSpeed;`

`MainView.axaml.cs` — add to `HookVm()` init chain (after `ApplyGlassTint();`): `ApplyMotionPrefs();`

Add the method (near `ApplyGlassTint`):

```csharp
    /// <summary>Push the motion prefs onto the shared engine (statics — affect every window).</summary>
    private void ApplyMotionPrefs()
    {
        if (Vm is not { } vm) return;
        Motion.Enabled = !vm.ReduceMotion;
        Motion.SpeedScale = vm.MotionSpeed switch { "Calm" => 1.4, "Snappy" => 0.6, _ => 1.0 };
    }
```

In `OnVmPropertyChanged` add a branch (next to the GlassTint one):

```csharp
        else if (e.PropertyName is nameof(MainViewModel.ReduceMotion) or nameof(MainViewModel.MotionSpeed))
            ApplyMotionPrefs();
```

- [ ] **Step 6: Prefs rows**

In `PreferencesWindow.axaml`, append inside `AppearancePanel` (after the GLASS rows):

```xml
                        <TextBlock Classes="section" Text="MOTION"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Reduce motion"/>
                                <TextBlock Classes="hint" Text="Skip animations — everything lands instantly."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ReduceMotion, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Animation speed"/>
                                <TextBlock Classes="hint" Text="How quickly the whole app moves."/>
                            </StackPanel>
                            <ComboBox x:Name="MotionSpeedBox" Grid.Column="1" Width="120" VerticalAlignment="Center"/>
                        </Grid>
```

In `PreferencesWindow.axaml.cs` constructor (with the other combos):

```csharp
        MotionSpeedBox.ItemsSource = new[] { "Calm", "Normal", "Snappy" };
        MotionSpeedBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && MotionSpeedBox.SelectedItem is string speed) vm.MotionSpeed = speed;
        };
```

In `SyncFromVm()` add: `MotionSpeedBox.SelectedItem = vm.MotionSpeed;`

- [ ] **Step 7: Build + full suite + commit**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo`
Expected: all PASS.

```bash
git add -A src tests
git commit -m "feat(m7): motion prefs — reduce motion + Calm/Normal/Snappy speed on the shared engine"
```

---

## Task 8: Relaunch + owner verification

- [ ] **Step 1: Kill, build, relaunch the real app**

Run: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && cmd //c start "" "src\Lumenotepad\bin\Debug\net10.0\Lumenotepad.exe"`

- [ ] **Step 2: Owner checklist** (compositor/pointer behavior — cannot be verified headlessly)

1. Gear → prefs opens with the left nav; panels swap with a rise-in; all old toggles still work.
2. Layout → "Show notebooks rail / pages panel" apply live AND persist across restart.
3. Clicking "Data & tools" while locked shows the accent-colored Unlock dialog; Cancel returns
   to the previous category; Unlock reveals it (padlock disappears) and persists across restart.
4. Data & tools: Open reveals the userdata folder; size reads sensibly; Reset (red confirm)
   restores every pref incl. re-locking Advanced; Lock re-locks.
5. Accent: pick Pink → whole app re-tints (selection glows, gradients, toolbar accents, caret);
   custom hex applies on Enter; "A" chip returns to the theme accent; persists across restart.
6. Glass tint: on Lumen, slider left/right darkens/lightens the glass live; on Light (full
   theme), no visual change (veil hidden).
7. Motion: Snappy/Calm visibly change animation pace app-wide; Reduce motion lands everything
   instantly (home↔notebook zoom included) with no broken end-states.

Fix whatever the owner flags before starting Part 2 (bullets & numbers).
