# Advanced Preferences — Part 3 (Fonts Curation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship spec phase 5 — a gated "Fonts" prefs category with a per-font enable/disable
checklist that controls exactly what the toolbar's font menu offers, plus the "Show all installed
fonts" master switch (subsuming the Appearance panel's blunt Extended-fonts toggle, which moves
here).

**Architecture:** `AppSettings.DisabledFonts` (List&lt;string&gt;) → VM `IsFontEnabled`/`SetFontEnabled`
+ `FontPrefsVersion` bump (mirrors `BulletPrefsVersion`) → `MainView` pushes
`Toolbar.SetFontPrefs(extended, disabled)` (replacing `SetExtendedFonts`) which filters via a new
pure `AppFonts.WithoutDisabled` (bundled faces are NEVER hidden; case-insensitive). The prefs
checklist is a virtualized ListBox of checkbox rows built lazily when the Fonts panel shows
(virtualized item templates must null-guard — recycling briefly rebuilds with a null datum, and
`new FontFamily(null)` throws: known gotcha).

**Tech Stack:** Avalonia 12.0.4 / .NET 10, CommunityToolkit.Mvvm, xUnit. No web components.

**Covers spec phase 5** of `docs/superpowers/specs/2026-07-09-advanced-preferences-design.md`.
Parts 4+ (editor defaults, export/encoding/hash, optional roundness) follow after owner review.

**Known facts (verified at `3ca24bf`, 90/90 green):**
- `AppFonts` (`src/Lumenotepad/Services/AppFonts.cs`): `Bundled` = Bebas Neue/Caveat/Gambarino/Yuyu
  (never hidden, resolve via `Family(name)`); `ListNames(bool extended)` = bundled first + curated
  shortlist (or all installed). It queries `FontManager.Current` — NOT callable from headless unit
  tests, so the filter must be a separate pure function.
- Toolbar: `FormatToolbar.SetExtendedFonts(bool)` (FormatToolbar.axaml.cs:271) → `RefreshFontList()`
  → `AppFonts.ListNames(_extendedFonts)`. Call sites: MainView.axaml.cs:426 (HookVm) and :640
  (the `ExtendedFonts` branch of OnVmPropertyChanged).
- Prefs window: nav gets a `Tag="fonts"` item; `IsGated` ALREADY includes "fonts". The
  "Extended font list" toggle currently sits in `AppearancePanel` (PreferencesWindow.axaml:~140) and
  MOVES into the new Fonts panel. `ShowPanel(key)` swaps panels; the "data" case already does
  `if (key == "data") RefreshDataPanel();` — fonts follows the same lazy pattern.
- VM idioms: guard-save hooks, `ResetSettingsToDefaults`, version-bump channels
  (`BulletPrefsVersion`).
- BUILD GOTCHA: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never
  launch the GUI from a subagent.

---

## Task 1: AppFonts — pure disabled-filter + ListNames overload (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppFonts.cs`
- Test: `tests/Lumenotepad.Tests/AppFontsTests.cs` (CREATE)

- [ ] **Step 1: Failing tests.** Create `tests/Lumenotepad.Tests/AppFontsTests.cs`:

```csharp
using System.Linq;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class AppFontsTests
{
    [Fact]
    public void WithoutDisabled_DropsDisabled_CaseInsensitive()
    {
        var names = new[] { "Arial", "Georgia", "Impact" };
        var result = AppFonts.WithoutDisabled(names, new[] { "georgia", "IMPACT" }).ToList();
        Assert.Equal(new[] { "Arial" }, result);
    }

    [Fact]
    public void WithoutDisabled_NeverHidesBundledFaces()
    {
        var names = new[] { "Caveat", "Arial", "Yuyu" };
        var result = AppFonts.WithoutDisabled(names, new[] { "Caveat", "Yuyu", "Arial" }).ToList();
        Assert.Equal(new[] { "Caveat", "Yuyu" }, result);
    }

    [Fact]
    public void WithoutDisabled_NullOrEmpty_PassesThrough()
    {
        var names = new[] { "Arial", "Georgia" };
        Assert.Equal(names, AppFonts.WithoutDisabled(names, null).ToList());
        Assert.Equal(names, AppFonts.WithoutDisabled(names, System.Array.Empty<string>()).ToList());
    }
}
```

