# M8 Part 1b — Personal Touches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the nine "personal touches": caret color/width/blink, default highlight color,
insert date/time (Ctrl+Shift+T + format pref), default container width, accent-follows-notebook,
greeting personalization (+ hideable stats), gallery card size, and a Shortcuts reference category.

**Architecture:** Established patterns throughout. Editor knobs ride the statics-push
(`MainView.ApplyEditorPrefs` — new sibling of ApplyBulletPrefs/ApplyMotionPrefs). Card size =
three `DynamicResource` doubles set by MainView. Accent-follows-notebook slots into
`MainWindow.ApplyTheme`'s existing seed-selection. Greeting becomes an observable refreshed by
`RefreshHome`. New UNGATED prefs categories: **Editor** (between Canvas and ADVANCED) and
**Shortcuts** (after Editor).

**Spec:** Part 1b table in `docs/superpowers/specs/2026-07-11-m8-customization-design.md`.
One honest adjustment vs the spec table: containers have AUTO height by design (`NoteBox.H = 0`
= content height), so the pref is **width only** (`NewNoteWidth`), not width+height.

**Known facts (verified at `98ff2da`, 100/100 green):**
- Editor caret: `RichTextEditor.CaretBrush` public property (default accent), SET by
  `NoteCanvas.cs:262` (`CaretBrush = B(t.Accent)`) at editor construction. Caret rect width is a
  `1.6` literal at RichTextEditor.cs:300 (render) and :440 (fallback rect). Blink:
  `AnimTick` (~line 872) computes `double op = gliding ? 1.0 : BlinkOpacity(_blinkMs);`.
- Default highlight: `ToggleDefaultHighlight` (RichTextEditor.cs:743) has `const string yellow =
  "#66FFD666";` — bound to `Key.H when ctrl && shift` (:610). The toolbar's `Highlights` palette
  (FormatToolbar.axaml.cs:17) lists the offerable hexes.
- Keyboard: the editor's key switch has `case Key.H when ctrl && shift:` — Ctrl+Shift+T goes next
  to it. For INSERTING text, mirror the editor's existing paste path (grep `Paste` /
  `OnTextInput` in RichTextEditor.cs — reuse its PushUndo + delete-selection + InsertText +
  caret/anchor + invalidate flow; if no reusable private helper exists, extract one).
- New containers: `CanvasDocument.AddBox(x, y, width = NoteBox.DefaultWidth /* 360 */)`; the sole
  creation call site is `NoteCanvas.cs:141` (`_doc.AddBox(p.X - 11, p.Y - 16)`).
- Gallery card: `MainView.axaml:315` `<Border Classes="nbcard" Width="196" Height="132" …>` inside
  the HomeCards ItemTemplate. Cover snapshot/drag code is size-agnostic (RenderTargetBitmap of the
  live card).
- Greeting: `MainViewModel.Greeting { get; } = BuildGreeting();` (static, computed once) +
  `_homeSubtitle` observable rebuilt in `RefreshHome`. XAML binds `{Binding Greeting}`.
- `MainWindow.ApplyTheme` picks the accent seed from `NormalizeHex(vm.CustomAccent)`;
  `Notebook.Color` is `#RRGGBB`. MainWindow subscribes to the VM before MainView (established).
- Prefs shell: `_panels` dict + Tag-keyed nav; ungated categories need no gate work
  (`IsGated` lists only data/bullets/fonts).
- BUILD GOTCHA: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never
  launch the GUI from a subagent. Ctor-load gotcha: OnXChanged hooks fire during the ctor
  settings-load for non-default persisted values — hooks touching workspace/UI state MUST guard
  (`_workspace is null`), see the RecentCount incident.

---

## Task 1: Settings + VM (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing settings test.** Add to `AppSettingsTests.cs`:

```csharp
    [Fact]
    public void PersonalTouchPrefs_DefaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.Null(d.CaretColor);
            Assert.Equal(1.6, d.CaretWidth, 3);
            Assert.True(d.CaretBlink);
            Assert.Equal("#66FFD666", d.DefaultHighlight);
            Assert.Equal("yyyy-MM-dd", d.DateFormat);
            Assert.Equal(360, d.NewNoteWidth, 3);
            Assert.False(d.AccentFollowsNotebook);
            Assert.Equal("", d.UserName);
            Assert.True(d.ShowHomeStats);
            Assert.Equal("Medium", d.CardSize);

            var s = new AppSettings
            {
                CaretColor = "#FF0000", CaretWidth = 2.5, CaretBlink = false,
                DefaultHighlight = "#6699E28A", DateFormat = "HH:mm", NewNoteWidth = 480,
                AccentFollowsNotebook = true, UserName = "Sam", ShowHomeStats = false,
                CardSize = "Large",
            };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);
            Assert.Equal("#FF0000", loaded.CaretColor);
            Assert.Equal(2.5, loaded.CaretWidth, 3);
            Assert.False(loaded.CaretBlink);
            Assert.Equal("#6699E28A", loaded.DefaultHighlight);
            Assert.Equal("HH:mm", loaded.DateFormat);
            Assert.Equal(480, loaded.NewNoteWidth, 3);
            Assert.True(loaded.AccentFollowsNotebook);
            Assert.Equal("Sam", loaded.UserName);
            Assert.False(loaded.ShowHomeStats);
            Assert.Equal("Large", loaded.CardSize);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2:** Filtered run → compile FAIL.

- [ ] **Step 3: Settings fields.** In `AppSettings.cs` after `AlwaysOnTop`:

```csharp
    public string? CaretColor { get; set; }                 // null = the theme accent
    public double CaretWidth { get; set; } = 1.6;           // 1..3 px
    public bool CaretBlink { get; set; } = true;
    public string DefaultHighlight { get; set; } = "#66FFD666";  // Ctrl+Shift+H color
    public string DateFormat { get; set; } = "yyyy-MM-dd";  // Ctrl+Shift+T insert format
    public double NewNoteWidth { get; set; } = 360;         // new container width (height is auto)
    public bool AccentFollowsNotebook { get; set; }         // inside a notebook, its color tints the UI
    public string UserName { get; set; } = "";              // greeting name; "" = plain greeting
    public bool ShowHomeStats { get; set; } = true;         // notebook/page counts under the greeting
    public string CardSize { get; set; } = "Medium";        // gallery covers: Small | Medium | Large
```

Filtered run → PASS.

- [ ] **Step 4: VM plumbing.** In `MainViewModel.cs` after `_alwaysOnTop`:

```csharp
    [ObservableProperty] private string? _caretColor;               // prefs: null = accent
    [ObservableProperty] private double _caretWidth = 1.6;
    [ObservableProperty] private bool _caretBlink = true;
    [ObservableProperty] private string _defaultHighlight = "#66FFD666";
    [ObservableProperty] private string _dateFormat = "yyyy-MM-dd";
    [ObservableProperty] private double _newNoteWidth = 360;
    [ObservableProperty] private bool _accentFollowsNotebook;
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private bool _showHomeStats = true;
    [ObservableProperty] private string _cardSize = "Medium";       // Small | Medium | Large
```

Ctor load block (after `AlwaysOnTop = ...`):

```csharp
            CaretColor = _settings.CaretColor;
            CaretWidth = _settings.CaretWidth;
            CaretBlink = _settings.CaretBlink;
            DefaultHighlight = _settings.DefaultHighlight;
            DateFormat = _settings.DateFormat;
            NewNoteWidth = _settings.NewNoteWidth;
            AccentFollowsNotebook = _settings.AccentFollowsNotebook;
            UserName = _settings.UserName;
            ShowHomeStats = _settings.ShowHomeStats;
            CardSize = _settings.CardSize;
