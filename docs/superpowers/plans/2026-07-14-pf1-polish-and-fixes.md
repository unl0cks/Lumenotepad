# PF1 — Owner Feedback: Bugs, Polish & Prefs Round

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the owner's 5 reported bugs (invisible list items, laggy/incomplete smooth scrolling,
cut-off fonts list), land 3 UI changes (wrapping sections + resizable side panel, toolbar overflow
placement, softer theme borders), 4 preferences upgrades (full grid options, double-click create,
bigger default, "?" help on every setting), and 3 animation items (combo dropdowns, slider glide,
themed/animated context menus).

**Architecture:** All fixes ride existing patterns — Motion engine, AppSettings→VM guard-save,
ThemePalettes tokens, GuideLayer keys. The one new mechanism is a container-state reset on ListBox
recycling (`ContainerPrepared`), which is the suspected root cause of both invisible-item bugs.

**Tech Stack:** Avalonia 12.0.4, .NET 10, xUnit.

**Owner bug reports (verbatim intent):**
B1 deleting a notebook's only page → every page row everywhere invisible; B2 new pages/sections
invisible until restart; B3 smooth scrolling laggy; B4 smooth scrolling dead in the Offered-fonts
list; B5 fonts list stops in a box instead of filling the window.

**Verified context (do not re-derive):**
- Add/delete list animations: `MainView.RiseAdded` posts `Motion.RiseIn(container)`;
  `CollapseThenDelete` runs `Motion.CollapseOut(container, …, delete)`. Motion tweens set a LOCAL
  `Opacity`/`RenderTransform` (Frame(0) = opacity 0) and clear them only when a tween completes at
  identity. Avalonia ListBox containers are RECYCLED — a container left at opacity 0 (deleted item,
  or a cancelled rise) is later REUSED for a different item, which then renders invisible. That
  matches B1 (the collapsed container gets recycled as other sections/pages realize) and B2 (the
  posted RiseIn can target a container that's re-prepared before the tween runs, or a recycled
  0-opacity container never gets a rise). Fix at the source: reset visual state whenever a container
  is (re)prepared. `ItemsControl.ContainerPrepared` exists in Avalonia 11+ (event args expose
  `Container`).
- `SmoothScroll` (Views/SmoothScroll.cs) ticks a plain `DispatcherTimer` (default priority) at 15ms
  — input-priority work delays it → perceived jank (B3). `DispatcherTimer(TimeSpan, DispatcherPriority.Render, EventHandler)`
  ctor exists. Its `OverInnerScrollable` deliberately bails over the fonts list's inner ScrollViewer
  (B4) — attach SmoothScroll to that inner ScrollViewer too (outer tunnel handler runs first,
  returns unhandled over inner scrollables, inner attach then smooths).
- `FontsList` has fixed `Height="300"` — LOAD-BEARING for virtualization inside the outer
  ScrollViewer (never remove the bound-height; instead SIZE IT to the window: PrefsScroll viewport
  height minus the header block).
- Sections strip: `MainView.axaml` wraps `SectionsList` in a horizontal `ScrollViewer`
  (`HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"`), ItemsPanel =
  horizontal StackPanel. PagesPanel is `Width="224"`; `Motion.Reveal(PagesPanel, 224, …)` and
  `ApplyPanels` hardcode 224.
- Toolbar: `FormatToolbar.axaml` — `Border#Chrome` → `StackPanel#Panel` with format buttons then
  `DockBtn` ("..." = Toolbar position) LAST IN FLOW. `SetPlacement(dock, onPaper)` in the
  code-behind flips `Panel.Orientation` for Left/Right docks (read it before editing).
- ThemePalettes.Resolve: Dark `frameBorder:"#26FFFFFF"`, `solidPaperBorder:"#30FFFFFF"`; Light
  `#1F000000`/`#24000000`; Pink `#26B0526E`/`#33C97D97`; Light blue `#265F7BAE`/`#336E86B8`;
  `CanvasChipBorder` dark `#3AFFFFFF` / light `#24000000`. Lumen is NOT in scope (owner is happy
  with it). The glass-paper (Full-theme-off) branch of `Solid(...)` sits at ThemePalettes.cs:120+ —
  read it and soften its PaperBorder analogously.