- [ ] **Step 2:** `cd /e/CLAUDE/Lumenotepad && dotnet test --filter AppFontsTests -v q --nologo` → compile FAIL.

- [ ] **Step 3: Implement.** In `AppFonts.cs` add after `Family(...)`:

```csharp
    /// <summary>Pure filter for the fonts-curation pref: drop disabled names (case-insensitive)
    /// but NEVER the bundled faces — they must stay reachable on every machine.</summary>
    public static IEnumerable<string> WithoutDisabled(IEnumerable<string> names,
                                                      IReadOnlyCollection<string>? disabled)
    {
        if (disabled is not { Count: > 0 }) return names;
        var hidden = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase);
        foreach (var b in Bundled) hidden.Remove(b);
        return names.Where(n => !hidden.Contains(n));
    }
```

And change `ListNames` to take the optional filter (existing call sites stay source-compatible):

```csharp
    /// <summary>The names offered by the toolbar's font menu: bundled first, then the curated
    /// shortlist (or every installed family when <paramref name="extended"/>), minus any the
    /// fonts-curation pref disabled (bundled faces are never hidden).</summary>
    public static IReadOnlyList<string> ListNames(bool extended, IReadOnlyCollection<string>? disabled = null)
    {
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> rest = extended
            ? installed.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            : Curated.Where(installed.Contains);
        return Bundled.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Concat(WithoutDisabled(rest.Where(n => !Bundled.Contains(n, StringComparer.OrdinalIgnoreCase)), disabled))
            .ToList();
    }
```

- [ ] **Step 4:** Filtered tests PASS; full suite `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet test -v q --nologo` → 93/93.

- [ ] **Step 5: Commit.**
```bash
git add src/Lumenotepad/Services/AppFonts.cs tests/Lumenotepad.Tests/AppFontsTests.cs
git commit -m "feat(m7p3): AppFonts.WithoutDisabled — pure curation filter, bundled always kept"
```

---

## Task 2: Settings + VM — DisabledFonts (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test.** Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void DisabledFonts_DefaultEmpty_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Empty(new AppSettings().DisabledFonts);
            var s = new AppSettings();
            s.DisabledFonts.Add("Impact");
            s.Save(dir);
            Assert.Equal(new[] { "Impact" }, AppSettings.Load(dir).DisabledFonts);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2:** Filtered run → compile FAIL.

- [ ] **Step 3: Settings field.** In `AppSettings.cs` after `NumStrikeDefault`:

```csharp
    public List<string> DisabledFonts { get; set; } = new();  // fonts hidden from the toolbar menu
```

Filtered run → PASS.

- [ ] **Step 4: VM plumbing.** In `MainViewModel.cs` after `_bulletPrefsVersion`:

```csharp
    /// <summary>Bumped whenever the fonts-curation list changes — the toolbar menu refreshes.</summary>
    [ObservableProperty] private int _fontPrefsVersion;
```

Near `BulletColorFor`/`SetBulletColor` add:

```csharp
    /// <summary>The fonts-curation blocklist (empty in the designer).</summary>
    public IReadOnlyCollection<string> DisabledFontsList =>
        (IReadOnlyCollection<string>?)_settings?.DisabledFonts ?? System.Array.Empty<string>();

    /// <summary>A font is offered unless the curation pref disabled it (case-insensitive).</summary>
    public bool IsFontEnabled(string name) =>
        _settings is null ||
        !_settings.DisabledFonts.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Enable/disable one font in the toolbar menu; bumps FontPrefsVersion.</summary>
    public void SetFontEnabled(string name, bool enabled)
    {
        if (_settings is null || _settingsDir is null) return;
        if (enabled) _settings.DisabledFonts.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        else if (IsFontEnabled(name)) _settings.DisabledFonts.Add(name);
        _settings.Save(_settingsDir);
        FontPrefsVersion++;
    }
```

In `ResetSettingsToDefaults()` append (after the BulletColors block):