```

Ten guard-save hooks after `OnAlwaysOnTopChanged` — nine are the plain pattern
(`OnCaretColorChanged`/`OnCaretWidthChanged`/`OnCaretBlinkChanged`/`OnDefaultHighlightChanged`/
`OnDateFormatChanged`/`OnNewNoteWidthChanged`/`OnAccentFollowsNotebookChanged`/
`OnCardSizeChanged` take the standard `if (_settings is null || _settingsDir is null) return;
_settings.X = value; _settings.Save(_settingsDir);` body). TWO differ (ctor-load gotcha —
they touch home-surface state):

```csharp
    partial void OnUserNameChanged(string value)
    {
        if (_workspace is not null) Greeting = BuildGreeting(value);   // live greeting refresh
        if (_settings is null || _settingsDir is null) return;
        _settings.UserName = value;
        _settings.Save(_settingsDir);
    }

    partial void OnShowHomeStatsChanged(bool value)
    {
        if (_workspace is not null) RefreshHome();                     // subtitle swaps live
        if (_settings is null || _settingsDir is null) return;
        _settings.ShowHomeStats = value;
        _settings.Save(_settingsDir);
    }
```

- [ ] **Step 5: Greeting becomes observable + personalized; stats hideable.**

REPLACE `public string Greeting { get; } = BuildGreeting();` with:

```csharp
    [ObservableProperty] private string _greeting = BuildGreeting("");
```

REPLACE the static `BuildGreeting()` with:

```csharp
    private static string BuildGreeting(string name)
    {
        var now = DateTime.Now;
        var word = now.Hour < 5 ? "Up late" : now.Hour < 12 ? "Good morning"
                 : now.Hour < 18 ? "Good afternoon" : "Good evening";
        var who = string.IsNullOrWhiteSpace(name) ? "" : $", {name.Trim()}";
        return $"{word}{who} — it's {now:dddd, MMMM d}";
    }
```

In `RefreshHome()`: first line becomes `Greeting = BuildGreeting(UserName);` and the subtitle
assignment at the end becomes:

```csharp
        HomeSubtitle = ShowHomeStats
            ? $"{Notebooks.Count} {(Notebooks.Count == 1 ? "notebook" : "notebooks")} · " +
              $"{pages} {(pages == 1 ? "page" : "pages")} — pick up where you left off."
            : "Pick up where you left off, or start something new.";
```

(Keep the `pages` computation; if ShowHomeStats is false it's unused but harmless — or guard it,
implementer's choice, no behavioral difference.)

In `ResetSettingsToDefaults()` append:

```csharp
        CaretColor = d.CaretColor; CaretWidth = d.CaretWidth; CaretBlink = d.CaretBlink;
        DefaultHighlight = d.DefaultHighlight; DateFormat = d.DateFormat;
        NewNoteWidth = d.NewNoteWidth; AccentFollowsNotebook = d.AccentFollowsNotebook;
        UserName = d.UserName; ShowHomeStats = d.ShowHomeStats; CardSize = d.CardSize;
```

- [ ] **Step 6: VM test.** Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void Greeting_PersonalizesAndRefreshes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            new AppSettings { UserName = "Sam", ShowHomeStats = false }.Save(dir);
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);   // must not crash (ctor gotcha)
            Assert.Contains(", Sam —", vm.Greeting);
            Assert.DoesNotContain("notebook", vm.HomeSubtitle);

            vm.UserName = "";
            Assert.DoesNotContain(",", vm.Greeting.Split('—')[0]);      // plain greeting again

            vm.ShowHomeStats = true;
            Assert.Contains("notebook", vm.HomeSubtitle);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 7:** Full suite → 102/102. Commit:
```bash
git add -A src tests
git commit -m "feat(m8): personal-touch settings — caret, highlight, date format, container width, greeting, cards"
```

---

## Task 2: Editor statics + date insert + container width

**Files:**
- Modify: `src/Lumenotepad/Editor/RichTextEditor.cs`
- Modify: `src/Lumenotepad/Editor/NoteCanvas.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1: Editor statics.** In `RichTextEditor.cs`, next to the bullet-prefs statics add:

```csharp
    // ---- prefs: caret + highlight + date insert (pushed by MainView.ApplyEditorPrefs) ----
    /// <summary>Caret color override (null = the theme accent picked up at construction).</summary>
    public static string? CaretColorOverride;
    public static double CaretWidthPref = 1.6;
    public static bool CaretBlinkPref = true;
    /// <summary>The Ctrl+Shift+H highlight color.</summary>
    public static string DefaultHighlightPref = "#66FFD666";
    /// <summary>The Ctrl+Shift+T timestamp format.</summary>
    public static string DateFormatPref = "yyyy-MM-dd";
```

Apply them:
- Render caret (line ~300): `new Rect(r.X, r.Y, 1.6, r.Height)` → `new Rect(r.X, r.Y, CaretWidthPref, r.Height)`.
  Same substitution in the `CaretRect` fallback at ~440 (both `1.6` literals).
- Blink (AnimTick ~872): `double op = gliding ? 1.0 : BlinkOpacity(_blinkMs);` →
  `double op = gliding || !CaretBlinkPref ? 1.0 : BlinkOpacity(_blinkMs);`
- `ToggleDefaultHighlight` (~743): delete the `const string yellow = "#66FFD666";` line and use
  `DefaultHighlightPref` where `yellow` was used (keep the toggle-off logic identical).

- [ ] **Step 2: Insert date/time.** In the key switch, next to `case Key.H when ctrl && shift:` add:

```csharp
            case Key.T when ctrl && shift:
                InsertDateTime();
                e.Handled = true;
                return;
```

(Match the surrounding cases' exact style for handling/return.) Add the method near
`ToggleDefaultHighlight`, reusing the editor's existing insert flow — find the paste/text-input
path (grep `Paste`, `OnTextInput`) and mirror it exactly (PushUndo, replace selection, insert at
caret with the caret's current format, move caret, RaiseSelectionChanged/invalidate). Shape:

```csharp
    /// <summary>Ctrl+Shift+T: insert the current date/time at the caret using the format pref.</summary>
    public void InsertDateTime()
    {
        string stamp;
        try { stamp = System.DateTime.Now.ToString(DateFormatPref); }
        catch (System.FormatException) { stamp = System.DateTime.Now.ToString("yyyy-MM-dd"); }
        // …then EXACTLY the same steps the paste path performs with its text.
    }
```

If the paste flow can't be reused without duplicating >10 lines, extract a private
`InsertPlainText(string text)` used by both. If you cannot find the paste path or it's structured
in a way that makes this ambiguous, report NEEDS_CONTEXT with the relevant excerpt.

- [ ] **Step 3: Caret color + container width in NoteCanvas.** Line ~262:
`CaretBrush = B(t.Accent),` → `CaretBrush = B(RichTextEditor.CaretColorOverride ?? t.Accent),`.
Line ~141: `_doc.AddBox(p.X - 11, p.Y - 16)` →
`_doc.AddBox(p.X - 11, p.Y - 16, System.Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640))`
— which needs one more static in RichTextEditor's prefs block:

```csharp
    /// <summary>Width for freshly created note containers (height stays content-auto).</summary>
    public static double NewNoteWidthPref = 360;
```

(NoteCanvas has no `using System;` if it qualifies Math elsewhere — match the file.)

- [ ] **Step 4: MainView push-site.** Next to `ApplyBulletPrefs` add:

```csharp
    /// <summary>Push the editor prefs onto the shared statics; optionally rebuild so open note
    /// boxes pick up caret color/width changes immediately.</summary>
    private void ApplyEditorPrefs(bool rebuild)
    {
        if (Vm is not { } vm) return;
        RichTextEditor.CaretColorOverride = Services.ThemePalettes.NormalizeHex(vm.CaretColor);
        RichTextEditor.CaretWidthPref = System.Math.Clamp(vm.CaretWidth, 1, 3);
        RichTextEditor.CaretBlinkPref = vm.CaretBlink;
        RichTextEditor.DefaultHighlightPref = vm.DefaultHighlight;
        RichTextEditor.DateFormatPref = vm.DateFormat;
        RichTextEditor.NewNoteWidthPref = vm.NewNoteWidth;
        if (rebuild) PageCanvas.Document = PageCanvas.Document;
    }
```

`HookVm()`: add `ApplyEditorPrefs(rebuild: false);` right after `ApplyBulletPrefs(rebuild: false);`.
`OnVmPropertyChanged`: add a branch next to the bullet one:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.CaretColor) or nameof(MainViewModel.CaretWidth)
                 or nameof(MainViewModel.CaretBlink) or nameof(MainViewModel.DefaultHighlight)
                 or nameof(MainViewModel.DateFormat) or nameof(MainViewModel.NewNoteWidth))
            ApplyEditorPrefs(rebuild: true);
