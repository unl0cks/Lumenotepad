# M8 Part 2 — Editor Defaults & Smart Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Default text font/size for notes, line/paragraph-spacing + indent scales, smart lists
(typing `1. `/`- `/`* ` auto-starts a list), and user-editable toolbar color/highlight palettes.

**Architecture:** Editor knobs extend the `ApplyEditorPrefs` statics-push; font/size/spacing are
read at NoteBoxView construction (rebuild applies changes). Palettes: `FormatToolbar` swaps its
static color arrays for instance lists fed by `SetPalettes(...)`; a `PalettePrefsVersion` channel
mirrors Font/BulletPrefsVersion. The smart-list conversion hooks `OnTextInput` after a space
lands, with the pure prefix-detection extracted and unit-tested.

**Spec:** Part 2 of `docs/superpowers/specs/2026-07-11-m8-customization-design.md`.

**Known facts (verified at HEAD `3e963fb`+docs, 102/102 green):**
- The Enter-on-empty-list-escapes behavior ALREADY EXISTS (RichTextEditor.cs:574-576) — smart
  lists only adds the typed-prefix conversion, gated by the new pref.
- `RichTextEditor.FontFamily`/`FontSize` are plain CLR props with defaults ("Segoe UI Variable
  Text, Segoe UI"; FontSize default is declared next to it — read it, it's the base for the pref
  default). `NoteBoxView`'s ctor (NoteCanvas.cs:258-264) builds the editor and is where the
  defaults get applied (like CaretBrush). `ParagraphSpacing { get; set; } = 4` (:29).
- `BuildLayout` (RichTextEditor.cs:167-180): empty paragraphs take the 4-arg TextLayout; run
  paragraphs use `GenericTextParagraphProperties(..., defaultProps, TextWrapping.Wrap,
  double.NaN /* lineHeight */, 0, 0)`.
- `IndentOf(Paragraph)` (:162): `BulletIndent * Math.Max(1, ScaleOf(p))`.
- `OnTextInput` (:527): sanitizes, `PushUndo(typing: !HasSelection)`, DeleteSelection, insert,
  (ends with AfterEdit — verify the tail).
- Toolbar palettes: `Highlights` (5 non-null hexes + null "None") and `TextColors` (6 non-null +
  null "Default") static arrays (FormatToolbar.axaml.cs:17-29); `BuildSwatches(panel, list,
  apply, ownerBtn)` builds chips in the ctor.
- RISK (line spacing): Avalonia's paragraph `lineHeight` is a FIXED height — mixed-size runs in
  one paragraph may clip/overlap if the height is computed from the wrong size. Mitigation: use
  the paragraph's LARGEST effective run size × 1.35 × scale, apply only when scale > 1.005
  (never compress below natural), and the owner checklist verifies mixed-size paragraphs.
- BUILD GOTCHA: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never
  launch GUI. CTOR-LOAD GOTCHA: hooks touching workspace/UI state guard `_workspace is null`.

---

## Task 1: Settings + VM (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test.** Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void EditorDefaultPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Null(d.EditorFont);
            Assert.Equal(15, d.EditorFontSize, 3);
            Assert.Equal(1.0, d.LineSpacingScale, 3);
            Assert.Equal(1.0, d.ParagraphSpacingScale, 3);
            Assert.Equal(1.0, d.IndentScale, 3);
            Assert.True(d.SmartLists);
            Assert.Empty(d.HighlightPalette);
            Assert.Empty(d.TextPalette);

            var s = new AppSettings
            {
                EditorFont = "Caveat", EditorFontSize = 18, LineSpacingScale = 1.4,
                ParagraphSpacingScale = 2.0, IndentScale = 1.5, SmartLists = false,
            };
            s.HighlightPalette.Add("#66FF0000");
            s.TextPalette.Add("#00FF00");
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("Caveat", loaded.EditorFont);
            Assert.Equal(18, loaded.EditorFontSize, 3);
            Assert.Equal(1.4, loaded.LineSpacingScale, 3);
            Assert.Equal(2.0, loaded.ParagraphSpacingScale, 3);
            Assert.Equal(1.5, loaded.IndentScale, 3);
            Assert.False(loaded.SmartLists);
            Assert.Equal(new[] { "#66FF0000" }, loaded.HighlightPalette);
            Assert.Equal(new[] { "#00FF00" }, loaded.TextPalette);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2:** Filtered run → compile FAIL.

