# M8 Part 3 — Canvas: Paper Grid, Grid Snap, Per-Notebook Paper Tint

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A paper grid (dots or lines) drawn under note containers with optional 20px snap for
move/resize/placement, plus a per-notebook paper tint set from the notebook context menu.

**Architecture:** Two new global settings (`PageGrid`, `GridSnap`) follow the established
AppSettings → VM observable + guard-save → `ApplyCanvasPrefs` push. The grid itself is a Border
child of NoteCanvas filled with a **tiled DrawingBrush** (one 20px cell repeated by the GPU — never
per-dot geometry). Snap is a pure static (`GridMath.Snap`) applied inside the existing drag handlers.
Paper tint is **per-notebook data** (`Notebook.PaperTint`, nullable hex, persisted in the notebook's
own `notebook.json`) rendered as a translucent veil between the paper background and the page content.

**Tech Stack:** Avalonia 12.0.4 (`DrawingBrush`, `GeometryDrawing`, `TileMode` all confirmed present
in `Avalonia.Base.dll` 12.0.4), .NET 10, xUnit.

**Verified constraints (do not re-derive):**
- `Panel.Render` is sealed in this codebase's experience — the grid must be a CHILD element of
  NoteCanvas (same lesson as the `_hint` TextBlock), not a Render override.
- Every `OnXChanged` VM hook fires during the ctor settings-load. The two new hooks are save-only
  (no workspace/UI access), so the standard `_settings`/`_settingsDir` guard is sufficient.
- `Notebook` is serialized per-notebook with `JsonSerializer` (`WorkspaceStore.Save` →
  `notebook.json`); a new `[ObservableProperty]` on it round-trips automatically (same as `Color`).
- `ThemePalettes.Alpha(string hex, byte alpha)` returns a `#AARRGGBB` string (existing helper).
- Build gotcha: the running app locks the exe — always
  `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before build/test. Never launch the GUI from
  a subagent. `cd /e/CLAUDE/Lumenotepad` in every Bash call (cwd does not persist).

---

### Task 1: Settings + VM + GridMath + Notebook.PaperTint (TDD)

**Files:**
- Create: `src/Lumenotepad/Editor/GridMath.cs`
- Create: `tests/Lumenotepad.Tests/GridMathTests.cs`
- Modify: `src/Lumenotepad/Services/AppSettings.cs` (add 2 properties after `SmartLists`/palettes block)
- Modify: `src/Lumenotepad/Models/Workspace.cs` (Notebook: add `PaperTint`)
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs` (observables, ctor load, hooks, reset, `SetNotebookPaperTint`)
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/WorkspaceStoreTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Lumenotepad.Tests/AppSettingsTests.cs` (inside the class):

```csharp
    [Fact]
    public void PaperGridPrefs_DefaultAndRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal("None", new AppSettings().PageGrid);
            Assert.False(new AppSettings().GridSnap);

            var s = new AppSettings { PageGrid = "Dots", GridSnap = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.Equal("Dots", loaded.PageGrid);
            Assert.True(loaded.GridSnap);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Create `tests/Lumenotepad.Tests/GridMathTests.cs`:

```csharp
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class GridMathTests
{
    // No exact midpoints (10, 30, …) — Math.Round uses banker's rounding there and the
    // pointer never delivers a perfect .5 anyway.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(11, 20)]
    [InlineData(20, 20)]
    [InlineData(29, 20)]
    [InlineData(31, 40)]
    [InlineData(347, 340)]
    public void Snap_landsOnNearestCell(double input, double expected) =>
        Assert.Equal(expected, GridMath.Snap(input));
}
```

Append to `tests/Lumenotepad.Tests/WorkspaceStoreTests.cs` (inside the class — it already has a
`TempDir()` helper):

```csharp
    [Fact]
    public void PaperTint_roundTrips()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            ws.Notebooks.Add(new Notebook { Name = "Tinted", PaperTint = "#E8D9A8" });
            ws.Notebooks.Add(new Notebook { Name = "Plain" });

            store.Save(ws);
            var loaded = new WorkspaceStore(dir).Load();

            Assert.Equal("#E8D9A8", loaded.Notebooks[0].PaperTint);
            Assert.Null(loaded.Notebooks[1].PaperTint);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Append to `tests/Lumenotepad.Tests/MainViewModelTests.cs` (inside the class):

```csharp
    [Fact]
    public void ResetSettingsToDefaults_restoresPaperGridPrefs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.PageGrid = "Lines";
            vm.GridSnap = true;

            vm.ResetSettingsToDefaults();

            Assert.Equal("None", vm.PageGrid);
            Assert.False(vm.GridSnap);
            var persisted = AppSettings.Load(dir);
            Assert.Equal("None", persisted.PageGrid);
            Assert.False(persisted.GridSnap);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetNotebookPaperTint_setsAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var nb = vm.Notebooks[0];

            vm.SetNotebookPaperTint(nb, "#8FC2EC");

            Assert.Equal("#8FC2EC", nb.PaperTint);
            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);
            Assert.Equal("#8FC2EC", reloaded.Notebooks[0].PaperTint);   // Save() ran

            vm.SetNotebookPaperTint(nb, null);
            Assert.Null(nb.PaperTint);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run the new tests — verify they fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "PaperGridPrefs_DefaultAndRoundTrip|Snap_landsOnNearestCell|PaperTint_roundTrips|ResetSettingsToDefaults_restoresPaperGridPrefs|SetNotebookPaperTint_setsAndPersists" 2>&1 | tail -20`
Expected: compile errors (`PageGrid`, `GridMath`, `PaperTint`, `SetNotebookPaperTint` don't exist) — that counts as the failing state.

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Editor/GridMath.cs`:

```csharp
using System;

namespace Lumenotepad.Editor;

/// <summary>The canvas paper-grid lattice. Pure math — kept off the UI types so it unit-tests
/// without an Avalonia platform.</summary>
public static class GridMath
{
    /// <summary>Grid cell edge in canvas pixels. The drawn grid and the snap share it, so a
    /// snapped container lands exactly on the dots/lines.</summary>
    public const double Cell = 20;

    /// <summary>Nearest lattice point (callers clamp to their own bounds afterwards).</summary>
    public static double Snap(double v) => Math.Round(v / Cell) * Cell;
}
```

In `src/Lumenotepad/Services/AppSettings.cs`, after the `TextPalette` line, add:

```csharp
    public string PageGrid { get; set; } = "None";          // canvas paper grid: None | Dots | Lines
    public bool GridSnap { get; set; }                      // move/resize lands on the 20px cell
```

In `src/Lumenotepad/Models/Workspace.cs`, inside `Notebook`, after the `_coverPath` field block, add:

```csharp
    /// <summary>Paper tint hex for this notebook's pages (null = untinted). Per-notebook data,
    /// not a preference — set from the notebook context menu, untouched by Reset settings.</summary>
    [ObservableProperty] private string? _paperTint;
```

In `src/Lumenotepad/ViewModels/MainViewModel.cs`:

1. After the `_smartLists` field, add:

```csharp
    [ObservableProperty] private string _pageGrid = "None";   // prefs: canvas paper grid
    [ObservableProperty] private bool _gridSnap;              // prefs: snap to the 20px cell
```

2. In the ctor settings-load block, after `SmartLists = _settings.SmartLists;`, add:

```csharp
            PageGrid = _settings.PageGrid;
            GridSnap = _settings.GridSnap;
```

3. After `OnSmartListsChanged`, add the two save hooks (save-only — safe during the ctor load):

```csharp
    partial void OnPageGridChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PageGrid = value;
        _settings.Save(_settingsDir);
    }

    partial void OnGridSnapChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.GridSnap = value;
        _settings.Save(_settingsDir);
    }
```

4. In `ResetSettingsToDefaults`, after `IndentScale = d.IndentScale; SmartLists = d.SmartLists;`, add:

```csharp
        PageGrid = d.PageGrid; GridSnap = d.GridSnap;
```