```

(DateFormat/NewNoteWidth don't strictly need the rebuild but sharing the branch is fine.)

- [ ] **Step 5:** Full suite → 102/102. Commit:
```bash
git add -A src
git commit -m "feat(m8): caret color/width/blink, default highlight, Ctrl+Shift+T timestamp, container width"
```

---

## Task 3: Accent-follows-notebook + card size + greeting bindings

**Files:**
- Modify: `src/Lumenotepad/Views/MainWindow.axaml.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml` + `.axaml.cs`

- [ ] **Step 1: Accent follows notebook.** In `MainWindow.ApplyTheme`, replace the accent-seed
lines with:

```csharp
        var tokens = Services.ThemePalettes.Resolve(vm.Theme, vm.FullTheme, vm.PaperLight);
        // Inside a notebook, its cover color can drive the whole accent (pref); otherwise the
        // user's custom accent; otherwise the theme's own.
        string? seed = vm.AccentFollowsNotebook && !vm.IsHomeVisible && vm.SelectedNotebook is { } nb
            ? Services.ThemePalettes.NormalizeHex(nb.Color)
            : Services.ThemePalettes.NormalizeHex(vm.CustomAccent);
        if (seed is { } accent) tokens = Services.ThemePalettes.WithAccent(tokens, accent);
        Services.ThemeManager.Apply(app, tokens);
```

Extend `OnThemePropertyChanged`'s ApplyTheme filter with:
`or nameof(ViewModels.MainViewModel.AccentFollowsNotebook)
or nameof(ViewModels.MainViewModel.SelectedNotebook)
or nameof(ViewModels.MainViewModel.IsHomeVisible)`.

In `MainView.OnVmPropertyChanged`, extend the theme-rebuild branch (Theme/FullTheme/PaperLight/
CustomAccent) with `or nameof(MainViewModel.AccentFollowsNotebook)` ONLY — SelectedNotebook and
IsHomeVisible already have their own handling there and adding them to the rebuild branch would
double-rebuild. The existing posted `ApplyGlassTint` in that branch is unaffected.

- [ ] **Step 2: Card size.** In `MainView.axaml:315`, change the card Border to:

```xml
                                <Border Classes="nbcard"
                                        Width="{DynamicResource NbCardWidth}" Height="{DynamicResource NbCardHeight}"
```

(rest of the element unchanged). In `MainView.axaml.cs` add near ApplyGlossyAccents:

```csharp
    /// <summary>Gallery card size pref → the DynamicResource doubles the card template consumes.</summary>
    private void ApplyCardSize()
    {
        var (w, h) = (Vm?.CardSize) switch
        {
            "Small" => (156.0, 104.0),
            "Large" => (236.0, 160.0),
            _ => (196.0, 132.0),
        };
        Resources["NbCardWidth"] = w;
        Resources["NbCardHeight"] = h;
    }