```csharp
        if (_settings is not null && _settingsDir is not null && _settings.DisabledFonts.Count > 0)
        {
            _settings.DisabledFonts.Clear();
            _settings.Save(_settingsDir);
            FontPrefsVersion++;
        }
```

(`MainViewModel.cs` already has `using System;` + `using System.Linq;` — `Contains(…, StringComparer)`
needs Linq; verify.)

- [ ] **Step 5: VM test.** Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void FontEnablement_PersistsAndResets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.True(vm.IsFontEnabled("Impact"));

            vm.SetFontEnabled("Impact", false);
            Assert.False(vm.IsFontEnabled("impact"));                        // case-insensitive
            Assert.Contains("Impact", AppSettings.Load(dir).DisabledFonts);  // persisted

            vm.SetFontEnabled("IMPACT", true);
            Assert.True(vm.IsFontEnabled("Impact"));
            Assert.Empty(AppSettings.Load(dir).DisabledFonts);

            vm.SetFontEnabled("Georgia", false);
            vm.ResetSettingsToDefaults();
            Assert.True(vm.IsFontEnabled("Georgia"));
            Assert.Empty(AppSettings.Load(dir).DisabledFonts);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 6:** Full suite → 95/95. Commit:
```bash
git add -A src tests
git commit -m "feat(m7p3): DisabledFonts setting — VM enable/disable with FontPrefsVersion channel"
```

---

## Task 3: Toolbar + MainView — the curated list reaches the font menu

**Files:**
- Modify: `src/Lumenotepad/Views/FormatToolbar.axaml.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1: Toolbar.** In `FormatToolbar.axaml.cs`, REPLACE the `SetExtendedFonts` method
(line ~271, including its `_extendedFonts` field usage — keep the field) with:

```csharp
    private System.Collections.Generic.IReadOnlyCollection<string>? _disabledFonts;

    /// <summary>Fonts prefs: the full installed list vs the curated shortlist, minus the
    /// curation blocklist. Rebuilds the menu only when something actually changed.</summary>
    public void SetFontPrefs(bool extended, System.Collections.Generic.IReadOnlyCollection<string> disabled)
    {
        bool same = _extendedFonts == extended && _disabledFonts is not null
            && _disabledFonts.Count == disabled.Count
            && _disabledFonts.SequenceEqual(disabled);
        if (same && FontList.ItemsSource is not null) return;
        _extendedFonts = extended;
        _disabledFonts = disabled.ToList();               // snapshot — the VM list mutates in place
        RefreshFontList();
    }
```

and change `RefreshFontList` to pass the blocklist:

```csharp
    private void RefreshFontList()
    {
        var names = new System.Collections.Generic.List<string> { "(Default)" };
        names.AddRange(Services.AppFonts.ListNames(_extendedFonts, _disabledFonts));
        FontList.ItemsSource = names;
    }
```

(`using System.Linq;` — verify present for `SequenceEqual`/`ToList`; add if missing.)

- [ ] **Step 2: MainView call sites.** In `MainView.axaml.cs`:

Line ~426 (HookVm): replace `Toolbar.SetExtendedFonts(_hookedVm.ExtendedFonts);` with:
```csharp
            Toolbar.SetFontPrefs(_hookedVm.ExtendedFonts, _hookedVm.DisabledFontsList);
```

Line ~640 (the ExtendedFonts branch): replace `Toolbar.SetExtendedFonts(Vm?.ExtendedFonts ?? false);`
with a branch that also reacts to the curation channel — change the branch condition from
`e.PropertyName == nameof(MainViewModel.ExtendedFonts)` to:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.ExtendedFonts) or nameof(MainViewModel.FontPrefsVersion))
        {
            if (Vm is { } fvm) Toolbar.SetFontPrefs(fvm.ExtendedFonts, fvm.DisabledFontsList);
        }
```

- [ ] **Step 3:** Full suite → 95/95 (no new tests — view wiring; the filter is covered by Task 1).
Commit:
```bash
git add -A src
git commit -m "feat(m7p3): toolbar font menu honors the curation blocklist (SetFontPrefs)"
```