(`Notebook.PaperTint` is deliberately NOT reset — it's notebook data, like covers and colors.)

5. After `ClearNotebookCover`, add:

```csharp
    /// <summary>Set (hex) or clear (null) a notebook's paper tint; persists the tree.</summary>
    public void SetNotebookPaperTint(Notebook nb, string? hex)
    {
        nb.PaperTint = hex;
        Save();
    }
```

- [ ] **Step 4: Run the full suite — verify green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -5`
Expected: all tests pass — 116 total (105 existing + 4 new Facts + the 7-case GridMath theory).

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): paper-grid + snap settings, GridMath, per-notebook PaperTint"
```

---

### Task 2: NoteCanvas grid layer + snap wiring

**Files:**
- Modify: `src/Lumenotepad/Editor/NoteCanvas.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs` (`ApplyCanvasPrefs` + `OnVmPropertyChanged`)

No new unit tests (visual/pointer behavior — owner-verified); the suite must stay green.

- [ ] **Step 1: Add the grid layer + prefs to NoteCanvas**

In `src/Lumenotepad/Editor/NoteCanvas.cs`, after the `ConfirmDelete` property, add:

```csharp
    /// <summary>The paper grid ("Paper grid" preference): "None" | "Dots" | "Lines". The dots and
    /// lines sit on the same 20px lattice the snap uses, so snapped containers land exactly on them.</summary>
    public string GridStyle
    {
        get => _gridStyle;
        set { if (_gridStyle == value) return; _gridStyle = value; RefreshGrid(); }
    }
    private string _gridStyle = "None";

    /// <summary>"Snap to grid" preference: drag/resize/placement land on the 20px cell.</summary>
    public bool SnapToGrid { get; set; }

    // The grid underlay: a Border filled with a TILED DrawingBrush — one 20px cell drawn once and
    // repeated by the compositor. Per-dot geometry would put tens of thousands of nodes in the
    // scene. A child element because Panel.Render is sealed (same lesson as the starter hint).
    private readonly Border _gridLayer = new() { IsHitTestVisible = false };

    private void RefreshGrid() => _gridLayer.Background = BuildGridBrush(_gridStyle);

    private static IBrush? BuildGridBrush(string style)
    {
        var t = Services.ThemeManager.Current;
        if (style == "Dots")
        {
            // Full dots at all four tile corners: each is clipped to its quarter inside the cell
            // and the neighbouring tiles complete it — the assembled sheet shows whole dots
            // exactly on the 20px lattice.
            var g = new GeometryGroup();
            foreach (var (x, y) in new[] { (0.0, 0.0), (GridMath.Cell, 0.0), (0.0, GridMath.Cell), (GridMath.Cell, GridMath.Cell) })
                g.Children.Add(new EllipseGeometry(new Rect(x - 1.1, y - 1.1, 2.2, 2.2)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Brush = new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x30))),
            });
        }
        if (style == "Lines")
        {
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(GridMath.Cell, 0)));
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, GridMath.Cell)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            });
        }
        return null;
    }

    private static DrawingBrush Tile(Drawing cell) => new()
    {
        Drawing = cell, TileMode = TileMode.Tile, Stretch = Stretch.None,
        SourceRect = new RelativeRect(0, 0, GridMath.Cell, GridMath.Cell, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, GridMath.Cell, GridMath.Cell, RelativeUnit.Absolute),
    };
```

- [ ] **Step 2: Re-add the layer in Rebuild + arrange it full-bleed**

`Rebuild()` becomes:

```csharp
    private void Rebuild()
    {
        Children.Clear();
        SetActive(null);
        Children.Add(_gridLayer);      // first child = bottom of z-order: under every container
        RefreshGrid();                 // theme changes arrive as a Document reset — re-tint here
        Children.Add(_hint);
        if (_doc is not null)
            foreach (var box in _doc.Boxes)
                Children.Add(new NoteBoxView(this, box));
        UpdateHint();
        InvalidateMeasure();
    }
```

In `ArrangeOverride`, insert a special case at the TOP of the foreach body (before the
`if (child is not NoteBoxView v)` branch):

```csharp
            if (ReferenceEquals(child, _gridLayer))
            {
                child.Arrange(new Rect(finalSize));     // the grid covers the whole scrollable page
                continue;
            }
```

(`MeasureOverride` needs no change — the Border has no child, so `Measure(Size.Infinity)` in the
existing non-NoteBoxView branch yields a 0×0 desired size, which is harmless.)

- [ ] **Step 3: Snap in the drag handlers + new-box placement**

In `NoteBoxView.WireDrag`, replace the `PointerMoved` geometry block:

```csharp
            if (mode == DragMode.Move)
            {
                double nx = _dragOrigin.X + dx, ny = _dragOrigin.Y + dy;
                if (_canvas.SnapToGrid) { nx = GridMath.Snap(nx); ny = GridMath.Snap(ny); }
                Box.X = Math.Max(0, nx);
                Box.Y = Math.Max(0, ny);
            }
            if (mode is DragMode.Width or DragMode.Both)
            {
                double nw = _dragOrigin.W + dx;
                if (_canvas.SnapToGrid) nw = GridMath.Snap(nw);
                Box.Width = Math.Clamp(nw, NoteBox.MinWidth, 1600);
            }
            if (mode is DragMode.Height or DragMode.Both)
            {
                double nh = _dragOrigin.H + dy;
                if (_canvas.SnapToGrid) nh = GridMath.Snap(nh);
                Box.H = Math.Clamp(nh, NoteBox.MinHeight, 4000);
            }
```

In `NoteCanvas.OnPointerPressed`, replace the AddBox line:

```csharp
        var p = e.GetPosition(this);
        double bx = p.X - 11, by = p.Y - 16;
        if (SnapToGrid) { bx = Math.Max(0, GridMath.Snap(bx)); by = Math.Max(0, GridMath.Snap(by)); }
        var view = AddBoxView(_doc.AddBox(bx, by, Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640)));
```

- [ ] **Step 4: Push the prefs from MainView**

In `src/Lumenotepad/Views/MainView.axaml.cs`, `ApplyCanvasPrefs()`, after
`PageCanvas.HistoryEnabled = vm.DeletedHistory;`, add:

```csharp
        PageCanvas.GridStyle = vm.PageGrid;
        PageCanvas.SnapToGrid = vm.GridSnap;
```

In `OnVmPropertyChanged`, extend the canvas-prefs branch:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.ResizablePages) or nameof(MainViewModel.DeletedHistory)
                 or nameof(MainViewModel.PageGrid) or nameof(MainViewModel.GridSnap))
            ApplyCanvasPrefs();