```

Call `ApplyCardSize();` in `HookVm()` (with the other Apply* calls) and add the branch:

```csharp
        else if (e.PropertyName == nameof(MainViewModel.CardSize))
            ApplyCardSize();
```

IMPORTANT: `Resources["NbCardWidth"]` must exist before first bind — ALSO seed the two resources
in `MainView.axaml`'s `<UserControl.Resources>` (create the section if missing, right after the
opening UserControl tag / before Styles):

```xml
    <UserControl.Resources>
        <x:Double x:Key="NbCardWidth">196</x:Double>
        <x:Double x:Key="NbCardHeight">132</x:Double>
    </UserControl.Resources>
```

- [ ] **Step 3: Greeting binding sanity.** `Greeting` changed from get-only to observable in
Task 1 — verify the homepage XAML binding (`{Binding Greeting}`) needs no change (it doesn't;
just confirm it compiles and no `x:Static`/OneTime quirk exists).

- [ ] **Step 4:** Full suite → 102/102. Commit:
```bash
git add -A src
git commit -m "feat(m8): accent follows notebook color, gallery card size, live greeting"
```

---

## Task 4: Prefs UI — Editor + Shortcuts categories, rows in General/Appearance/Canvas

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: Nav + panels.** Add UNGATED nav items after `Tag="canvas"` (before the ADVANCED
header): `<ListBoxItem Tag="editor">…"Editor"…` and `<ListBoxItem Tag="shortcuts">…"Shortcuts"…`
(same one-line TextBlock pattern as the others). Register `["editor"] = EditorPanel,` and
`["shortcuts"] = ShortcutsPanel,` in `_panels`.

- [ ] **Step 2: EditorPanel XAML** (new StackPanel, `IsVisible="False"`, before ADVANCED panels):

```xml
                    <StackPanel x:Name="EditorPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="CARET" Margin="0,4,0,2"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Caret color"/>
                                <TextBlock Classes="hint" Text="Blank hex returns to the accent color."/>
                            </StackPanel>
                            <TextBox x:Name="CaretColorBox" Grid.Column="1" Width="110" FontSize="12.5"
                                     PlaceholderText="#RRGGBB" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Caret width"/>
                            <TextBlock x:Name="CaretWidthValue" Grid.Column="1" Classes="hint" Text="1.6"/>
                        </Grid>
                        <Slider x:Name="CaretWidthSlider" Minimum="1" Maximum="3"
                                TickFrequency="0.1" IsSnapToTickEnabled="True"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Caret blinks"/>
                                <TextBlock Classes="hint" Text="Off keeps a steady caret (it still glides)."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding CaretBlink, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>

                        <TextBlock Classes="section" Text="WRITING"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Quick highlight color"/>
                                <TextBlock Classes="hint" Text="What Ctrl+Shift+H applies."/>
                            </StackPanel>
                            <StackPanel x:Name="HighlightChoices" Grid.Column="1" Orientation="Horizontal"
                                        Spacing="6" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Date &amp; time format"/>
                                <TextBlock Classes="hint" Text="What Ctrl+Shift+T inserts at the caret."/>
                            </StackPanel>
                            <ComboBox x:Name="DateFormatBox" Grid.Column="1" Width="170" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>
```

- [ ] **Step 3: ShortcutsPanel XAML** — a static two-column reference. Build the rows in
CODE-BEHIND from a table so it stays maintainable:

```xml
                    <StackPanel x:Name="ShortcutsPanel" Spacing="6" IsVisible="False">
                        <TextBlock Classes="section" Text="KEYBOARD SHORTCUTS" Margin="0,4,0,2"/>
                        <TextBlock Classes="hint" Text="Everything the keyboard can do today. Custom bindings come later."/>
                        <StackPanel x:Name="ShortcutRows" Spacing="2" Margin="0,4,0,0"/>
                    </StackPanel>
