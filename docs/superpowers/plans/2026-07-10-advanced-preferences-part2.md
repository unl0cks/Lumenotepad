# Advanced Preferences — Part 2 (Bullets & Numbers) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship spec phase 4 — per-bullet-style color overrides, global numbered-list number-style
defaults (both in a new gated "Bullets & numbers" prefs category), and the per-list number-style
override in the editor's bullet flyout ("both" per the owner's decision).

**Architecture:** Overrides flow prefs → `AppSettings` (dict + 4 `bool?`s) → `MainViewModel`
(`SetBulletColor`/`BulletColorFor` + `BulletPrefsVersion` bump; 4 observable `bool?` defaults) →
`MainView.ApplyBulletPrefs` pushes onto `RichTextEditor` STATICS (`BulletColorOverrides`,
`Num*Default`) and rebuilds the canvas (the proven theme-change pattern). Rendering resolves
number flags as `per-paragraph ?? global default ?? text run` via one pure helper. The per-list
override reuses the existing `Paragraph.NumBold/…` model + undo snapshots, driven from a new
`NumStylePanel` row in the toolbar's bullet flyout.

**Tech Stack:** Avalonia 12.0.4 / .NET 10, CommunityToolkit.Mvvm, xUnit. No web components.

**Covers spec phase 4** of `docs/superpowers/specs/2026-07-09-advanced-preferences-design.md`.
Parts 3+ (fonts curation, editor defaults, export/encoding/hash) follow after owner review.

**Known facts (verified):**
- `Paragraph.NumBold/NumItalic/NumUnderline/NumStrike` (`bool?`, null = inherit) exist and persist
  (`RichDocJson` `nb/ni/nu/ns`). Only UI + defaults are missing.
- `RichTextEditor.BulletGlyphs` (private static) maps style → (glyph, hex): dot/arrow `#4DA6FF`,
  star `#E9B865`, heart `#E27BA6`, flower `#7FD1A6`, spark `#FFD966`. `BrushFor(hex)` caches brushes.
  `DrawBullet`'s `"num"` branch already reads `p.NumBold ?? fr?.Bold ?? false` etc.
- Editor: `CurrentBullet`, `ApplyBullet` (pattern: `PushUndo(); …; _typingBurst = false;
  RaiseSelectionChanged();`), `SelOrdered()`, `_doc`. Toolbar: `Do(Action<RichTextEditor>)` helper,
  `UpdateFromEditor()` synced on the editor's `SelectionChanged`, `_syncing` guard, bullet flyout =
  `<Flyout><StackPanel … x:Name="BulletChoices"/></Flyout>` (FormatToolbar.axaml:59-63).
- Prefs window: `_panels` dict + `ShowPanel`; `IsGated` ALREADY includes `"bullets"` (Task 3
  forward-looking); `SyncFromVm` ends with `UpdateGateVisuals()`; `OnVmChanged` handles
  CustomAccent/theme/AdvancedUnlocked. `MainViewModel.NotebookPalette` = 9 families × 5 shades.
- `AppSettings.cs` has usings `System.IO` + `System.Text.Json` ONLY — Task 2 adds
  `System.Collections.Generic`. `MainView.axaml.cs` has NO `using System;` (qualify `System.Math`
  if ever needed). Tests: 85 green at `70062ef`.
- BUILD GOTCHA: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never
  launch the GUI from a subagent.

---

## Task 1: Model — numbered-run walk + per-list flag set (pure, TDD)

**Files:**
- Modify: `src/Lumenotepad/Editor/RichModel.cs` (RichDocument)
- Test: `tests/Lumenotepad.Tests/RichModelTests.cs`

- [ ] **Step 1: Failing tests.** Add to `RichModelTests.cs`:

```csharp
    [Fact]
    public void NumRunAt_FindsContiguousRun_AndRejectsNonNum()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "a\nb\nc\nd\ne");
        doc.SetBullet(new DocPos(1, 0), new DocPos(3, 0), "num");   // paras 1..3 numbered

        Assert.Equal((1, 3), doc.NumRunAt(2));
        Assert.Equal((1, 3), doc.NumRunAt(1));
        Assert.Equal((1, 3), doc.NumRunAt(3));
        Assert.Null(doc.NumRunAt(0));
        Assert.Null(doc.NumRunAt(4));
        Assert.Null(doc.NumRunAt(-1));
        Assert.Null(doc.NumRunAt(99));
    }

    [Fact]
    public void SetNumFlag_SetsWholeRun_NotNeighbors_AndRaisesChanged()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "a\nb\nc\nd");
        doc.SetBullet(new DocPos(0, 0), new DocPos(2, 0), "num");   // paras 0..2 numbered

        bool changed = false;
        doc.Changed += () => changed = true;
        doc.SetNumFlag(1, 'b', true);

        Assert.True(changed);
        Assert.True(doc.Paragraphs[0].NumBold);
        Assert.True(doc.Paragraphs[1].NumBold);
        Assert.True(doc.Paragraphs[2].NumBold);
        Assert.Null(doc.Paragraphs[3].NumBold);      // outside the run

        doc.SetNumFlag(1, 'b', null);                // clearing restores inherit
        Assert.Null(doc.Paragraphs[0].NumBold);

        changed = false;
        doc.SetNumFlag(3, 'b', true);                // not a numbered paragraph → no-op
        Assert.False(changed);
    }
```

- [ ] **Step 2:** `cd /e/CLAUDE/Lumenotepad && dotnet test --filter "NumRunAt_FindsContiguousRun_AndRejectsNonNum|SetNumFlag_SetsWholeRun_NotNeighbors_AndRaisesChanged" -v q --nologo` → compile FAIL.

- [ ] **Step 3: Implement.** In `RichModel.cs`, inside `RichDocument` after `ToggleChecked`:

```csharp
    /// <summary>The contiguous run of "num" paragraphs containing <paramref name="paraIndex"/>
    /// (start..end inclusive), or null when that paragraph isn't numbered.</summary>
    public (int Start, int End)? NumRunAt(int paraIndex)
    {
        if (paraIndex < 0 || paraIndex >= Paragraphs.Count || Paragraphs[paraIndex].Bullet != "num")
            return null;
        int s = paraIndex, e = paraIndex;
        while (s > 0 && Paragraphs[s - 1].Bullet == "num") s--;
        while (e + 1 < Paragraphs.Count && Paragraphs[e + 1].Bullet == "num") e++;
        return (s, e);
    }

    /// <summary>Set one number-style override ('b','i','u','s'; null = inherit) across the whole
    /// numbered list containing <paramref name="paraIndex"/>. No-op outside a numbered list.</summary>
    public void SetNumFlag(int paraIndex, char flag, bool? value)
    {
        if (NumRunAt(paraIndex) is not { } run) return;
        for (int i = run.Start; i <= run.End; i++)
        {
            var p = Paragraphs[i];
            switch (flag)
            {
                case 'b': p.NumBold = value; break;
                case 'i': p.NumItalic = value; break;
                case 'u': p.NumUnderline = value; break;
                case 's': p.NumStrike = value; break;
            }
            p.Version++;
        }
        OnChanged();
    }
```

- [ ] **Step 4:** Filtered tests PASS, then full suite: `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet test -v q --nologo` → 87/87.

- [ ] **Step 5: Commit.**
```bash
git add src/Lumenotepad/Editor/RichModel.cs tests/Lumenotepad.Tests/RichModelTests.cs
git commit -m "feat(m7p2): RichDocument.NumRunAt + SetNumFlag — per-list number-style model ops"
```

---

## Task 2: Settings + VM — bullet colors dict + number defaults

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test.** Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void BulletPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Empty(new AppSettings().BulletColors);
            Assert.Null(new AppSettings().NumBoldDefault);

            var s = new AppSettings { NumBoldDefault = true, NumStrikeDefault = false };
            s.BulletColors["star"] = "#FF0000";
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal("#FF0000", loaded.BulletColors["star"]);
            Assert.True(loaded.NumBoldDefault);
            Assert.False(loaded.NumStrikeDefault);
            Assert.Null(loaded.NumItalicDefault);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2:** Filtered run → compile FAIL.