- [ ] **Step 3: Settings fields.** In `AppSettings.cs` after `CardSize`:

```csharp
    public string? EditorFont { get; set; }                 // base note font; null = app default
    public double EditorFontSize { get; set; } = 15;        // base note size (11..24)
    public double LineSpacingScale { get; set; } = 1.0;     // 1..1.8 — lines within a paragraph
    public double ParagraphSpacingScale { get; set; } = 1.0;// 0.5..3 — the gap between paragraphs
    public double IndentScale { get; set; } = 1.0;          // 0.7..2 — bullet indent width
    public bool SmartLists { get; set; } = true;            // "1. "/"- "/"* " auto-start lists
    public List<string> HighlightPalette { get; set; } = new();  // empty = the built-in palette
    public List<string> TextPalette { get; set; } = new();       // empty = the built-in palette
```

Filtered run → PASS.

- [ ] **Step 4: VM plumbing.** After `_cardSize`:

```csharp
    [ObservableProperty] private string? _editorFont;               // prefs: null = app default
    [ObservableProperty] private double _editorFontSize = 15;
    [ObservableProperty] private double _lineSpacingScale = 1.0;
    [ObservableProperty] private double _paragraphSpacingScale = 1.0;
    [ObservableProperty] private double _indentScale = 1.0;
    [ObservableProperty] private bool _smartLists = true;
    /// <summary>Bumped whenever a toolbar palette changes — the toolbar rebuilds its swatches.</summary>
    [ObservableProperty] private int _palettePrefsVersion;
```

Ctor loads (after `CardSize = ...`) for the six scalar prefs (same pattern). Six plain guard-save
hooks after `OnCardSizeChanged` (standard body each). Palette accessors + ops near
`BulletColorFor` (note: EMPTY list = built-ins; the ops materialize the built-ins on first edit
so removing a default chip works):

```csharp
    /// <summary>The effective palette (empty stored list = the built-in defaults).</summary>
    public IReadOnlyList<string> PaletteFor(bool highlight, IReadOnlyList<string> builtIns) =>
        _settings is { } s && (highlight ? s.HighlightPalette : s.TextPalette) is { Count: > 0 } list
            ? list : builtIns;

    /// <summary>Add a color; seeds the stored list from the built-ins on first edit.</summary>
    public void AddPaletteColor(bool highlight, string hex, IReadOnlyList<string> builtIns)
    {
        if (_settings is null || _settingsDir is null) return;
        var list = highlight ? _settings.HighlightPalette : _settings.TextPalette;
        if (list.Count == 0) list.AddRange(builtIns);
        if (!list.Contains(hex, StringComparer.OrdinalIgnoreCase)) list.Add(hex);
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }

    /// <summary>Remove a color; seeds first like Add. The last chip cannot be removed.</summary>
    public void RemovePaletteColor(bool highlight, string hex, IReadOnlyList<string> builtIns)
    {
        if (_settings is null || _settingsDir is null) return;
        var list = highlight ? _settings.HighlightPalette : _settings.TextPalette;
        if (list.Count == 0) list.AddRange(builtIns);
        if (list.Count <= 1) return;
        list.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }

    /// <summary>Back to the built-ins (clears the stored list).</summary>
    public void ResetPalette(bool highlight)
    {
        if (_settings is null || _settingsDir is null) return;
        (highlight ? _settings.HighlightPalette : _settings.TextPalette).Clear();
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }
```

Reset additions (after the CardSize line): the six scalars via `d.`, plus:

```csharp
        if (_settings is not null && _settingsDir is not null
            && (_settings.HighlightPalette.Count > 0 || _settings.TextPalette.Count > 0))
        {
            _settings.HighlightPalette.Clear();
            _settings.TextPalette.Clear();
            _settings.Save(_settingsDir);
            PalettePrefsVersion++;
        }
```

