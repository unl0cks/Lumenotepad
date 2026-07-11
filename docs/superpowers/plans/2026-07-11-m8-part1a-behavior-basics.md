# M8 Part 1a — Behavior Basics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the six "behavior basics" of the M8 spec: launch behavior (home vs last page),
autosave interval, per-kind delete confirmations, Jump-back-in count, always-on-top, and
start-with-Windows — all in a new ungated **General** prefs category that becomes the nav's FIRST
item.

**Architecture:** Established M7 pattern throughout (AppSettings → VM observable + guard-save →
consumer applies). Start-with-Windows is the exception: the REGISTRY is the source of truth (no
AppSettings field) via a new `Platform/StartupRegistry` helper. Delete confirmations ride the two
existing choke points: `MainView.ConfirmThenDelete` (notebook/section/page) gains an `ask` flag,
and the `PageCanvas.ConfirmDelete` lambda short-circuits.

**Tech Stack:** Avalonia 12.0.4 / .NET 10, CommunityToolkit.Mvvm, xUnit, Microsoft.Win32.Registry.

**Spec:** Part 1a table in `docs/superpowers/specs/2026-07-11-m8-customization-design.md`.

**Known facts (verified at `596b562`, 95/95 green):**
- `MainView.ConfirmThenDelete(string title, string message, System.Action delete)` at
  MainView.axaml.cs:971 is the single confirm helper for notebook/section/page deletes (find its
  call sites by grepping `ConfirmThenDelete(` — they're in the three context-menu builders).
- Container confirm: `PageCanvas.ConfirmDelete = () => ConfirmDialog.Show(Window!, …)` in the
  MainView constructor (line ~51). `ConfirmDelete` is a `Func<Task<bool>>`-shaped member.
- Autosave: `MainView.OnDocsDirtied` (line ~590) lazily creates `_autosave` with a hardcoded
  `FromMilliseconds(900)` and restarts it per change.
- VM: `OnSelectedPageChanged(Page? value)` partial ALREADY EXISTS (null-reselect guard) — extend
  it, don't duplicate. Ctor sets `SelectedNotebook = Notebooks.FirstOrDefault();` then
  `RefreshHome();`. `RefreshHome()` has `.Take(5)`. `Page.Id` is a string id.
- MainWindow: `HookThemeVm`/`OnThemePropertyChanged`/`ApplyTheme` — add an `AlwaysOnTop` branch
  (do NOT widen the ApplyTheme filter; Topmost is not a theme concern).
- Prefs window: nav `ListBoxItem Tag="…"` list starts with `appearance`; `_panels` dict;
  `NavList.SelectedIndex = 0` after wiring; `SyncFromVm()` ends with `UpdateGateVisuals()`.
- Packages are centrally managed: `Directory.Packages.props` at repo root holds versions; the
  csproj references without versions. Check the csproj TFM — if plain `net10.0`, the
  `Microsoft.Win32.Registry` package (5.0.0) is required for registry access.
- `AppSettings.DefaultDir` gotcha: `Environment.ProcessPath` points at dotnet.exe under
  `dotnet App.dll` — the Run-key value must use `AppContext.BaseDirectory + "Lumenotepad.exe"`.
- BUILD GOTCHA: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never
  launch the GUI from a subagent.

---

## Task 1: Settings + VM (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test.** Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void BehaviorPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Equal("Home", d.LaunchTarget);
            Assert.Null(d.LastPageId);
            Assert.Equal(900, d.AutosaveMs);
            Assert.True(d.ConfirmDeleteNotebook);
            Assert.True(d.ConfirmDeleteSection);
            Assert.True(d.ConfirmDeletePage);
            Assert.True(d.ConfirmDeleteContainer);
            Assert.Equal(5, d.RecentCount);
            Assert.False(d.AlwaysOnTop);

            var s = new AppSettings
            {
                LaunchTarget = "LastPage", LastPageId = "p1", AutosaveMs = 2000,
                ConfirmDeletePage = false, RecentCount = 8, AlwaysOnTop = true,
            };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("LastPage", loaded.LaunchTarget);
            Assert.Equal("p1", loaded.LastPageId);
            Assert.Equal(2000, loaded.AutosaveMs);
            Assert.False(loaded.ConfirmDeletePage);
            Assert.True(loaded.ConfirmDeleteNotebook);
            Assert.Equal(8, loaded.RecentCount);
            Assert.True(loaded.AlwaysOnTop);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2:** Filtered run → compile FAIL.

- [ ] **Step 3: Settings fields.** In `AppSettings.cs` after `DisabledFonts`:

```csharp
    public string LaunchTarget { get; set; } = "Home";      // "Home" | "LastPage"
    public string? LastPageId { get; set; }                 // auto-tracked; not a pref, not reset
    public int AutosaveMs { get; set; } = 900;              // typing → save debounce (100..5000)
    public bool ConfirmDeleteNotebook { get; set; } = true; // per-kind "are you sure" prompts
    public bool ConfirmDeleteSection { get; set; } = true;
    public bool ConfirmDeletePage { get; set; } = true;
    public bool ConfirmDeleteContainer { get; set; } = true;
    public int RecentCount { get; set; } = 5;               // homepage "Jump back in" entries (0..10)
    public bool AlwaysOnTop { get; set; }
```

Filtered run → PASS.

- [ ] **Step 4: VM plumbing.** In `MainViewModel.cs` after `_fontPrefsVersion`:

```csharp
    [ObservableProperty] private string _launchTarget = "Home";     // prefs: "Home" | "LastPage"
    [ObservableProperty] private int _autosaveMs = 900;             // prefs: save debounce
    [ObservableProperty] private bool _confirmDeleteNotebook = true;
    [ObservableProperty] private bool _confirmDeleteSection = true;
    [ObservableProperty] private bool _confirmDeletePage = true;
    [ObservableProperty] private bool _confirmDeleteContainer = true;
    [ObservableProperty] private int _recentCount = 5;              // prefs: Jump back in entries
    [ObservableProperty] private bool _alwaysOnTop;
```

Ctor load block (after the Num*Default lines):

```csharp
            LaunchTarget = _settings.LaunchTarget;
            AutosaveMs = _settings.AutosaveMs;
            ConfirmDeleteNotebook = _settings.ConfirmDeleteNotebook;
            ConfirmDeleteSection = _settings.ConfirmDeleteSection;
            ConfirmDeletePage = _settings.ConfirmDeletePage;
            ConfirmDeleteContainer = _settings.ConfirmDeleteContainer;
            RecentCount = _settings.RecentCount;
            AlwaysOnTop = _settings.AlwaysOnTop;
```

Guard-save hooks (after `OnNumStrikeDefaultChanged`, one per property — all identical shape):

```csharp
    partial void OnLaunchTargetChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.LaunchTarget = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAutosaveMsChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AutosaveMs = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteNotebookChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteNotebook = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteSectionChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteSection = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeletePageChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeletePage = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteContainerChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteContainer = value;
        _settings.Save(_settingsDir);
    }

    partial void OnRecentCountChanged(int value)
    {
        // AMENDED (final review C1): the ctor's settings-load fires this BEFORE the workspace is
        // built — RefreshHome would NRE on a persisted non-default count. The ctor calls
        // RefreshHome itself at the end, so skipping here is correct, not just safe.
        if (_workspace is null) return;
        RefreshHome();                                   // the strip resizes live
        if (_settings is null || _settingsDir is null) return;
        _settings.RecentCount = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AlwaysOnTop = value;
        _settings.Save(_settingsDir);
    }
```

- [ ] **Step 5: Launch-target resolution + last-page tracking + recents count.**

In the ctor, REPLACE the tail `SelectedNotebook = Notebooks.FirstOrDefault(); RefreshHome();` with
(AMENDED after implementation caught an ordering bug: the default-selection cascade fires
`OnSelectedPageChanged`, which OVERWRITES `_settings.LastPageId` before the resolution block would
read it — so the target id must be captured into a local FIRST):

```csharp
        // Capture BEFORE the default selection below — its cascade re-tracks LastPageId.
        var lastPageId = _settings is { LaunchTarget: "LastPage" } ? _settings.LastPageId : null;
        SelectedNotebook = Notebooks.FirstOrDefault();
        if (!string.IsNullOrEmpty(lastPageId))
        {
            var hit = Notebooks
                .SelectMany(nb => nb.Sections.SelectMany(s => s.Pages.Select(p => (nb, s, p))))
                .FirstOrDefault(x => x.p.Id == lastPageId);
            if (hit.p is not null)
            {
                SelectedNotebook = hit.nb;
                SelectedSection = hit.s;
                SelectedPage = hit.p;
                IsHomeVisible = false;              // land straight in the editor
            }
        }
        RefreshHome();
```

Extend the EXISTING `OnSelectedPageChanged` partial — after its null-reselect guard, append:

```csharp
        if (value is not null && _settings is not null && _settingsDir is not null)
        {
            _settings.LastPageId = value.Id;        // bookkeeping for "open last page" launches
            _settings.Save(_settingsDir);
        }
```

In `RefreshHome()`, replace `.Take(5)` with:

```csharp
            .Take(Math.Clamp(RecentCount, 0, 10))
```

In `ResetSettingsToDefaults()` add (LastPageId is bookkeeping, deliberately NOT reset):

```csharp
        LaunchTarget = d.LaunchTarget; AutosaveMs = d.AutosaveMs;
        ConfirmDeleteNotebook = d.ConfirmDeleteNotebook; ConfirmDeleteSection = d.ConfirmDeleteSection;
        ConfirmDeletePage = d.ConfirmDeletePage; ConfirmDeleteContainer = d.ConfirmDeleteContainer;
        RecentCount = d.RecentCount; AlwaysOnTop = d.AlwaysOnTop;
```

- [ ] **Step 6: VM tests.** Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void LaunchTarget_LastPage_LandsInEditorOnMatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var seed = new MainViewModel(new WorkspaceStore(dir), dir);
            var page = seed.SelectedPage!;
            new AppSettings { LaunchTarget = "LastPage", LastPageId = page.Id }.Save(dir);

            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.False(vm.IsHomeVisible);
            Assert.Equal(page.Id, vm.SelectedPage?.Id);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LaunchTarget_LastPage_FallsBackToHomeWhenGone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { LaunchTarget = "LastPage", LastPageId = "no-such-page" }.Save(dir);
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.True(vm.IsHomeVisible);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void SelectingAPage_TracksLastPageId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var id = vm.SelectedPage!.Id;
            Assert.Equal(id, AppSettings.Load(dir).LastPageId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

(The third test works because the ctor's selection cascade assigns SelectedPage, firing the
tracking hook. If the cascade assigns before `_settings` is set, the assert fails — in that case
the ctor ordering is the bug to fix: settings MUST load before `_workspace = store.LoadOrSeed();`,
which it already does.)

- [ ] **Step 7:** Full suite: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && dotnet test -v q --nologo` → 99/99. Commit:
```bash
git add -A src tests
git commit -m "feat(m8): behavior-basics settings — launch target, autosave, confirmations, recents, topmost"
```

---

## Task 2: View wiring (autosave, confirmations, topmost)

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Modify: `src/Lumenotepad/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Autosave interval.** In `MainView.OnDocsDirtied` (line ~590), the timer is created
once with 900ms. Change so every arm reads the pref (live):

```csharp
    private void OnDocsDirtied()
    {
        if (_autosave is null)
        {
            _autosave = new Avalonia.Threading.DispatcherTimer();
            _autosave.Tick += (_, _) => { _autosave!.Stop(); Vm?.FlushDirtyDocs(); };
        }
        _autosave.Stop();
        _autosave.Interval = System.TimeSpan.FromMilliseconds(
            System.Math.Clamp(Vm?.AutosaveMs ?? 900, 100, 5000));
        _autosave.Start();
    }
```

(Match the existing method body — only the Interval line moves/changes; keep the rest.)

- [ ] **Step 2: Confirmations.** Change `ConfirmThenDelete` (line ~971) to:

```csharp
    private async void ConfirmThenDelete(string title, string message, bool ask, System.Action delete)
    {
        if (!ask) { delete(); return; }
        if (Window is not { } w) return;
        if (await ConfirmDialog.Show(w, title, message)) delete();
    }
```

Grep `ConfirmThenDelete(` call sites (the notebook/section/page context menus + any others) and
pass the matching pref at each:
- notebook deletes → `Vm?.ConfirmDeleteNotebook ?? true`
- section deletes → `Vm?.ConfirmDeleteSection ?? true`
- page deletes → `Vm?.ConfirmDeletePage ?? true`

(Insert the flag as the third argument, before the delete action.)

Container confirm — in the constructor, REPLACE the `PageCanvas.ConfirmDelete = () => ConfirmDialog.Show(…)` assignment with:

```csharp
        PageCanvas.ConfirmDelete = () =>
            Vm is { ConfirmDeleteContainer: false }
                ? System.Threading.Tasks.Task.FromResult(true)
                : ConfirmDialog.Show(Window!,
                    "Delete this container?",
                    PageCanvas.HistoryEnabled
                        ? "It will move to this page's deleted history — you can drag it back onto the page anytime."
                        : "The deleted history is turned off, so this can't be undone.");
```

- [ ] **Step 3: Always on top.** In `MainWindow.axaml.cs`:

In `HookThemeVm()`, after `ApplyTheme();` add: `Topmost = _themeVm.AlwaysOnTop;`

In `OnThemePropertyChanged`, add after the existing theme-filter `if`:

```csharp
        if (e.PropertyName == nameof(ViewModels.MainViewModel.AlwaysOnTop) && _themeVm is { } topVm)
            Topmost = topVm.AlwaysOnTop;
```

- [ ] **Step 4:** Full suite → 99/99. Commit:
```bash
git add -A src
git commit -m "feat(m8): live autosave interval, per-kind delete confirmations, always-on-top"
```

---

## Task 3: StartupRegistry + the General prefs category

**Files:**
- Create: `src/Lumenotepad/Platform/StartupRegistry.cs`
- Modify: `Directory.Packages.props` + `src/Lumenotepad/Lumenotepad.csproj` (Registry package,
  ONLY if the TFM is plain `net10.0` — check first; if it's `net10.0-windows` the API is inbox)
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: Registry helper.** Create `src/Lumenotepad/Platform/StartupRegistry.cs`:

```csharp
using System;
using System.IO;
using Microsoft.Win32;

namespace Lumenotepad.Platform;

/// <summary>The "Start with Windows" toggle. The REGISTRY is the source of truth (no settings
/// field) so the switch always shows reality even if the user edited it elsewhere. The Run value
/// points at the exe beside our assemblies — Environment.ProcessPath is dotnet.exe under
/// `dotnet App.dll` and must not be used.</summary>
public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumenotepad";

    private static string ExePath => Path.Combine(AppContext.BaseDirectory, "Lumenotepad.exe");

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(ValueName, $"\"{ExePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* non-elevated registry writes can still fail under policy — fail quiet */ }
    }
}
```

Build once. If `Microsoft.Win32.Registry` doesn't resolve: add
`<PackageVersion Include="Microsoft.Win32.Registry" Version="5.0.0"/>` to
`Directory.Packages.props` and `<PackageReference Include="Microsoft.Win32.Registry"/>` to the app
csproj, then rebuild.

- [ ] **Step 2: XAML.** In `PreferencesWindow.axaml`, insert as the FIRST NavList item (before
`Tag="appearance"`):

```xml
                    <ListBoxItem Tag="general"><TextBlock Classes="label" Text="General"/></ListBoxItem>
```

Insert as the first panel inside the panels `Panel` (before `AppearancePanel`; note Appearance
keeps `IsVisible` default true from Task-1-of-M7 — CHANGE `AppearancePanel` to `IsVisible="False"`
and give `GeneralPanel` the visible-by-default role):

```xml
                    <StackPanel x:Name="GeneralPanel" Spacing="6">
                        <TextBlock Classes="section" Text="STARTUP" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="When Lumenotepad opens"/>
                                <TextBlock Classes="hint" Text="Land on the notebook gallery, or jump straight back into the last page."/>
                            </StackPanel>
                            <ComboBox x:Name="LaunchTargetBox" Grid.Column="1" Width="150" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Start with Windows"/>
                                <TextBlock Classes="hint" Text="Launch Lumenotepad when you sign in."/>
                            </StackPanel>
                            <ToggleSwitch x:Name="StartupToggle" Grid.Column="1" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Always on top"/>
                                <TextBlock Classes="hint" Text="Keep the window above other apps."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding AlwaysOnTop, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <TextBlock Classes="section" Text="SAVING"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Autosave after typing stops"/>
                            <TextBlock x:Name="AutosaveValue" Grid.Column="1" Classes="hint" Text="0.9s"/>
                        </Grid>
                        <Slider x:Name="AutosaveSlider" Minimum="100" Maximum="5000"
                                TickFrequency="100" IsSnapToTickEnabled="True"/>

                        <TextBlock Classes="section" Text="HOMEPAGE"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Jump back in entries"/>
                                <TextBlock Classes="hint" Text="How many recent pages the homepage offers. Zero hides the strip."/>
                            </StackPanel>
                            <TextBlock x:Name="RecentCountValue" Grid.Column="1" Classes="label" Text="5"/>
                        </Grid>
                        <Slider x:Name="RecentCountSlider" Minimum="0" Maximum="10"
                                TickFrequency="1" IsSnapToTickEnabled="True"/>

                        <TextBlock Classes="section" Text="ASK BEFORE DELETING"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Notebooks"/>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ConfirmDeleteNotebook, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Sections"/>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ConfirmDeleteSection, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Pages"/>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ConfirmDeletePage, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Note containers"/>
                                <TextBlock Classes="hint" Text="Container deletes can restore from the page's deleted history anyway."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ConfirmDeleteContainer, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>
```

- [ ] **Step 3: Code-behind.** In `PreferencesWindow.axaml.cs`:

`_panels` gains `["general"] = GeneralPanel,` as the FIRST entry. (NavList.SelectedIndex = 0 now
selects General; ShowPanel hides the others — including AppearancePanel whose default visibility
you flipped in Step 2.)

Constructor wiring (near the other combos/sliders):

```csharp
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
```

Class members:

```csharp
    private bool _syncingStartup;
```

`SyncFromVm()` additions (before `UpdateGateVisuals();`):

```csharp
        LaunchTargetBox.SelectedItem = vm.LaunchTarget == "LastPage" ? "Last page" : "Home page";
        AutosaveSlider.Value = vm.AutosaveMs;
        AutosaveValue.Text = $"{vm.AutosaveMs / 1000.0:0.#}s";
        RecentCountSlider.Value = vm.RecentCount;
        RecentCountValue.Text = vm.RecentCount.ToString();
        _syncingStartup = true;
        StartupToggle.IsChecked = Platform.StartupRegistry.IsEnabled();
        _syncingStartup = false;
```

- [ ] **Step 4:** Full suite → 99/99. Commit:
```bash
git add -A src Directory.Packages.props
git commit -m "feat(m8): General prefs category — startup, autosave, homepage, delete-confirm toggles"
```

---

## Task 4: Final integration review + relaunch + owner checklist

- [ ] **Step 1:** Final reviewer over the Part 1a range: reset covers the 8 new prefs (NOT
LastPageId); launch-target resolution ordering vs the selection cascade; the last-page settings
write on every page switch doesn't fight the autosave debounce; General panel is the startup
panel and Appearance still reachable; StartupToggle reflects the registry, not settings.
- [ ] **Step 2:** Rebuild + relaunch.
- [ ] **Step 3: Owner checklist:**
1. Prefs now opens on **General** (first nav item); Appearance and everything else still work.
2. "When Lumenotepad opens → Last page", close, relaunch → lands directly in the last page you
   touched; switch back to Home page → gallery again.
3. Autosave slider: set 5s, type, watch the save happen later than before (or trust the tests).
4. Jump back in slider: 0 hides the strip; 10 shows up to ten.
5. Ask-before-deleting: turn "Pages" off → deleting a page is instant, no dialog; others still ask.
6. Always on top keeps the window above other apps; off releases it.
7. Start with Windows: toggle on → sign-out/in (or check Task Manager → Startup apps) →
   Lumenotepad autostarts; toggle off unregisters. The switch reflects reality on reopen.
8. Reset settings restores all of the above except your last-page bookmark.