- [ ] **Step 3: Settings fields.** In `AppSettings.cs`: add `using System.Collections.Generic;` at the
top, and after `MotionSpeed` add:

```csharp
    public Dictionary<string, string> BulletColors { get; set; } = new();  // bullet style → hex override
    public bool? NumBoldDefault { get; set; }               // numbered-list number style defaults;
    public bool? NumItalicDefault { get; set; }             // null = the number matches its line's text
    public bool? NumUnderlineDefault { get; set; }
    public bool? NumStrikeDefault { get; set; }
```

Filtered run → PASS.

- [ ] **Step 4: VM plumbing.** In `MainViewModel.cs` after `_motionSpeed`:

```csharp
    [ObservableProperty] private bool? _numBoldDefault;             // prefs: number-style defaults;
    [ObservableProperty] private bool? _numItalicDefault;           // null = match the line's text
    [ObservableProperty] private bool? _numUnderlineDefault;
    [ObservableProperty] private bool? _numStrikeDefault;
    /// <summary>Bumped whenever a bullet color override changes — consumers re-read BulletColorFor.</summary>
    [ObservableProperty] private int _bulletPrefsVersion;
```

Ctor load block (after `MotionSpeed = ...`):
```csharp
            NumBoldDefault = _settings.NumBoldDefault;
            NumItalicDefault = _settings.NumItalicDefault;
            NumUnderlineDefault = _settings.NumUnderlineDefault;
            NumStrikeDefault = _settings.NumStrikeDefault;
```

After `OnMotionSpeedChanged`, four identical hooks:
```csharp
    partial void OnNumBoldDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumBoldDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumItalicDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumItalicDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumUnderlineDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumUnderlineDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumStrikeDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumStrikeDefault = value;
        _settings.Save(_settingsDir);
    }
```

Near `SettingsDir` add:
```csharp
    /// <summary>The effective hex override for a bullet style (null = the built-in default color).</summary>
    public string? BulletColorFor(string style) =>
        _settings is not null && _settings.BulletColors.TryGetValue(style, out var hex) ? hex : null;

    /// <summary>Set (hex) or clear (null) one bullet style's color override; bumps BulletPrefsVersion.</summary>
    public void SetBulletColor(string style, string? hex)
    {
        if (_settings is null || _settingsDir is null) return;
        if (hex is null) _settings.BulletColors.Remove(style);
        else _settings.BulletColors[style] = hex;
        _settings.Save(_settingsDir);
        BulletPrefsVersion++;
    }
```

In `ResetSettingsToDefaults()` append:
```csharp
        NumBoldDefault = d.NumBoldDefault; NumItalicDefault = d.NumItalicDefault;
        NumUnderlineDefault = d.NumUnderlineDefault; NumStrikeDefault = d.NumStrikeDefault;
        if (_settings is not null && _settingsDir is not null && _settings.BulletColors.Count > 0)
        {
            _settings.BulletColors.Clear();
            _settings.Save(_settingsDir);
            BulletPrefsVersion++;
        }
```

- [ ] **Step 5: VM test.** Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void BulletColor_SetPersistClearAndReset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Null(vm.BulletColorFor("star"));

            vm.SetBulletColor("star", "#FF0000");
            Assert.Equal("#FF0000", vm.BulletColorFor("star"));
            Assert.Equal("#FF0000", AppSettings.Load(dir).BulletColors["star"]);   // persisted

            vm.SetBulletColor("star", null);
            Assert.Null(vm.BulletColorFor("star"));

            vm.SetBulletColor("heart", "#00FF00");
            vm.NumBoldDefault = true;
            vm.ResetSettingsToDefaults();
            Assert.Null(vm.BulletColorFor("heart"));
            Assert.Null(vm.NumBoldDefault);
            Assert.Empty(AppSettings.Load(dir).BulletColors);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 6:** Full suite → 89/89. Commit:
```bash
git add -A src tests
git commit -m "feat(m7p2): bullet-color overrides + number-style defaults in settings/VM"
```

---

## Task 3: Editor — override-aware rendering + per-list toggle API

**Files:**
- Modify: `src/Lumenotepad/Editor/RichTextEditor.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Test: `tests/Lumenotepad.Tests/RichModelTests.cs` (pure fallback helper test)

- [ ] **Step 1: Failing test** (the pure resolver). Add to `RichModelTests.cs` — note it targets
`RichTextEditor.NumFlag`, add `using Lumenotepad.Editor;` if missing (it's there for the model):

```csharp
    [Fact]
    public void NumFlag_ResolvesParaThenDefaultThenRun()
    {
        Assert.True(RichTextEditor.NumFlag(true, false, false));    // paragraph override wins
        Assert.False(RichTextEditor.NumFlag(false, true, true));
        Assert.True(RichTextEditor.NumFlag(null, true, false));     // then the global default
        Assert.False(RichTextEditor.NumFlag(null, false, true));
        Assert.True(RichTextEditor.NumFlag(null, null, true));      // then the text run
        Assert.False(RichTextEditor.NumFlag(null, null, false));
    }
```

Run filtered → compile FAIL.

- [ ] **Step 2: Editor statics + resolver + rendering.** In `RichTextEditor.cs`, right above the
`BulletGlyphs` dictionary add:

```csharp
    // ---- prefs: bullet colors + number-style defaults (pushed by MainView.ApplyBulletPrefs) ----
    /// <summary>Per-style bullet color overrides (style → hex); missing = the built-in default.</summary>
    public static readonly Dictionary<string, string> BulletColorOverrides = new();
    /// <summary>Global number-style defaults; null = the number matches its line's text.</summary>
    public static bool? NumBoldDefault, NumItalicDefault, NumUnderlineDefault, NumStrikeDefault;

    /// <summary>Number-style resolution: paragraph override, then the global default, then the run.</summary>
    public static bool NumFlag(bool? para, bool? global, bool run) => para ?? global ?? run;

    /// <summary>Glyph + built-in color for a bullet style — the prefs UI reads this so the defaults
    /// live in exactly one place.</summary>
    public static (string Glyph, string Color)? BulletGlyphInfo(string style) =>
        BulletGlyphs.TryGetValue(style, out var g) ? g : null;
```

In `DrawBullet`'s `"num"` branch, REPLACE the four flag lines
(`bool bold = p.NumBold ?? fr?.Bold ?? false;` etc.) with:

```csharp
            bool bold = NumFlag(p.NumBold, NumBoldDefault, fr?.Bold ?? false);
            bool italic = NumFlag(p.NumItalic, NumItalicDefault, fr?.Italic ?? false);
            bool under = NumFlag(p.NumUnderline, NumUnderlineDefault, fr?.Underline ?? false);
            bool strike = NumFlag(p.NumStrike, NumStrikeDefault, fr?.Strike ?? false);
```

In the glyph branch, REPLACE `... new Typeface(GlyphFont), size, BrushFor(g.Color));` with:

```csharp
                FlowDirection.LeftToRight, new Typeface(GlyphFont), size,
                BrushFor(BulletColorOverrides.TryGetValue(p.Bullet, out var oc) ? oc : g.Color));