- [ ] **Step 5: VM test.** Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void Palettes_SeedEditResetRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var builtIns = new[] { "#AAA111", "#BBB222" };
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Equal(builtIns, vm.PaletteFor(highlight: false, builtIns));

            vm.AddPaletteColor(false, "#CCC333", builtIns);
            Assert.Equal(new[] { "#AAA111", "#BBB222", "#CCC333" }, vm.PaletteFor(false, builtIns));
            Assert.Equal(3, AppSettings.Load(dir).TextPalette.Count);      // seeded + persisted

            vm.RemovePaletteColor(false, "#aaa111", builtIns);             // case-insensitive
            Assert.Equal(new[] { "#BBB222", "#CCC333" }, vm.PaletteFor(false, builtIns));

            vm.RemovePaletteColor(false, "#BBB222", builtIns);
            vm.RemovePaletteColor(false, "#CCC333", builtIns);             // last chip survives
            Assert.Single(vm.PaletteFor(false, builtIns));

            vm.ResetPalette(false);
            Assert.Equal(builtIns, vm.PaletteFor(false, builtIns));
            Assert.Empty(AppSettings.Load(dir).TextPalette);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 6:** Full suite → 104/104. Commit:
```bash
git add -A src tests
git commit -m "feat(m8): editor-default + palette settings — fonts, spacing scales, smart lists, palette ops"
```

---

## Task 2: Editor — defaults, spacing scales, smart lists

**Files:**
- Modify: `src/Lumenotepad/Editor/RichTextEditor.cs`
- Modify: `src/Lumenotepad/Editor/NoteCanvas.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Test: `tests/Lumenotepad.Tests/RichModelTests.cs` (pure prefix detection)

- [ ] **Step 1: Failing test** (pure smart-list prefix detection):

```csharp
    [Fact]
    public void SmartListKind_DetectsPrefixes()
    {
        Assert.Equal("num", RichTextEditor.SmartListKind("1."));
        Assert.Equal("dot", RichTextEditor.SmartListKind("-"));
        Assert.Equal("dot", RichTextEditor.SmartListKind("*"));
        Assert.Null(RichTextEditor.SmartListKind("2."));      // only "1." starts a list
        Assert.Null(RichTextEditor.SmartListKind("a."));
        Assert.Null(RichTextEditor.SmartListKind(""));
        Assert.Null(RichTextEditor.SmartListKind("hello -"));
    }
```

Filtered run → compile FAIL.

- [ ] **Step 2: Editor statics + pure helper.** In the prefs-statics region add:

```csharp
    /// <summary>Base font/size for note text (runs without their own font/size render in these).</summary>
    public static string? EditorFontPref;
    public static double EditorFontSizePref = 15;
    /// <summary>Layout scales (read at construction; canvas rebuild applies changes).</summary>
    public static double LineSpacingScalePref = 1.0;
    public static double ParagraphSpacingScalePref = 1.0;
    public static double IndentScalePref = 1.0;
    /// <summary>"1. "/"- "/"* " at a line start auto-starts a list.</summary>
    public static bool SmartListsPref = true;

    /// <summary>Pure prefix→list-kind detection for smart lists: the text BEFORE the just-typed
    /// space. Only exact "1." starts numbering (matching how lists renumber from 1).</summary>
    public static string? SmartListKind(string beforeCaret) => beforeCaret switch
    {
        "1." => "num",
        "-" or "*" => "dot",
        _ => null,
    };
```

Filtered test → PASS.

- [ ] **Step 3: Apply the scales.**
- `IndentOf(Paragraph)` (:162): `BulletIndent * Math.Max(1, ScaleOf(p))` →
  `BulletIndent * IndentScalePref * Math.Max(1, ScaleOf(p))`.
- `BuildLayout`: paragraph spacing stays (`ParagraphSpacing` is scaled at CONSTRUCTION, Step 4).
  For line spacing, in the runs branch compute the fixed line height only when scaled up:

```csharp
        double lineHeight = double.NaN;
        if (LineSpacingScalePref > 1.005 && p.Runs.Count > 0)
            lineHeight = p.Runs.Max(r => r.Size ?? FontSize) * 1.35 * LineSpacingScalePref;
        var paraProps = new GenericTextParagraphProperties(
            FlowDirection.LeftToRight, TextAlignment.Left, true, false,
            defaultProps, TextWrapping.Wrap, lineHeight, 0, 0);