```

Code-behind: BEFORE building the table, grep the REAL shortcuts (`Key\.` cases in
`RichTextEditor.cs`'s key switch + any `KeyDown` handlers in MainView/PreferencesWindow) and list
exactly what exists — do NOT invent bindings. Then:

```csharp
    private void BuildShortcutRows()
    {
        if (ShortcutRows.Children.Count > 0) return;      // static — build once
        foreach (var (keys, what) in new[]
        {
            // FILL FROM THE GREP — e.g.:
            ("Ctrl+B / I / U", "Bold / italic / underline"),
            ("Ctrl+Shift+S", "Strikethrough"),
            ("Ctrl+Shift+H", "Quick highlight"),
            ("Ctrl+Shift+8", "Bullet list"),
            ("Ctrl+Shift+7", "Numbered list"),
            ("Ctrl+Shift+T", "Insert date & time"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo"),
            ("Escape", "Close dialogs and the preferences window"),
        })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            var k = new TextBlock { Text = keys, FontSize = 12.5, FontWeight = Avalonia.Media.FontWeight.SemiBold };
            var w = new TextBlock { Text = what, FontSize = 12.5, Opacity = 0.8 };
            Grid.SetColumn(w, 1);
            row.Children.Add(k);
            row.Children.Add(w);
            ShortcutRows.Children.Add(row);
        }
    }
```

VERIFY each row against the code (the list above is indicative; correct it to match reality —
e.g. confirm whether numbered list is Ctrl+Shift+7, what clipboard/caret-nav shortcuts exist and
are worth listing). Call `BuildShortcutRows();` from the nav handler when `key == "shortcuts"`.

- [ ] **Step 4: Rows in existing panels.**
- `AppearancePanel` — after the MOTION rows:

```xml
                        <TextBlock Classes="section" Text="GALLERY"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Accent follows notebook"/>
                                <TextBlock Classes="hint" Text="Inside a notebook, its cover color tints the whole app."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding AccentFollowsNotebook, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Cover size"/>
                            <ComboBox x:Name="CardSizeBox" Grid.Column="1" Width="120" VerticalAlignment="Center"/>
                        </Grid>
```

- `GeneralPanel` — in the HOMEPAGE section (after the recents slider):

```xml
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Greet me as"/>
                                <TextBlock Classes="hint" Text="Blank keeps the plain greeting."/>
                            </StackPanel>
                            <TextBox x:Name="UserNameBox" Grid.Column="1" Width="150" FontSize="12.5"
                                     VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="Show notebook &amp; page counts"/>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding ShowHomeStats, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
```

- `CanvasPanel` — after the deleted-history row:

```xml
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Classes="label" Text="New container width"/>
                            <TextBlock x:Name="NewNoteWidthValue" Grid.Column="1" Classes="label" Text="360"/>
                        </Grid>
                        <Slider x:Name="NewNoteWidthSlider" Minimum="240" Maximum="640"
                                TickFrequency="20" IsSnapToTickEnabled="True"/>