---

## Task 4: Prefs UI — gated "Fonts" category (checklist + master switch)

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: XAML.** In `NavList`, insert BETWEEN the ADVANCED header item and the
`Tag="bullets"` item:

```xml
                    <ListBoxItem Tag="fonts"><TextBlock Classes="label" Text="Fonts"/></ListBoxItem>
```

REMOVE the whole "Extended font list" Grid row from `AppearancePanel` (label + hint + ToggleSwitch —
it moves here).

Add to `Window.Styles` (with the other prefnav styles):

```xml
        <!-- fonts checklist rows: plain checkbox rows, no selection fill -->
        <Style Selector="ListBox.fontcheck ListBoxItem">
            <Setter Property="Padding" Value="4,2"/>
        </Style>
        <Style Selector="ListBox.fontcheck ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="Transparent"/>
        </Style>
        <Style Selector="ListBox.fontcheck ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="Transparent"/>
        </Style>
```

Inside the panels `Panel`, BEFORE `BulletsPanel`, insert:

```xml
                    <StackPanel x:Name="FontsPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="FONT MENU" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Show all installed fonts"/>
                                <TextBlock Classes="hint" Text="Offer every installed font instead of the essentials shortlist."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ExtendedFonts, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <TextBlock Classes="section" Text="OFFERED FONTS"/>
                        <TextBlock Classes="hint"
                                   Text="Untick a font to hide it from the toolbar's menu. The bundled faces are always available."/>
                        <ListBox x:Name="FontsList" Classes="fontcheck" Height="300" Margin="0,4,0,0"
                                 Background="Transparent" BorderThickness="0" Padding="0"/>
                    </StackPanel>
```

- [ ] **Step 2: Code-behind.** In `PreferencesWindow.axaml.cs`:

Register the panel: `["fonts"] = FontsPanel,` in `_panels`.

In the nav SelectionChanged handler, next to the existing `if (key == "data") RefreshDataPanel();`
line add:

```csharp
            if (key == "fonts") RefreshFontChoices();
```

Add to the class:

```csharp
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
```

(`using System.Linq;` — add if missing; `using Lumenotepad.Services;` is present.)

In `OnVmChanged` add a branch:

```csharp
        else if (e.PropertyName == nameof(MainViewModel.ExtendedFonts))
        {
            if (FontsPanel.IsVisible) RefreshFontChoices();
        }
```

In `SyncFromVm()` (before `UpdateGateVisuals();`) add — reset-while-on-fonts must refresh:

```csharp
        if (FontsPanel.IsVisible) RefreshFontChoices();
```

- [ ] **Step 3:** Full suite → 95/95. Commit:
```bash
git add -A src
git commit -m "feat(m7p3): Fonts prefs — per-font checklist + master switch (moved from Appearance)"
```

---

## Task 5: Final integration review + relaunch + owner checklist

- [ ] **Step 1:** Final reviewer over the Part 3 range: the two ListNames call paths (toolbar
filtered, prefs checklist unfiltered) stay coherent; disabling a font while its FontList entry is
selected in the toolbar; reset clears the blocklist and both windows refresh; ExtendedFonts flip
refreshes toolbar AND visible checklist.
- [ ] **Step 2:** `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && cmd //c start "" "src\Lumenotepad\bin\Debug\net10.0\Lumenotepad.exe"`
- [ ] **Step 3: Owner checklist** (real-app only):
1. ADVANCED nav now: Fonts, Bullets & numbers, Data & tools (same unlock).
2. Fonts panel: master switch shows all installed fonts (checklist grows); untick e.g. Impact →
   the toolbar's font menu no longer offers it (open a note, check the menu); re-tick → it's back.
3. Bundled faces (Bebas Neue, Caveat, Gambarino, Yuyu) are locked on with a tooltip.
4. The "Extended font list" toggle is GONE from Appearance (lives in Fonts now); flipping it in
   Fonts updates the toolbar menu immediately.
5. Unticked fonts persist across restart; Reset settings re-enables everything.
6. Scroll the checklist with all-installed on — smooth (virtualized), each name previews in its
   own face.