- Prefs global grid pref: `AppSettings.PageGrid` stores "None"|"Dots"|"Lines" (legacy);
  `PageStyles.MapGlobalGrid` maps them (garbage→Blank). New grid-style keys: Blank/Ruled/Grid/Dots.
- `NoteCanvas.OnPointerPressed` creates a container on ANY bare-canvas left press; `e.ClickCount`
  is available on `PointerPressedEventArgs`.
- ContextMenus are ALL code-created: `MainView.OpenMenu(e, items)` (one shared choke point), the
  grip menu in `NoteCanvas.cs` (`_grip.ContextRequested`), and `OpenSortMenu`'s `MenuFlyout`.
  `ThemeManager.Apply` writes ~30 DynamicResource brushes from ThemeTokens — new tokens follow that
  pattern (`Brush("XBrush", t.X)`).
- Keyframe `Style.Animations` and Opacity/Brush transitions WORK declaratively; RenderTransform
  styles/transitions DO NOT (drive transforms via Motion code-behind only).
- Build gotchas: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test;
  `cd /e/CLAUDE/Lumenotepad` per Bash call; never launch the GUI from a subagent. Suite: 168 green.

---

### Task 1: B1+B2 — recycled containers keep animation state

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1: Implement the reset**

In `MainView`'s constructor (near the `HookCollectionAnimations` wiring), add one hook per
item-hosting list — `NotebooksList`, `SectionsList`, `PagesList`, `HomeCards`:

```csharp
        // Recycled containers keep whatever LOCAL Opacity/RenderTransform a Motion tween left on
        // them (a deleted row collapsed to opacity 0, a cancelled rise…) — the next item presented
        // in that container then renders INVISIBLE. Reset the visual state every time a container
        // is (re)prepared; legit add-animations start AFTER this (posted at Background priority).
        foreach (var list in new ItemsControl[] { NotebooksList, SectionsList, PagesList, HomeCards })
            list.ContainerPrepared += (_, e) =>
            {
                Motion.Stop(e.Container);
                e.Container.ClearValue(Visual.OpacityProperty);
                e.Container.ClearValue(Visual.RenderTransformProperty);
            };
```

(If the event's args type differs — e.g. `ContainerPreparedEventArgs` exposing `Container` as a
`Control` — adapt member access only; if `ContainerPrepared` does not exist on this Avalonia build,
report BLOCKED with what IS available rather than improvising.)

- [ ] **Step 2: Also heal CollapseThenDelete's leftover state at the source**

`CollapseThenDelete` currently runs `Motion.CollapseOut(container, Motion.Base, delete);`. Restore
the container's visual state after the delete runs, so even non-recycled reuse is clean:

```csharp
    /// <summary>Collapse an item's container out, then run the actual delete. The container may be
    /// RECYCLED for another item afterwards — restore its visual state once the delete has run.</summary>
    private void CollapseThenDelete(Control? container, System.Action delete)
    {
        if (container is null) { delete(); return; }
        Motion.CollapseOut(container, Motion.Base, () =>
        {
            delete();
            container.ClearValue(Visual.OpacityProperty);
            container.ClearValue(Visual.RenderTransformProperty);
        });
    }
```

- [ ] **Step 3: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build clean, 168/168. (The repro itself is visual — the owner verifies: delete a
notebook's only page, then browse all notebooks; create new pages/sections and see them appear.)

- [ ] **Step 4: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "fix(pf1): reset recycled list containers' animation state — invisible pages/sections"
```

---

### Task 2: B3+B4+B5 — smooth-scroll FPS, fonts-list scroll + height

**Files:**
- Modify: `src/Lumenotepad/Views/SmoothScroll.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`

- [ ] **Step 1: Render-priority, faster ticks (B3)**

In `SmoothScroll.CreateTimer`, replace the timer construction:

```csharp
        // Render priority + 10ms ticks: a default-priority 15ms timer queues behind input/layout
        // work and visibly stutters (owner report). Render priority fires right before the frame.
        var t = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Render, (_, _) => Tick());