```

(Replace the existing `double.NaN` argument; `using System.Linq;` is present. The empty-paragraph
branch stays natural — an empty line at scaled height is handled by the runs branch never running;
acceptable.)

- [ ] **Step 4: Construction reads in NoteBoxView** (NoteCanvas.cs:258-264) — extend the editor
initializer:

```csharp
        Editor = new RichTextEditor
        {
            Document = box.Doc, Margin = new Thickness(10, 3, 10, 9),
            Foreground = B(t.PaperText),
            CaretBrush = B(RichTextEditor.CaretColorOverride ?? t.Accent),
            SelectionBrush = B(t.FieldSelection),
            FontFamily = Services.AppFonts.Family(RichTextEditor.EditorFontPref),
            FontSize = Math.Clamp(RichTextEditor.EditorFontSizePref, 11, 24),
            ParagraphSpacing = 4 * Math.Clamp(RichTextEditor.ParagraphSpacingScalePref, 0.5, 3),
        };
```

(`AppFonts.Family(null)` returns the default family — null-safe by design. Match the file's
`Math` qualification style.)

- [ ] **Step 5: Smart-list conversion.** In `OnTextInput`, AFTER the insertion completes (find
where `_caret` has been advanced past the typed text; before/around `AfterEdit()`), add:

```csharp
        // Smart lists: a space completing "1."/"-"/"*" at the start of a plain paragraph turns
        // it into a real list (its own undo step — Ctrl+Z restores the typed prefix).
        if (SmartListsPref && text == " " && _caret.Off >= 2)
        {
            var para = _doc.Paragraphs[_caret.Para];
            if (para.Bullet is null && SmartListKind(para.Text[..(_caret.Off - 1)]) is { } kind)
            {
                PushUndo();
                _doc.DeleteRange(new DocPos(_caret.Para, 0), _caret);
                _caret = _anchor = new DocPos(_caret.Para, 0);
                _doc.SetBullet(_caret, _caret, kind);
            }
        }
```

Place it so `AfterEdit()` still runs afterwards (move/duplicate nothing — if the method's tail is
`AfterEdit();`, insert this block immediately before it). The `_caret.Off - 1` slice excludes the
just-typed space; `para.Text[..]` must cover from paragraph start, so this only fires when the
prefix IS the whole line before the space.

- [ ] **Step 6: ApplyEditorPrefs extension** (MainView.axaml.cs) — add to the method body:

```csharp
        RichTextEditor.EditorFontPref = vm.EditorFont;
        RichTextEditor.EditorFontSizePref = vm.EditorFontSize;
        RichTextEditor.LineSpacingScalePref = vm.LineSpacingScale;
        RichTextEditor.ParagraphSpacingScalePref = vm.ParagraphSpacingScale;
        RichTextEditor.IndentScalePref = vm.IndentScale;
        RichTextEditor.SmartListsPref = vm.SmartLists;
```

and extend its `OnVmPropertyChanged` branch with the six new property names (same `or nameof(...)`
list style).

- [ ] **Step 7:** Full suite → 105/105. Commit:
```bash
git add -A src tests
git commit -m "feat(m8): editor defaults, spacing/indent scales, smart lists"
```

---

## Task 3: Toolbar palettes + prefs UI

**Files:**
- Modify: `src/Lumenotepad/Views/FormatToolbar.axaml.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: Toolbar accepts palettes.** In `FormatToolbar.axaml.cs`:
- Expose the built-ins (the prefs UI + VM ops need them):

```csharp
    /// <summary>The built-in palettes (the "(none)" entry excluded) — prefs seeds edits from these.</summary>
    public static readonly string[] BuiltInHighlights =
        Highlights.Where(h => h.Hex is not null).Select(h => h.Hex!).ToArray();
    public static readonly string[] BuiltInTextColors =
        TextColors.Where(c => c.Hex is not null).Select(c => c.Hex!).ToArray();
```