```

- [ ] **Step 5: Code-behind wiring** (constructor, mirroring the established patterns):

```csharp
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
        DateFormatBox.ItemsSource = DateFormats.Select(f => System.DateTime.Now.ToString(f)).ToArray();
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
```

Class members:

```csharp
    /// <summary>The Ctrl+Shift+T format presets (shown rendered with today's date).</summary>
    private static readonly string[] DateFormats =
        { "yyyy-MM-dd", "MMMM d, yyyy", "dd/MM/yyyy", "yyyy-MM-dd HH:mm", "HH:mm" };

    /// <summary>The quick-highlight choices — the toolbar's own highlight palette.</summary>
    private void BuildHighlightChoices()
    {
        foreach (var hex in new[] { "#66FFD666", "#6699E28A", "#66FF8FAB", "#6690CAF9" })
        {
            var chip = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#66808080")), BorderThickness = new Thickness(1),
            };
            chip.PointerPressed += (_, _) => { if (Vm is { } vm) vm.DefaultHighlight = hex; UpdateHighlightRings(); };
            HighlightChoices.Children.Add(chip);
        }
        UpdateHighlightRings();
    }

    private void UpdateHighlightRings()
    {
        foreach (var child in HighlightChoices.Children)
            if (child is Border b && b.Background is SolidColorBrush sb)
                b.BorderThickness = new Thickness(
                    string.Equals(Vm?.DefaultHighlight, $"#{sb.Color.ToUInt32():X8}"[..3] + $"{sb.Color.R:X2}{sb.Color.G:X2}{sb.Color.B:X2}", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }
```

STOP — that ring comparison is too clever and fragile. Instead TAG each chip:
`chip.Tag = hex;` at build, and:

```csharp
    private void UpdateHighlightRings()
    {
        foreach (var child in HighlightChoices.Children)
            if (child is Border { Tag: string hex } b)
                b.BorderThickness = new Thickness(
                    string.Equals(Vm?.DefaultHighlight, hex, StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }
```

Use the Tag version; do not implement the color-reconstruction one. IMPORTANT: the 4 hexes above
MUST be checked against `FormatToolbar.Highlights` (FormatToolbar.axaml.cs:17) — use the toolbar's
actual non-null hex list verbatim (the 4th entry above is a guess; read the real one).

`SyncFromVm()` additions (before UpdateGateVisuals):

```csharp
        CaretColorBox.Text = vm.CaretColor ?? "";
        CaretWidthSlider.Value = vm.CaretWidth;
        CaretWidthValue.Text = $"{vm.CaretWidth:0.0}";
        DateFormatBox.SelectedIndex = Math.Max(0, Array.IndexOf(DateFormats, vm.DateFormat));
        UserNameBox.Text = vm.UserName;
        CardSizeBox.SelectedItem = vm.CardSize;
        NewNoteWidthSlider.Value = vm.NewNoteWidth;
        NewNoteWidthValue.Text = ((int)vm.NewNoteWidth).ToString();
        UpdateHighlightRings();
```

Nav handler: add `if (key == "shortcuts") BuildShortcutRows();` next to the data/fonts lines.

- [ ] **Step 6:** Full suite → 102/102. Commit:
```bash
git add -A src
git commit -m "feat(m8): Editor + Shortcuts prefs categories, personal-touch rows in General/Appearance/Canvas"
```

---

## Task 5: Final integration review + relaunch + owner checklist

- [ ] **Step 1:** Final reviewer over the Part 1b range. Extra seams: ctor-load gotcha compliance
for ALL ten new hooks (Greeting/RefreshHome guards); accent-follows vs custom-accent precedence +
home↔notebook transitions re-tinting; card-size resource seeding before first bind; date format
invalid-string fallback; DefaultHighlight interaction with ToggleDefaultHighlight's toggle-off
logic (same value = clears — still true with a changed pref?).
- [ ] **Step 2:** Rebuild + relaunch.
- [ ] **Step 3: Owner checklist:**
1. Editor category: caret color hex (blank = accent), width slider (visible live in a note),
   blink toggle; quick-highlight chips change what Ctrl+Shift+H paints; date format presets show
   TODAY's date rendered; Ctrl+Shift+T inserts it in a note.
2. Canvas: new-container width slider — new boxes spawn at that width.
3. Appearance: "Accent follows notebook" — open different-colored notebooks and watch the app
   re-tint; home goes back to your accent. Cover size S/M/L resizes gallery cards.
4. General: "Greet me as" + counts toggle change the homepage live.
5. Shortcuts category lists the real bindings.
6. Reset restores everything.