```

(Keep the surrounding `FormattedText` construction otherwise identical.)

- [ ] **Step 3: Editor selection API.** After `ApplyBullet` add:

```csharp
    /// <summary>Effective number-style flags at the selection start when it sits in a numbered list
    /// (override ?? global default ?? first text run), else null — drives the toolbar's number row.</summary>
    public (bool Bold, bool Italic, bool Underline, bool Strike)? CurrentNumStyle
    {
        get
        {
            var (a, _) = SelOrdered();
            _doc.Clamp(ref a);
            var p = _doc.Paragraphs[a.Para];
            if (p.Bullet != "num") return null;
            var fr = p.Runs.Count > 0 ? p.Runs[0] : null;
            return (NumFlag(p.NumBold, NumBoldDefault, fr?.Bold ?? false),
                    NumFlag(p.NumItalic, NumItalicDefault, fr?.Italic ?? false),
                    NumFlag(p.NumUnderline, NumUnderlineDefault, fr?.Underline ?? false),
                    NumFlag(p.NumStrike, NumStrikeDefault, fr?.Strike ?? false));
        }
    }

    /// <summary>Toggle one number-style flag ('b','i','u','s') for the whole numbered list at the
    /// selection start — the override becomes the opposite of the current effective state.</summary>
    public void ToggleNumStyle(char flag)
    {
        if (CurrentNumStyle is not { } cur) return;
        var (a, _) = SelOrdered();
        _doc.Clamp(ref a);
        bool eff = flag switch { 'b' => cur.Bold, 'i' => cur.Italic, 'u' => cur.Underline, _ => cur.Strike };
        PushUndo();
        _doc.SetNumFlag(a.Para, flag, !eff);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    /// <summary>Clear the list's overrides — numbers return to the global default / their text.</summary>
    public void ClearNumStyle()
    {
        if (CurrentBullet != "num") return;
        var (a, _) = SelOrdered();
        _doc.Clamp(ref a);
        PushUndo();
        foreach (char f in "bius") _doc.SetNumFlag(a.Para, f, null);
        _typingBurst = false;
        RaiseSelectionChanged();
    }
```

- [ ] **Step 4: MainView push-site.** In `MainView.axaml.cs`, add near `ApplyMotionPrefs`:

```csharp
    /// <summary>Push the bullet/number prefs onto the editor statics; optionally rebuild the open
    /// page so existing note boxes re-render with the new furniture.</summary>
    private void ApplyBulletPrefs(bool rebuild)
    {
        if (Vm is not { } vm) return;
        Editor.RichTextEditor.BulletColorOverrides.Clear();
        foreach (var style in new[] { "dot", "arrow", "star", "heart", "flower", "spark" })
            if (vm.BulletColorFor(style) is { } hex) Editor.RichTextEditor.BulletColorOverrides[style] = hex;
        Editor.RichTextEditor.NumBoldDefault = vm.NumBoldDefault;
        Editor.RichTextEditor.NumItalicDefault = vm.NumItalicDefault;
        Editor.RichTextEditor.NumUnderlineDefault = vm.NumUnderlineDefault;
        Editor.RichTextEditor.NumStrikeDefault = vm.NumStrikeDefault;
        if (rebuild) PageCanvas.Document = PageCanvas.Document;
    }
```

(`using Lumenotepad.Editor;` already present — then drop the `Editor.` qualifiers and write
`RichTextEditor.…` directly; verify and match the file.)

In `HookVm()`, add `ApplyBulletPrefs(rebuild: false);` as the FIRST line of the hooked-VM init chain
(BEFORE `SyncEditorDocument();` — the statics must be right before the first render).

In `OnVmPropertyChanged`, add a branch next to the motion one:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.BulletPrefsVersion)
                 or nameof(MainViewModel.NumBoldDefault) or nameof(MainViewModel.NumItalicDefault)
                 or nameof(MainViewModel.NumUnderlineDefault) or nameof(MainViewModel.NumStrikeDefault))
            ApplyBulletPrefs(rebuild: true);
```

- [ ] **Step 5:** Full suite → 90/90. Commit:
```bash
git add -A src tests
git commit -m "feat(m7p2): editor honors bullet-color overrides + number-style defaults; per-list toggle API"
```

---

## Task 4: Prefs UI — gated "Bullets & numbers" category

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: XAML.** In `NavList`, between the ADVANCED header item and the `Tag="data"` item:

```xml
                    <ListBoxItem Tag="bullets"><TextBlock Classes="label" Text="Bullets &amp; numbers"/></ListBoxItem>
```

Inside the panels `Panel`, before `DataPanel`:

```xml
                    <StackPanel x:Name="BulletsPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="BULLET COLORS" Margin="0,4,0,2"/>
                        <TextBlock Classes="hint"
                                   Text="Each bullet style carries its own color. Numbered lists and checklists follow the text and accent instead."/>
                        <StackPanel x:Name="BulletColorRows" Spacing="4" Margin="0,4,0,0"/>

                        <TextBlock Classes="section" Text="NUMBERED LISTS"/>
                        <TextBlock Classes="hint"
                                   Text="How numbers render by default. A single list can still be overridden from the toolbar's bullet menu."/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Bold numbers"/>
                            <ComboBox x:Name="NumBoldBox" Grid.Column="1" Width="130"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Italic numbers"/>
                            <ComboBox x:Name="NumItalicBox" Grid.Column="1" Width="130"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Underlined numbers"/>
                            <ComboBox x:Name="NumUnderlineBox" Grid.Column="1" Width="130"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Struck-through numbers"/>
                            <ComboBox x:Name="NumStrikeBox" Grid.Column="1" Width="130"/>
                        </Grid>
                    </StackPanel>
```

- [ ] **Step 2: Code-behind.** In `PreferencesWindow.axaml.cs`:

Register the panel: `["bullets"] = BulletsPanel,` in `_panels`.

Add the style table + builders to the class (glyph/name UI-local; the built-in default color comes
from `RichTextEditor.BulletGlyphInfo` so defaults live in one place — add `using Lumenotepad.Editor;`):

```csharp
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
            swatch.Flyout = BuildBulletColorFlyout(key, info.Color);
            Grid.SetColumn(swatch, 2);

            row.Children.Add(glyph);
            row.Children.Add(label);
            row.Children.Add(swatch);
            BulletColorRows.Children.Add(row);
        }
    }

    /// <summary>The palette flyout for one bullet style: 9 hue families × 5 shades + default reset.</summary>
    private Flyout BuildBulletColorFlyout(string styleKey, string defaultHex)
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
```

Combo wiring — add to the constructor (after MotionSpeedBox wiring):

```csharp
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
```

`SyncFromVm()` — before the final `UpdateGateVisuals();` add:

```csharp
        NumBoldBox.SelectedItem = NumOpt(vm.NumBoldDefault);
        NumItalicBox.SelectedItem = NumOpt(vm.NumItalicDefault);
        NumUnderlineBox.SelectedItem = NumOpt(vm.NumUnderlineDefault);
        NumStrikeBox.SelectedItem = NumOpt(vm.NumStrikeDefault);
        BuildBulletRows();
```

and the tiny mapper on the class:

```csharp
    private static string NumOpt(bool? v) => v switch { true => "Always on", false => "Always off", _ => "Match text" };
```

`OnVmChanged` — add a branch:

```csharp
        else if (e.PropertyName == nameof(MainViewModel.BulletPrefsVersion)) BuildBulletRows();
```

- [ ] **Step 3:** Build + full suite (90/90). Commit:
```bash
git add -A src
git commit -m "feat(m7p2): Bullets & numbers prefs — per-style color pickers + number-style defaults"
```

---

## Task 5: Toolbar — per-list number-style row in the bullet flyout

**Files:**
- Modify: `src/Lumenotepad/Views/FormatToolbar.axaml` + `.axaml.cs`

- [ ] **Step 1: XAML.** Replace the bullet flyout content (FormatToolbar.axaml:59-63):

```xml
                <Button.Flyout>
                    <Flyout Placement="Bottom">
                        <StackPanel Spacing="6">
                            <StackPanel Orientation="Horizontal" Spacing="4" x:Name="BulletChoices"/>
                            <!-- number-style override for the list under the caret (shown on "num") -->
                            <StackPanel Orientation="Horizontal" Spacing="4" x:Name="NumStylePanel" IsVisible="False"/>
                        </StackPanel>
                    </Flyout>
                </Button.Flyout>
```

- [ ] **Step 2: Code-behind.** In `FormatToolbar.axaml.cs`:

Fields + builder (call `BuildNumStyleRow();` right after the existing `BuildBulletChoices();` call):

```csharp
    private Button? _numB, _numI, _numU, _numS;

    /// <summary>The per-list number-style row: label + B/I/U/S toggles + "match text" reset. Lives in
    /// the bullet flyout, visible only when the caret sits in a numbered list; the flyout stays open
    /// so several flags can be flipped in a row.</summary>
    private void BuildNumStyleRow()
    {
        var label = new TextBlock
        {
            Text = "Numbers:", FontSize = 11.5, Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0),
        };
        NumStylePanel.Children.Add(label);

        Button Make(string text, char flag, string tip, FontWeight weight = FontWeight.Normal,
                    FontStyle style = FontStyle.Normal, TextDecorationCollection? deco = null)
        {
            var b = new Button
            {
                Width = 30, Height = 30, FontSize = 13, Theme = BoldBtn.Theme,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = text, FontWeight = weight, FontStyle = style, TextDecorations = deco,
                },
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => Do(e => e.ToggleNumStyle(flag));
            NumStylePanel.Children.Add(b);
            return b;
        }
        _numB = Make("B", 'b', "Bold numbers", weight: FontWeight.Bold);
        _numI = Make("I", 'i', "Italic numbers", style: FontStyle.Italic);
        _numU = Make("U", 'u', "Underlined numbers", deco: TextDecorations.Underline);
        _numS = Make("S", 's', "Struck-through numbers", deco: TextDecorations.Strikethrough);

        var reset = new Button
        {
            Height = 30, FontSize = 11.5, Theme = BoldBtn.Theme, Padding = new Thickness(8, 0),
            Content = "Match text", VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(reset, "Numbers follow their line's own formatting again");
        reset.Click += (_, _) => Do(e => e.ClearNumStyle());
        NumStylePanel.Children.Add(reset);
    }
```

In `UpdateFromEditor()` (inside the try block, after the `BulletBtn.Classes.Set` line):

```csharp
            NumStylePanel.IsVisible = _target.CurrentBullet == "num";
            if (_target.CurrentNumStyle is { } ns)
            {
                _numB?.Classes.Set("on", ns.Bold);
                _numI?.Classes.Set("on", ns.Italic);
                _numU?.Classes.Set("on", ns.Underline);
                _numS?.Classes.Set("on", ns.Strike);
            }
```

- [ ] **Step 3:** Build + full suite (90/90). Commit:
```bash
git add -A src
git commit -m "feat(m7p2): per-list number-style toggles in the toolbar's bullet flyout"
```

---

## Task 6: Final integration review + relaunch + owner checklist

- [ ] **Step 1:** Final reviewer over the whole Part 2 range (cross-task consistency: reset covers
the new prefs; ApplyBulletPrefs pushed before first render AND on change; statics never left stale
after reset; prefs rows rebuild on BulletPrefsVersion).
- [ ] **Step 2:** `(taskkill //F //IM Lumenotepad.exe 2>/dev/null; true) && dotnet build -v q && cmd //c start "" "src\Lumenotepad\bin\Debug\net10.0\Lumenotepad.exe"`
- [ ] **Step 3: Owner checklist** (real-app only):
1. Prefs window opens larger and resizes from any edge/corner (also verify the min size stops it).
2. ADVANCED now lists "Bullets & numbers" (gated by the same unlock).
3. Bullet colors: change Star to red → open a note with star bullets → they're red immediately;
   reset to default → back to gold; persists across restart.
4. Numbered lists: set "Bold numbers → Always on" → all numbers everywhere render bold (unless a
   list has its own override); "Match text" restores.
5. In the editor: caret inside a numbered list → bullet flyout shows the Numbers: B/I/U/S row →
   toggling flips the WHOLE list's numbers; "Match text" clears; undo (Ctrl+Z) reverts; the row is
   hidden for non-numbered paragraphs.
6. Reset settings (Data & tools) also clears bullet colors + number defaults.