(Static field initialization order: these MUST be declared AFTER the `Highlights`/`TextColors`
arrays in the file, or they initialize from null arrays. Verify placement.)

- Add a `SetPalettes` that rebuilds the two swatch panels from custom lists (names become the hex
strings for custom colors — tooltips only):

```csharp
    private System.Collections.Generic.IReadOnlyList<string>? _customHighlights, _customTextColors;

    /// <summary>Palette prefs: rebuild the highlight/text-color swatch rows. Null/empty = built-ins.</summary>
    public void SetPalettes(System.Collections.Generic.IReadOnlyList<string> highlights,
                            System.Collections.Generic.IReadOnlyList<string> textColors)
    {
        bool same = _customHighlights is not null && _customHighlights.SequenceEqual(highlights)
                 && _customTextColors is not null && _customTextColors.SequenceEqual(textColors);
        if (same) return;
        _customHighlights = highlights.ToList();
        _customTextColors = textColors.ToList();
        HighlightSwatches.Children.Clear();
        ColorSwatches.Children.Clear();
        BuildSwatches(HighlightSwatches,
            new[] { ((string?)null, "None") }.Concat(highlights.Select(h => ((string?)h, h))).ToArray(),
            hex => Do(e => e.ApplyHighlight(hex)), HighlightBtn);
        BuildSwatches(ColorSwatches,
            new[] { ((string?)null, "Default") }.Concat(textColors.Select(c => ((string?)c, c))).ToArray(),
            hex => Do(e => e.ApplyColor(hex)), ColorBtn);
    }
```

CHECK `BuildSwatches`' actual parameter types first (it takes the `(string? Hex, string Name)[]`
tuple array + the click action + owner button — match its real signature exactly; adapt the
concat expressions if the tuple element names differ). The ctor's original two `BuildSwatches`
calls stay (initial build with built-ins).

- [ ] **Step 2: MainView push.** In `HookVm()` (with the other pushes) and in a new branch:

```csharp
            Toolbar.SetPalettes(
                _hookedVm.PaletteFor(highlight: true, FormatToolbar.BuiltInHighlights),
                _hookedVm.PaletteFor(highlight: false, FormatToolbar.BuiltInTextColors));
```

```csharp
        else if (e.PropertyName == nameof(MainViewModel.PalettePrefsVersion))
        {
            if (Vm is { } pvm) Toolbar.SetPalettes(
                pvm.PaletteFor(true, FormatToolbar.BuiltInHighlights),
                pvm.PaletteFor(false, FormatToolbar.BuiltInTextColors));
        }
```

- [ ] **Step 3: Prefs UI.** In `EditorPanel` (PreferencesWindow.axaml), append a section:

```xml
                        <TextBlock Classes="section" Text="TEXT DEFAULTS"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Note font"/>
                                <TextBlock Classes="hint" Text="What unstyled note text renders in."/>
                            </StackPanel>
                            <ComboBox x:Name="EditorFontBox" Grid.Column="1" Width="170" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Note text size"/>
                            <TextBlock x:Name="EditorFontSizeValue" Grid.Column="1" Classes="label" Text="15"/>
                        </Grid>
                        <Slider x:Name="EditorFontSizeSlider" Minimum="11" Maximum="24"
                                TickFrequency="1" IsSnapToTickEnabled="True"/>

                        <TextBlock Classes="section" Text="SPACING"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Line spacing"/>
                            <TextBlock x:Name="LineSpacingValue" Grid.Column="1" Classes="hint" Text="1.0×"/>
                        </Grid>
                        <Slider x:Name="LineSpacingSlider" Minimum="1" Maximum="1.8"
                                TickFrequency="0.1" IsSnapToTickEnabled="True"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Paragraph spacing"/>
                            <TextBlock x:Name="ParaSpacingValue" Grid.Column="1" Classes="hint" Text="1.0×"/>
                        </Grid>
                        <Slider x:Name="ParaSpacingSlider" Minimum="0.5" Maximum="3"
                                TickFrequency="0.25" IsSnapToTickEnabled="True"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="List indent"/>
                            <TextBlock x:Name="IndentScaleValue" Grid.Column="1" Classes="hint" Text="1.0×"/>
                        </Grid>
                        <Slider x:Name="IndentScaleSlider" Minimum="0.7" Maximum="2"
                                TickFrequency="0.1" IsSnapToTickEnabled="True"/>

                        <TextBlock Classes="section" Text="SMART INPUT"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Smart lists"/>
                                <TextBlock Classes="hint" Text="Typing '1. ', '- ' or '* ' at a line start begins a real list."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding SmartLists, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <TextBlock Classes="section" Text="TOOLBAR PALETTES"/>
                        <TextBlock Classes="hint" Text="The color rows the toolbar offers. Right-click a chip to remove it."/>
                        <Grid ColumnDefinitions="90,*,Auto">
                            <TextBlock Classes="label" Text="Text colors"/>
                            <WrapPanel x:Name="TextPaletteChips" Grid.Column="1" VerticalAlignment="Center"/>
                            <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                                <TextBox x:Name="TextPaletteHexBox" Width="92" FontSize="12" PlaceholderText="#RRGGBB"/>
                                <Button x:Name="TextPaletteReset" Content="Reset" FontSize="11.5"/>
                            </StackPanel>
                        </Grid>
                        <Grid ColumnDefinitions="90,*,Auto">
                            <TextBlock Classes="label" Text="Highlights"/>
                            <WrapPanel x:Name="HighlightPaletteChips" Grid.Column="1" VerticalAlignment="Center"/>
                            <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                                <TextBox x:Name="HighlightPaletteHexBox" Width="92" FontSize="12" PlaceholderText="#RRGGBB"/>
                                <Button x:Name="HighlightPaletteReset" Content="Reset" FontSize="11.5"/>
                            </StackPanel>
                        </Grid>
```

- [ ] **Step 4: Prefs code-behind.** Constructor wiring:

```csharp
        EditorFontBox.ItemsSource = new[] { "(Default)" }.Concat(AppFonts.ListNames(Vm?.ExtendedFonts ?? false)).ToArray();
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
```

Class helpers:

```csharp
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
```

`OnVmChanged` branch: `else if (e.PropertyName == nameof(MainViewModel.PalettePrefsVersion))
{ BuildPaletteChips(TextPaletteChips, false); BuildPaletteChips(HighlightPaletteChips, true); }`

`SyncFromVm` additions: EditorFontBox.SelectedItem (map null→"(Default)"), the four sliders +
labels, and both `BuildPaletteChips` calls. (Uses `using Lumenotepad.Views;`-visible
`FormatToolbar` statics — PreferencesWindow is in the same namespace, no using needed.)

- [ ] **Step 5:** Full suite → 105/105. Commit:
```bash
git add -A src
git commit -m "feat(m8): editable toolbar palettes + editor-default rows in the Editor category"
```

---

## Task 4: Final integration review + relaunch + owner checklist

- [ ] **Step 1:** Final reviewer over the Part 2 range. Extra seams: static-init ORDER of
BuiltInHighlights vs Highlights; smart-list conversion undo granularity + interaction with the
typing burst; line-height risk with mixed-size runs (flag for the owner test); PaletteFor seeding
semantics (empty = built-ins) consistent across VM/toolbar/prefs; EditorFontBox listing vs the
fonts-curation blocklist (candidates list is fine unfiltered? judge).
- [ ] **Step 2:** Rebuild + relaunch.
- [ ] **Step 3: Owner checklist:**
1. Editor → Note font/size: unstyled text in ALL notes re-renders; explicitly-styled runs keep
   their font.
2. Spacing sliders: line spacing (test a paragraph with MIXED text sizes — watch for clipping!),
   paragraph spacing, list indent — all live.
3. Smart lists ON: type `1. ` at a line start → numbered list starts, prefix vanishes; `- ` or
   `* ` → bullet; Ctrl+Z restores the typed text; toggle OFF disables it; Enter on an empty list
   line still exits the list.
4. Toolbar palettes: add a custom text color (hex + Enter) → it appears in the toolbar's color
   flyout; right-click a chip removes it (last chip refuses); Reset restores the built-ins.
5. Reset settings restores everything.