```

Refactor the tick body into a private `void Tick()` (same logic; `_timer!.Stop()` becomes
`_timer?.Stop()`). Constructor form: `DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback)` —
this ctor exists; if the signature differs, set `Interval` + priority via the matching ctor overload
rather than improvising a new mechanism. Scale `CatchUp` for the shorter tick: 0.22 per 15ms ≈
**0.16 per 10ms** — change the constant to 0.16 with a comment.

- [ ] **Step 2: Smooth the fonts checklist (B4)**

The fonts ListBox scrolls via its OWN inner ScrollViewer, which `OverInnerScrollable` deliberately
leaves native. Attach SmoothScroll to that inner ScrollViewer once it exists. In
`PreferencesWindow.axaml.cs`, in `RefreshFontChoices()` after `FontsList.ItemsSource = choices;`:

```csharp
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
```

Add the field `private bool _fontsScrollSmoothed;` and `using Avalonia.VisualTree;` if missing.

- [ ] **Step 3: Fill the window (B5) — keep the bounded height, derive it from the viewport**

The fixed Height=300 exists so the list VIRTUALIZES inside the outer infinite-height ScrollViewer —
keep a bounded height but size it to the window. In the ctor (after `SmoothScroll.Attach(PrefsScroll);`):

```csharp
        // The fonts checklist needs a BOUNDED height to virtualize (the outer ScrollViewer measures
        // with infinite height), but a fixed 300 strands it mid-window — track the viewport instead.
        PrefsScroll.SizeChanged += (_, _) =>
            FontsList.Height = Math.Max(240, PrefsScroll.Bounds.Height - 200);
```

In `PreferencesWindow.axaml`, update the `FontsList` element: remove `Height="300"` and UPDATE the
load-bearing comment above it to say the height is set from the viewport in code (still bounded, still
virtualizing) — do not delete the comment.

- [ ] **Step 4: Build + suite green; commit**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "fix(pf1): smooth-scroll render priority + fonts list eased scroll + viewport-filling height"
```

---

### Task 3: Sections wrap + resizable side panel + toolbar overflow placement (TDD for the setting)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml`, `src/Lumenotepad/Views/MainView.axaml.cs`
- Modify: `src/Lumenotepad/Views/FormatToolbar.axaml`, `src/Lumenotepad/Views/FormatToolbar.axaml.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing tests for the width setting**

AppSettingsTests:

```csharp
    [Fact]
    public void PagesPanelWidth_defaultsAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal(224, new AppSettings().PagesPanelWidth);
            new AppSettings { PagesPanelWidth = 300 }.Save(dir);
            Assert.Equal(300, AppSettings.Load(dir).PagesPanelWidth, 3);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

MainViewModelTests:

```csharp
    [Fact]
    public void ResetSettingsToDefaults_restoresPagesPanelWidth()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.PagesPanelWidth = 320;
            vm.ResetSettingsToDefaults();
            Assert.Equal(224, vm.PagesPanelWidth);
            Assert.Equal(224, AppSettings.Load(dir).PagesPanelWidth, 3);
        }
        finally { Directory.Delete(dir, true); }
    }
```

Run the filter → compile errors expected. Then implement the standard settings chain (POCO
`public double PagesPanelWidth { get; set; } = 224;` after `SummonHotkey`; VM observable
`_pagesPanelWidth = 224` + ctor-load + save-only guard hook + reset line — mirror the adjacent
Part-5 members exactly).

- [ ] **Step 2: Sections wrap instead of scrolling**

In `MainView.axaml`, the sections block currently is a horizontal `ScrollViewer` wrapping
`SectionsList` (StackPanel ItemsPanel). Replace with the bare list + a WrapPanel:

```xml
                    <ListBox x:Name="SectionsList" DockPanel.Dock="Top" Classes="sections" Background="Transparent" BorderThickness="0" Padding="0"
                             ItemsSource="{Binding SelectedNotebook.Sections}"
                             SelectedItem="{Binding SelectedSection, Mode=TwoWay}">
                        <ListBox.ItemsPanel>
                            <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                        </ListBox.ItemsPanel>
```

(keep the existing ItemTemplate unchanged; the `DockPanel.Dock="Top"` moves from the removed
ScrollViewer to the ListBox).

- [ ] **Step 3: Resizable pages panel (drag grip, clamped, persisted)**

In `MainView.axaml`, immediately AFTER the `PagesPanel` Border's closing tag (still inside grid
column 1 — wrap PagesPanel and the grip in a `<Panel Grid.Column="1">` if needed, or simpler: add
the grip INSIDE `PagesPanel` as a right-edge overlay). Recommended: inside the PagesPanel Border,
wrap its DockPanel in a Panel and add:

```xml
                    <Border x:Name="PagesResizeGrip" Width="5" HorizontalAlignment="Right"
                            Background="Transparent" Cursor="SizeWestEast"/>
```

In `MainView.axaml.cs` (ctor):

```csharp
        // Drag the pages panel's right edge to resize it (clamped); persists via the VM setting.
        bool panelDragging = false; double panelStartX = 0, panelStartW = 0;
        PagesResizeGrip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(PagesResizeGrip).Properties.IsLeftButtonPressed) return;
            panelDragging = true;
            panelStartX = e.GetPosition(this).X;
            panelStartW = PagesPanel.Bounds.Width;
            e.Pointer.Capture(PagesResizeGrip);
            e.Handled = true;
        };
        PagesResizeGrip.PointerMoved += (_, e) =>
        {
            if (!panelDragging) return;
            PagesPanel.Width = Math.Clamp(panelStartW + (e.GetPosition(this).X - panelStartX), 180, 340);
        };
        PagesResizeGrip.PointerReleased += (_, e) =>
        {
            if (!panelDragging) return;
            panelDragging = false;
            e.Pointer.Capture(null);
            if (Vm is { } pvm) pvm.PagesPanelWidth = PagesPanel.Width;   // persist once, on release
        };
```