```

- [ ] **Step 5: Build + full suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 116/116 pass.

- [ ] **Step 6: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): paper grid underlay (tiled brush) + 20px grid snap on the canvas"
```

---

### Task 3: Per-notebook paper tint — veil + context menus

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml` (wrap PageDock, add the veil)
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs` (`ApplyPaperTint`, menu items, call sites)

- [ ] **Step 1: Add the veil to the page box**

In `src/Lumenotepad/Views/MainView.axaml`, the page Border currently holds `PageDock` directly:

```xml
            <Border Margin="14" CornerRadius="14"
                    Background="{DynamicResource PaperBackgroundBrush}" BorderBrush="{DynamicResource PaperBorderBrush}" BorderThickness="1">
                <DockPanel x:Name="PageDock">
```

Wrap the DockPanel in a Panel with the veil as the FIRST child (radius 13 = the 14px border radius
minus its 1px border, so the tint never bleeds past the rounding):

```xml
            <Border Margin="14" CornerRadius="14"
                    Background="{DynamicResource PaperBackgroundBrush}" BorderBrush="{DynamicResource PaperBorderBrush}" BorderThickness="1">
                <Panel>
                    <!-- per-notebook paper tint: a translucent veil over the paper, under the content -->
                    <Border x:Name="PaperTintVeil" CornerRadius="13" IsVisible="False" IsHitTestVisible="False"/>
                    <DockPanel x:Name="PageDock">
```