Replace the hardcoded 224s: `ApplyPanels` uses `vm.PagesPanelWidth` for PagesPanel.Width; the
`Motion.Reveal(PagesPanel, 224, …)` call becomes `Motion.Reveal(PagesPanel, Vm?.PagesPanelWidth ?? 224, …)`.
(add `using System;` only if `Math` isn't already reachable — the file uses `System.Math` style; match it.)

- [ ] **Step 4: Toolbar "..." to the opposite end**

In `FormatToolbar.axaml`: change `Border#Chrome`'s child to a DockPanel; `DockBtn` moves out of
`Panel` and docks at the far end; the tools StackPanel fills:

```xml
    <Border x:Name="Chrome" Padding="5,3">
        <DockPanel x:Name="Dock">
            <Button x:Name="DockBtn" DockPanel.Dock="Right" Theme="{StaticResource IconButton}" Width="30" Height="30" FontSize="14"
                    FontFamily="{StaticResource IconFont}" Content="&#xE712;" ToolTip.Tip="Toolbar position"/>
            <StackPanel x:Name="Panel" Orientation="Horizontal" Spacing="1">
```

(the format buttons stay inside `Panel`; close tags accordingly). In `FormatToolbar.axaml.cs`,
find `SetPlacement` — where it flips `Panel.Orientation` for vertical docks, also flip the dock:
`DockPanel.SetDock(DockBtn, vertical ? Avalonia.Controls.Dock.Bottom : Avalonia.Controls.Dock.Right);`
(read the method first; use its existing orientation condition. If it doesn't flip orientation at
all, place DockBtn Right always and note it.)

- [ ] **Step 5: Build + full suite green; commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(pf1): wrapping sections, resizable pages panel (persisted), toolbar overflow at the far end"
```

---

### Task 4: Softer borders on Dark / Light / Pink / Light blue

**Files:**
- Modify: `src/Lumenotepad/Services/ThemePalettes.cs`
- Modify (if hex-coded borders found): `src/Lumenotepad/Views/Converters.cs`, `src/Lumenotepad/Themes/Theme.axaml`

- [ ] **Step 1: Tune the four themes' border tokens (Lumen untouched)**

The owner: "the contrast is too big" — dark theme borders too bright, light-family too dark.
Roughly HALVE the border alphas. In `Resolve`:

| Theme | frameBorder | solidPaperBorder |
|---|---|---|
| Dark | `#26FFFFFF` → `#14FFFFFF` | `#30FFFFFF` → `#1AFFFFFF` |
| Light | `#1F000000` → `#10000000` | `#24000000` → `#12000000` |
| Pink | `#26B0526E` → `#14B0526E` | `#33C97D97` → `#1AC97D97` |
| Light blue | `#265F7BAE` → `#145F7BAE` | `#336E86B8` → `#1A6E86B8` |

In `Solid(...)`: `CanvasChipBorder` dark `#3AFFFFFF` → `#22FFFFFF`, light `#24000000` → `#12000000`.
Read the Full-theme-OFF (glass paper) branch below line 120 and apply the same ~50% alpha reduction
to any PaperBorder/border values there for these themes. Do NOT touch the Lumen(...) method.

- [ ] **Step 2: Buttons + notebook cards**

Find what borders the homepage `LumenButton`-themed buttons ("New notebook", "Preferences") draw:
grep `Theme.axaml` for `LumenButton` and check its BorderBrush — if it's a DynamicResource of a
token you just softened, done; if it's a literal hex, halve its alpha the same way (per-theme isn't
possible in a literal — prefer re-pointing it at `FrameBorderBrush`). For notebook cards/rail chips:
`Converters.CoverBorder` derives a border shade from the cover color — soften it (e.g. reduce the
shade delta or output alpha ~30%) so card outlines read gentler; the same converter serves all
themes, small global reduction is intended.

- [ ] **Step 3: Update any ThemePalettes tests**