…and close the new `</Panel>` right after the matching `</DockPanel>` (just before the page
Border's `</Border>`).

- [ ] **Step 2: ApplyPaperTint + call sites**

In `src/Lumenotepad/Views/MainView.axaml.cs`, after `ApplyCanvasPrefs`, add:

```csharp
    /// <summary>Per-notebook paper tint: the selected notebook's PaperTint hex as a translucent
    /// veil (fixed alpha keeps text readable on both light and dark paper).</summary>
    private void ApplyPaperTint()
    {
        var hex = Services.ThemePalettes.NormalizeHex(Vm?.SelectedNotebook?.PaperTint);
        PaperTintVeil.IsVisible = hex is not null;
        if (hex is not null) PaperTintVeil.Background = new SolidColorBrush(Color.Parse(hex), 0.22);
    }
```

Call it from `HookVm()` right after `ApplyCanvasPrefs();`:

```csharp
            ApplyPaperTint();
```

And in `OnVmPropertyChanged`, extend the existing rehook line:

```csharp
        if (e.PropertyName == nameof(MainViewModel.SelectedNotebook)) { RehookSections(); ApplyPaperTint(); }
```

- [ ] **Step 3: "Paper color" context-menu submenu**

In `src/Lumenotepad/Views/MainView.axaml.cs`, near `Swatch(...)`, add:

```csharp
    /// <summary>Soft tints that stay readable at the veil's fixed alpha on light AND dark paper.</summary>
    private static readonly (string Name, string? Hex)[] PaperTints =
    {
        ("None", null),
        ("Ivory", "#E8D9A8"), ("Peach", "#EFB98E"), ("Rose", "#EC9EB6"),
        ("Mint", "#9BD3A6"), ("Sky", "#8FC2EC"), ("Lavender", "#B4A2E6"),
        ("Sand", "#CBB98F"), ("Graphite", "#8C939E"),
    };

    /// <summary>The per-notebook "Paper color" submenu (current choice shown bold).</summary>
    private MenuItem PaperTintMenu(Notebook nb)
    {
        var root = new MenuItem { Header = "Paper color" };
        foreach (var (name, hex) in PaperTints)
        {
            var item = new MenuItem
            {
                Header = name,
                Icon = hex is null ? null : Swatch(hex),
                FontWeight = string.Equals(nb.PaperTint, hex, System.StringComparison.OrdinalIgnoreCase)
                    ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var chosen = hex;
            item.Click += (_, _) => { Vm?.SetNotebookPaperTint(nb, chosen); ApplyPaperTint(); };
            root.Items.Add(item);
        }
        return root;
    }
```

Wire it into both notebook menus:

In `OnHomeCardContextRequested`, insert `var paper = PaperTintMenu(nb);` after the `color` menu is
built, and add `paper` right after `color` in BOTH `OpenMenu(...)` calls:

```csharp
            OpenMenu(e, open, rename, moveLeft, moveRight, color, paper, cover, removeCover, delete);
```
```csharp
            OpenMenu(e, open, rename, moveLeft, moveRight, color, paper, cover, delete);
```

In `OnNotebooksContextRequested` (the rail), add the submenu before delete:

```csharp
        OpenMenu(e, PaperTintMenu(nb), delete);
```

- [ ] **Step 4: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 116/116 pass.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): per-notebook paper tint — veil + Paper color context menu"
```

---

### Task 4: Preferences Canvas panel — PAPER section

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` (CanvasPanel)
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml.cs` (combo wiring + SyncFromVm)

- [ ] **Step 1: Add the rows**

In `src/Lumenotepad/Views/PreferencesWindow.axaml`, inside `CanvasPanel`, after the
`NewNoteWidthSlider` element, add:

```xml
                        <TextBlock Classes="section" Text="PAPER"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Paper grid"/>
                                <TextBlock Classes="hint" Text="Faint dots or lines on the page, under your notes."/>
                            </StackPanel>
                            <ComboBox x:Name="PageGridBox" Grid.Column="1" Width="120" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Snap to grid"/>
                                <TextBlock Classes="hint" Text="Moving or resizing containers lands on the 20px grid."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding GridSnap, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <TextBlock Classes="hint" Margin="0,4,0,0"
                                   Text="Paper color is per notebook — right-click a notebook cover on the homepage or its chip in the rail."/>
```

- [ ] **Step 2: Wire the combo**

In `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`, in the ctor after the `NewNoteWidthSlider`
wiring, add:

```csharp
        PageGridBox.ItemsSource = new[] { "None", "Dots", "Lines" };
        PageGridBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && PageGridBox.SelectedItem is string g && vm.PageGrid != g) vm.PageGrid = g;
        };
```

In `SyncFromVm()`, after `NewNoteWidthValue.Text = ...;`, add:

```csharp
        PageGridBox.SelectedItem = vm.PageGrid;
```

(GridSnap is a plain TwoWay bool binding — no code sync needed. The stored value is one of the
three ItemsSource strings, so `SelectedItem` always resolves.)

- [ ] **Step 3: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 116/116 pass.

- [ ] **Step 4: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): Paper section in Canvas prefs — grid style + snap toggle"
```

---

### Task 5: Final integration review + relaunch + owner checklist

- [ ] Dispatch the final integration reviewer (opus) over the full Part 3 diff (`git diff afba0da..HEAD` scope: the 4 commits above) against this plan + the M8 spec Part 3.
- [ ] Fix anything Important+ inline; re-run the suite.
- [ ] Rebuild + relaunch the app for the owner.
- [ ] Update memory (`lumenotepad.md`) with the Part 3 entry.
- [ ] Hand the owner the verification checklist:
  1. Preferences → Canvas → Paper grid = Dots → faint dots appear under containers; Lines → ruled grid; theme switch keeps the grid legible.
  2. Snap to grid ON → dragging a container jumps in 20px steps and lands on the dots; resizing snaps too; clicking empty canvas starts the new box on the lattice. OFF → smooth free drag again.
  3. Right-click a homepage cover → Paper color → Sky → that notebook's page area gets a gentle blue wash; other notebooks unchanged; restart keeps it; None clears it. Also reachable from the rail chip's right-click.
  4. Reset settings clears grid + snap but NOT the per-notebook paper color.