`ThemePalettesTests` may assert exact border hexes — update ONLY assertions that pin the old values
(the test's intent is the matrix structure, not these literals).

- [ ] **Step 4: Build + suite green; commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(pf1): soften border contrast on Dark/Light/Pink/Light blue (Lumen untouched)"
```

---

### Task 5: Prefs — full grid options, double-click create, bigger window (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Editor/PageStyles.cs`, `src/Lumenotepad/Editor/NoteCanvas.cs`
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml(.cs)`, `src/Lumenotepad/Views/MainView.axaml.cs`
- Test: `tests/Lumenotepad.Tests/PageStylesTests.cs`, `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing tests**

PageStylesTests — extend the mapping theory with pass-through of the new keys:

```csharp
    [Theory]
    [InlineData("Blank", "Blank")]
    [InlineData("Ruled", "Ruled")]
    [InlineData("Grid", "Grid")]
    public void MapGlobalGrid_passesThroughNewKeys(string stored, string expected) =>
        Assert.Equal(expected, PageStyles.MapGlobalGrid(stored));
```

AppSettingsTests + MainViewModelTests — `DoubleClickCreate` bool (default false) round-trip and
reset, exactly in the shape of the Part-5 `TraySettings`/reset tests (copy that shape, one flag).

- [ ] **Step 2: Implement**

- `PageStyles.MapGlobalGrid`: add `"Blank" => Blank, "Ruled" => Ruled, "Grid" => Grid,` arms above
  the existing legacy arms ("Dots" already passes through; "Lines" stays → Grid; default → Blank).
- Settings chain for `DoubleClickCreate` (POCO default false → VM observable + ctor-load + guarded
  save hook + reset — the established pattern).
- `NoteCanvas`: `public bool CreateOnDoubleClick { get; set; }` and in `OnPointerPressed`, after the
  left-button check: `if (CreateOnDoubleClick && e.ClickCount < 2) return;`
- `MainView.ApplyCanvasPrefs`: push `PageCanvas.CreateOnDoubleClick = vm.DoubleClickCreate;` and add
  `nameof(MainViewModel.DoubleClickCreate)` to that OnVmPropertyChanged branch.
- Prefs Canvas panel: `PageGridBox.ItemsSource = new[] { "None", "Ruled", "Grid", "Dots" };` — the
  SelectionChanged handler stores the picked string directly ("None" stays the stored off-value;
  Ruled/Grid/Dots are the new-style keys). `SyncFromVm` maps stored legacy values for display:
  `"Lines" → "Grid"`, anything unknown → "None" (small local map — show the code in the handler).
  Update the row hint to "Faint ruled lines, grid, or dots on every page (pages can override)."
  Add below the Snap row a new toggle row "Create notes with double-click" bound
  `{Binding DoubleClickCreate, Mode=TwoWay}` with hint "Off: a single click on empty paper starts a
  note. On: double-click instead, so stray clicks do nothing."
- `PreferencesWindow.axaml`: `Width="840" Height="620"` (Min stays).

- [ ] **Step 3: Build + full suite green; commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(pf1): grid pref gains Ruled/Grid keys, double-click create, roomier prefs window"
```

---

### Task 6: "?" help on every preferences setting

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml`

- [ ] **Step 1: The qmark style**

In `Window.Styles`:

```xml
        <!-- Hover help: a small "?" beside every setting label; plain-language explanation. -->
        <Style Selector="Border.qmark">
            <Setter Property="Width" Value="15"/>
            <Setter Property="Height" Value="15"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="BorderBrush" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Margin" Value="6,0,0,0"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="(ToolTip.ShowDelay)" Value="500"/>
        </Style>
        <Style Selector="Border.qmark > TextBlock">
            <Setter Property="Text" Value="?"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="HorizontalAlignment" Value="Center"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
```

(If `ToolTip.ShowDelay` can't be set from a style in this build, set it per-element alongside the Tip.)

- [ ] **Step 2: Add a qmark to EVERY setting row in EVERY panel**

Pattern — each `<TextBlock Classes="label" Text="X"/>` heading a setting becomes:

```xml
<StackPanel Orientation="Horizontal">
    <TextBlock Classes="label" Text="X"/>
    <Border Classes="qmark" ToolTip.Tip="…plain-language help…"><TextBlock/></Border>
</StackPanel>
```

Apply to every setting in General, Appearance, Layout, Canvas, Editor, Fonts, Bullets & numbers,
and Data & tools (the Shortcuts panel is a reference table — skip it). ~45 rows.

**Help-copy rules (owner requirement: a NON-TECHNICAL user must understand):** one or two short
sentences; say what the user SEES change; name each option's effect when there are options; no
jargon (never "debounce", "acrylic", "hex", "UTC", "registry" — say "saved code for a color",
"when you sign in to Windows", etc.); no exclamation marks. Examples to copy the tone of:

- Autosave after typing stops → "How long Lumenotepad waits after you stop typing before saving your work. Shorter is safer, longer is quieter."
- Launch behavior → "What you see when the app opens. 'Home page' shows all your notebooks; 'Last page' jumps straight back to where you left off."
- Glass tint → "Makes the see-through parts of the window lighter or darker. Zero leaves them as they are."
- Custom accent → "The highlight color used for buttons and selections. Type a color code like #4DA6FF, or press Enter on an empty box to go back to the theme's own color."
- Bold numbers (bullets) → "How the numbers in numbered lists look. 'Match text' copies whatever the line's text looks like."
- Backup folder → "Where your automatic backup copies are saved. Pick a folder outside this computer's app folder — for example a cloud-synced one."
- Paper grid → "Faint lines or dots drawn on the page behind your notes: ruled lines like a notebook, squared grid, or dots. Individual pages can pick their own."
- Show all installed fonts → "Offers every font installed on this computer instead of the short recommended list. The menu gets much longer."

Write the remaining ~37 in this voice. The final reviewer will spot-check copy quality against
these rules.

- [ ] **Step 3: Build + suite green; commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(pf1): hover \"?\" help on every preferences setting (plain language)"
```

---

### Task 7: Animations — slider glide, combo dropdowns, themed context menus

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml(.cs)`
- Modify: `src/Lumenotepad/Services/ThemePalettes.cs`, `src/Lumenotepad/Services/ThemeManager.cs`
- Modify: `src/Lumenotepad/App.axaml`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`, `src/Lumenotepad/Editor/NoteCanvas.cs`

- [ ] **Step 1: Slider glide**

Remove `IsSnapToTickEnabled="True"` from every prefs Slider (keep `TickFrequency` for reference).
The thumb then follows the cursor continuously. Snap the STORED value in each ValueChanged handler
instead — e.g. Autosave: `int v = (int)(Math.Round(e.NewValue / 100) * 100);` then compare/assign
`vm.AutosaveMs` and format the label from `v` (not raw). Apply the same round-to-tick in each
handler (RecentCount/BackupKeep round to 1 — the `(int)` cast becomes `(int)Math.Round(...)`;
CaretWidth to 0.1; sliders whose scale labels show one decimal round to 0.1; EditorFontSize to 1;
NewNoteWidth to 20; GlassTint to 0.05). `SyncFromVm` already writes snapped values back.

- [ ] **Step 2: Combo dropdown animation + rounding**

App-wide rounding in `App.axaml` styles: `<Style Selector="ComboBox"><Setter Property="CornerRadius" Value="9"/></Style>`
and popup content `<Style Selector="ComboBox /template/ Border#PopupBorder"><Setter Property="CornerRadius" Value="9"/></Style>`
(the Fluent ComboBox popup border is named `PopupBorder`; if the build's template names differ,
inspect with `combo.GetVisualDescendants()` in a debug print or adapt the selector — do not ship a
selector you can't confirm compiles; a non-matching selector is silent, acceptable fallback).
Open animation (code-behind, proven Motion path) — in `PreferencesWindow.axaml.cs` ctor, after all
combos are wired:

```csharp
        // Dropdown open animation: Fluent has none; rise the popup content via the Motion engine.
        foreach (var combo in new[] { LaunchTargetBox, MotionSpeedBox, CardSizeBox, DateFormatBox,
                                      EditorFontBox, ToolbarPosBox, ToolbarScopeBox, PageGridBox,
                                      BackupEveryBox, NumBoldBox, NumItalicBox, NumUnderlineBox, NumStrikeBox })
            combo.DropDownOpened += (s, _) =>
            {
                if (s is ComboBox cb &&
                    cb.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().FirstOrDefault()?.Child is Control c)
                    Motion.RiseIn(c, Motion.Fast);
            };
```

(`Popup` may live in `Avalonia.Controls.Primitives` — adjust the using/qualifier to what compiles.
If the popup child can't be located, the handler silently does nothing — acceptable.)

- [ ] **Step 3: Context menus — theme tokens, rounding, animation, Lumen glass variant**

1. `ThemeTokens` gains `string MenuBackground, string MenuBorder` (record — add to the parameter
   list; update every construction site in ThemePalettes):
   - Lumen Full-theme OFF: `MenuBackground = "#F5171922"` (dark opaque), `MenuBorder = "#2EFFFFFF"`.
   - Lumen Full-theme ON: `MenuBackground = "#B814161C"` (translucent dark — the frosted look; see
     step 4), `MenuBorder = "#33FFFFFF"`.
   - Solid themes: `MenuBackground = Alpha(frameBg, 0xFF)` (the opaque frame family),
     `MenuBorder` = the theme's (newly softened) frameBorder.
2. `ThemeManager.Apply` writes `Brush("MenuBackgroundBrush", t.MenuBackground)` and
   `Brush("MenuBorderBrush", t.MenuBorder)` like its ~30 siblings.
3. `App.axaml` global styles:

```xml
        <Style Selector="ContextMenu">
            <Setter Property="Background" Value="{DynamicResource MenuBackgroundBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource MenuBorderBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="4"/>
        </Style>
        <Style Selector="MenuFlyoutPresenter">
            <Setter Property="Background" Value="{DynamicResource MenuBackgroundBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource MenuBorderBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="4"/>
        </Style>
        <Style Selector="MenuItem">
            <Setter Property="CornerRadius" Value="7"/>
        </Style>
```

4. Open animation via the shared choke points: in `MainView.OpenMenu`, before `menu.Open(c)`:
   `menu.Opened += (_, _) => Motion.RiseIn(menu, Motion.Fast);` — same one-liner on the grip
   ContextMenu in `NoteCanvas.cs` and the `MenuFlyout` in `OpenSortMenu` (MenuFlyout has no Opened?
   then hook after `ShowAt` via a posted RiseIn on… if no reliable hook exists for MenuFlyout, skip
   IT and note it — ContextMenus are the owner's main ask).
5. Lumen Full-ON "frosted glass": the translucent `#B8…` MenuBackground already reads as smoked
   glass over content. TRUE blur needs acrylic on the popup's own top-level. Best-effort: in
   `OpenMenu`'s `Opened` handler, `if (Services.ThemeManager.Current is { GlassWindow: true } t && TopLevel.GetTopLevel(menu) is { } tl && tl.TryGetPlatformHandle()?.Handle is { } h)` →
   call the existing `Platform.DwmAcrylic` apply helper on `h` (read `Platform/DwmAcrylic.cs` for
   the exact method — it exists; MainWindow uses it). Wrap in try/catch — if the popup handle
   rejects the backdrop, the translucent fallback stands. Gate on Lumen+FullTheme via
   `Vm is { Theme: "Lumen", FullTheme: true }`.

- [ ] **Step 4: Build + full suite green; commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(pf1): slider glide, combo dropdown motion+rounding, themed rounded animated context menus"
```

---

### Task 8: Final integration review + relaunch + checklist

- [ ] Final integration reviewer (opus) over the full PF1 diff vs this plan. Extra attention:
  the ContainerPrepared reset (does it fight RiseAdded's posted animation? order matters),
  SmoothScroll refactor (fresh-gesture detection still works with the new timer), the pages-panel
  drag vs Motion.Reveal interplay (hide/show while resized), MapGlobalGrid backward compatibility
  (legacy "Lines" settings still render Grid), double-click create doesn't break single-click when
  off, ThemeTokens record change compiles at every construction site, context-menu styles don't
  break the toolbar flyouts (Flyout ≠ ContextMenu — confirm scope), and a copy-quality spot-check
  of ~10 "?" tooltips against the plain-language rules.
- [ ] Fix Important+ inline; re-run the suite.
- [ ] Rebuild + relaunch; update memory; owner checklist:
  1. Delete a notebook's only page → other notebooks' pages all still visible; new pages/sections
     appear immediately.
  2. Prefs scrolling feels fluid; the fonts list glides too and reaches the window bottom.
  3. Sections wrap to a second row; drag the pages panel's right edge (180–340) — sticks after
     restart; toolbar "..." sits at the far end (top AND side docks).
  4. Dark/Light/Pink/Light blue: borders everywhere read softer; Lumen unchanged.
  5. Prefs: Paper grid offers None/Ruled/Grid/Dots; double-click-create toggle works both ways;
     window opens roomier; hover any "?" for half a second → plain-language help.
  6. Sliders glide; dropdowns animate open and are rounder; right-click menus are rounded, themed
     (Lumen full-off = dark opaque, full-on = frosted), and animate in.
