# M9 Part 1 — Page-Style Engine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-page grid styles (Blank/Ruled/Grid/Dots) and note-taking-method page styles
(Cornell/Two-column/Outline/Boxing/Charting/Sentence) with guide rendering, starter-container
stamping, lockable (rigid) containers, and a temporary page-context-menu picker.

**Architecture:** Three pure, unit-tested helpers — `PageStyles` (catalogs + effective-style
resolution), `PageStyleGuides` (guide geometry from a viewport/canvas size), `PageStyleTemplate`
(starter `NoteBox` sets) — plus a new `GuideLayer : Control` that replaces NoteCanvas's Part-3
`_gridLayer` Border and renders grid background + guide lines in one `Render` override. Model gains
`Notebook.Default*` / `Page.GridStyle|PageStyle|PageStyleMode` / `NoteBox.Locked` (all persisted).
The VM stamps starters on page creation and exposes setters; MainView pushes effective styles on page
switch and adds "Grid style" / "Page style" submenus to the page context menu (the Part-4 dialog
replaces these later).

**Tech Stack:** Avalonia 12.0.4 (`Control.Render` — NOT `Panel.Render`, which is sealed; custom
Render on a plain Control is proven by `RichTextEditor`), .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-14-notebook-studio-design.md` (Part 1 scope). Mindmap is
Part 5 — its key exists in the catalog but renders like Freeform for now and is NOT offered in menus.

**Verified facts (do not re-derive):**
- `Notebook`/`Page`/`Section` are `ObservableObject`s in `Models/Workspace.cs`; `[ObservableProperty]`
  fields generate JSON-serialized properties (persist via `notebook.json`) unless `[property: JsonIgnore]`.
- `NoteBox` (`Editor/CanvasModel.cs`) is a plain model (no Avalonia); `CanvasDocument.AddBox(x, y,
  width, doc)` appends + fires `Changed`. Page files serialize via `CanvasDocJson` DTOs
  (`Editor/CanvasJson.cs`): `BoxDto` with `[JsonPropertyName]` fields; `FromJson` rebuilds via
  `canvas.AddBox(...).H = b.H;` for live boxes and `FromDto` for trash.
- `NoteCanvas` (Part 3) has `_gridLayer` (Border, first child, arranged full-bleed via a
  `ReferenceEquals` special case), `GridStyle` ("None"|"Dots"|"Lines"), `RefreshGrid`,
  `BuildGridBrush`, `Tile`, `SnapToGrid`. `NoteBoxView.WireDrag` handles Move/Width/Height/Both;
  `RefreshChrome` gates handle visibility; `RequestDelete` / `OnEditorLostFocus` handle delete /
  empty-evaporate. Theme changes arrive as a `Document` re-set → `Rebuild()`.
- `MainView.ApplyCanvasPrefs` currently pushes `PageCanvas.GridStyle = vm.PageGrid;` — Part 1
  REPLACES that coupling with effective-style resolution (global pref maps `None→Blank`,
  `Dots→Dots`, `Lines→Grid`).
- The canvas ScrollViewer in `MainView.axaml` is unnamed (`<ScrollViewer ...><editor:NoteCanvas
  x:Name="PageCanvas"/></ScrollViewer>` inside the page Panel) — Task 5 names it `CanvasScroll`.
- Avalonia `Point`/`Rect`/`Size` are plain structs usable in unit tests without a UI platform
  (MotionTests already reference Avalonia types).
- Build gotchas: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test;
  `cd /e/CLAUDE/Lumenotepad` in every Bash call; never launch the GUI from a subagent.

**Locked geometry constants (tests assert these exact numbers):**
- Margin 16 for starter boxes; Cornell cue divider at `0.28 × viewport.Width`, summary divider at
  `0.80 × viewport.Height`; Two-column divider at `0.50 × vw`; Outline indent stops at x = 48, 88,
  128; Charting = 3 columns (dividers at vw/3, 2vw/3) + header underline at y = 64; Boxing = 2×2,
  outer margin 24, gap 16; Sentence/Ruled line spacing 28, Sentence lines start at y = 48.
- Guide pen alpha `0x26` of PaperText; Boxing rect corner radius 10.

---

### Task 1: Model fields + PageStyles resolution (TDD)

**Files:**
- Create: `src/Lumenotepad/Editor/PageStyles.cs`
- Create: `tests/Lumenotepad.Tests/PageStylesTests.cs`
- Modify: `src/Lumenotepad/Models/Workspace.cs`
- Test: `tests/Lumenotepad.Tests/WorkspaceStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Lumenotepad.Tests/PageStylesTests.cs`:

```csharp
using Lumenotepad.Editor;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStylesTests
{
    [Theory]
    [InlineData("None", "Blank")]
    [InlineData("Dots", "Dots")]
    [InlineData("Lines", "Grid")]
    [InlineData("garbage", "Blank")]
    public void MapGlobalGrid_mapsPart3Keys(string global, string expected) =>
        Assert.Equal(expected, PageStyles.MapGlobalGrid(global));

    [Fact]
    public void EffectiveGrid_pageOverNotebookOverGlobal()
    {
        var nb = new Notebook();
        var pg = new Page();
        Assert.Equal("Blank", PageStyles.EffectiveGrid(pg, nb, "None"));      // all inherit → global
        nb.DefaultGridStyle = "Ruled";
        Assert.Equal("Ruled", PageStyles.EffectiveGrid(pg, nb, "None"));      // notebook wins
        pg.GridStyle = "Dots";
        Assert.Equal("Dots", PageStyles.EffectiveGrid(pg, nb, "None"));       // page wins
    }

    [Fact]
    public void EffectiveStyle_pageOverridesNotebook_modeFollowsOwner()
    {
        var nb = new Notebook { DefaultPageStyle = "Cornell", DefaultPageStyleMode = 2 };
        var pg = new Page();
        Assert.Equal(("Cornell", 2), PageStyles.EffectiveStyle(pg, nb));      // inherit both
        pg.PageStyle = "Boxing";
        pg.PageStyleMode = 1;
        Assert.Equal(("Boxing", 1), PageStyles.EffectiveStyle(pg, nb));       // page wins both
    }

    [Fact]
    public void Defaults_freeformAndInherit()
    {
        var nb = new Notebook();
        Assert.Null(nb.DefaultGridStyle);
        Assert.Equal("Freeform", nb.DefaultPageStyle);
        Assert.Equal(0, nb.DefaultPageStyleMode);
        Assert.Null(nb.DefaultFont);
        Assert.Equal(15, nb.DefaultFontSize);
        var pg = new Page();
        Assert.Null(pg.GridStyle);
        Assert.Null(pg.PageStyle);
        Assert.Equal(0, pg.PageStyleMode);
    }
}
```

Append to `tests/Lumenotepad.Tests/WorkspaceStoreTests.cs` (inside the class — `TempDir()` exists):

```csharp
    [Fact]
    public void PageAndNotebookStyles_roundTrip()
    {
        var dir = TempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var ws = new Workspace();
            var nb = new Notebook
            {
                Name = "Styled", DefaultGridStyle = "Ruled", DefaultPageStyle = "Cornell",
                DefaultPageStyleMode = 2, DefaultFont = "Caveat", DefaultFontSize = 18,
            };
            var sec = new Section { Name = "S" };
            sec.Pages.Add(new Page { Title = "P", GridStyle = "Dots", PageStyle = "Boxing", PageStyleMode = 1 });
            sec.Pages.Add(new Page { Title = "Plain" });
            nb.Sections.Add(sec);
            ws.Notebooks.Add(nb);

            store.Save(ws);
            var loaded = new WorkspaceStore(dir).Load();

            var lnb = loaded.Notebooks[0];
            Assert.Equal("Ruled", lnb.DefaultGridStyle);
            Assert.Equal("Cornell", lnb.DefaultPageStyle);
            Assert.Equal(2, lnb.DefaultPageStyleMode);
            Assert.Equal("Caveat", lnb.DefaultFont);
            Assert.Equal(18, lnb.DefaultFontSize);
            var p0 = lnb.Sections[0].Pages[0];
            Assert.Equal("Dots", p0.GridStyle);
            Assert.Equal("Boxing", p0.PageStyle);
            Assert.Equal(1, p0.PageStyleMode);
            var p1 = lnb.Sections[0].Pages[1];
            Assert.Null(p1.GridStyle);
            Assert.Null(p1.PageStyle);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "PageStylesTests|PageAndNotebookStyles_roundTrip" 2>&1 | tail -12`
Expected: compile errors (`PageStyles`, `DefaultGridStyle`, etc. don't exist).

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Editor/PageStyles.cs`:

```csharp
using Lumenotepad.Models;

namespace Lumenotepad.Editor;

/// <summary>The two per-page style axes (spec: M9 Notebook Studio). GRID styles are the paper
/// background pattern; PAGE styles are the note-taking-method structure drawn over it. Pure —
/// key catalogs + effective-style resolution, no Avalonia.</summary>
public static class PageStyles
{
    // Grid styles (paper background). "Ruled" is new; the rest map from the Part-3 global pref.
    public const string Blank = "Blank", Ruled = "Ruled", Grid = "Grid", Dots = "Dots";
    public static readonly string[] GridStyles = { Blank, Ruled, Grid, Dots };

    // Page styles (methods). Mindmap is reserved for M9 Part 5 — renders like Freeform until then.
    public const string Freeform = "Freeform", Cornell = "Cornell", TwoColumn = "Two-column",
        Outline = "Outline", Boxing = "Boxing", Charting = "Charting", Sentence = "Sentence",
        Mindmap = "Mindmap";
    public static readonly string[] Styles =
        { Freeform, Cornell, TwoColumn, Outline, Boxing, Charting, Sentence };

    // Apply modes for the guide-based styles.
    public const int ModeGuides = 0;       // guides + starter containers
    public const int ModeStartersOnly = 1; // starter containers, no guides
    public const int ModeRigid = 2;        // guides + LOCKED starter containers

    /// <summary>The app-wide Part-3 grid pref ("None"|"Dots"|"Lines") → a grid-style key.</summary>
    public static string MapGlobalGrid(string pageGrid) => pageGrid switch
    {
        "Dots" => Dots,
        "Lines" => Grid,
        _ => Blank,
    };

    /// <summary>Effective grid style: page ?? notebook ?? global pref.</summary>
    public static string EffectiveGrid(Page page, Notebook nb, string globalPageGrid) =>
        page.GridStyle ?? nb.DefaultGridStyle ?? MapGlobalGrid(globalPageGrid);

    /// <summary>Effective page style + apply mode: an explicit page style carries its own mode;
    /// inheriting takes both from the notebook.</summary>
    public static (string Style, int Mode) EffectiveStyle(Page page, Notebook nb) =>
        page.PageStyle is { } s ? (s, page.PageStyleMode) : (nb.DefaultPageStyle, nb.DefaultPageStyleMode);
}
```

In `src/Lumenotepad/Models/Workspace.cs`:

Inside `Notebook`, after the `_paperTint` field (Part 3), add:

```csharp
    /// <summary>Per-notebook style defaults for NEW pages (M9). Grid null = inherit the global
    /// paper-grid pref; font null = the app default. Set by the notebook wizard / customization.</summary>
    [ObservableProperty] private string? _defaultGridStyle;
    [ObservableProperty] private string _defaultPageStyle = "Freeform";
    [ObservableProperty] private int _defaultPageStyleMode;
    [ObservableProperty] private string? _defaultFont;
    [ObservableProperty] private double _defaultFontSize = 15;
```

Inside `Page`, after the `_title` field, add:

```csharp
    /// <summary>Per-page style overrides (M9): null = inherit the notebook's default. An explicit
    /// PageStyle carries its own apply mode (0 guides+starters, 1 starters only, 2 rigid).</summary>
    [ObservableProperty] private string? _gridStyle;
    [ObservableProperty] private string? _pageStyle;
    [ObservableProperty] private int _pageStyleMode;
```

(Also update the stale `Page` doc comment "Content ... for now just a title" to
"A single page: title + per-page style overrides; canvas content lives in its page file.")

- [ ] **Step 4: Run full suite — green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -4`
Expected: 0 failures (139 + 8 new cases).

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): page/notebook style model fields + PageStyles resolution"
```

---

### Task 2: NoteBox.Locked — model, persistence, view gates (TDD)

**Files:**
- Modify: `src/Lumenotepad/Editor/CanvasModel.cs`
- Modify: `src/Lumenotepad/Editor/CanvasJson.cs`
- Modify: `src/Lumenotepad/Editor/NoteCanvas.cs` (NoteBoxView gates + delete/evaporate guards)
- Test: `tests/Lumenotepad.Tests/PagePersistenceTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `tests/Lumenotepad.Tests/PagePersistenceTests.cs` (inside the class; follow its existing
using/imports — it already exercises `CanvasDocJson`):

```csharp
    [Fact]
    public void LockedBox_roundTripsThroughJson()
    {
        var canvas = new CanvasDocument();
        canvas.AddBox(10, 20, 300).Locked = true;
        canvas.AddBox(40, 400, 300);                       // unlocked stays unlocked

        var reloaded = CanvasDocJson.FromJson(CanvasDocJson.ToJson(canvas));

        Assert.Equal(2, reloaded.Boxes.Count);
        Assert.True(reloaded.Boxes[0].Locked);
        Assert.False(reloaded.Boxes[1].Locked);
        // default-false stays off the wire (WhenWritingDefault)
        Assert.DoesNotContain("\"lk\":false", CanvasDocJson.ToJson(canvas));
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "LockedBox_roundTripsThroughJson" 2>&1 | tail -8`
Expected: compile error (`Locked` doesn't exist).

- [ ] **Step 3: Implement**

In `src/Lumenotepad/Editor/CanvasModel.cs`, inside `NoteBox`, after the `H` property, add:

```csharp
    /// <summary>Rigid page-style starters: a locked box cannot be moved, resized, or deleted, and
    /// never evaporates when empty (M9). Persisted.</summary>
    public bool Locked { get; set; }
```

In `src/Lumenotepad/Editor/CanvasJson.cs`:

1. In `BoxDto`, after the `H` property, add:

```csharp
        [JsonPropertyName("lk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Locked { get; set; }
```

2. `ToDto` becomes:

```csharp
    private static BoxDto ToDto(NoteBox b) => new()
    {
        X = b.X, Y = b.Y, W = b.Width, H = b.H, Locked = b.Locked,
        Paras = RichDocJson.ToDtos(b.Doc),
    };
```

3. `FromDto` becomes:

```csharp
    private static NoteBox FromDto(BoxDto b) => new(RichDocJson.FromDtos(b.Paras))
    {
        X = b.X, Y = b.Y, Width = b.W, H = b.H, Locked = b.Locked,
    };
```

4. In `FromJson`, the live-box loop currently reads
`canvas.AddBox(b.X, b.Y, b.W, RichDocJson.FromDtos(b.Paras)).H = b.H;` — replace with:

```csharp
                    foreach (var b in dto.Boxes)
                    {
                        var box = canvas.AddBox(b.X, b.Y, b.W, RichDocJson.FromDtos(b.Paras));
                        box.H = b.H;
                        box.Locked = b.Locked;
                    }
```

In `src/Lumenotepad/Editor/NoteCanvas.cs` (four small gates):

1. `NoteCanvas.RequestDelete` — add a locked guard as the FIRST line of the method body:

```csharp
        if (view.Box.Locked) return;                       // rigid starters can't be deleted
```

2. `NoteCanvas.OnEditorLostFocus` — the posted lambda's early-return currently reads
`if (!view.Box.IsEmpty || view.IsKeyboardFocusWithin) return;` — change to:

```csharp
            if (!view.Box.IsEmpty || view.Box.Locked || view.IsKeyboardFocusWithin) return;
```

3. `NoteBoxView.RefreshChrome` — locked boxes hide the ✕ and the resize handles. Change the two
lines that set `_close.IsVisible` and the resize visibility to:

```csharp
        _close.IsVisible = active && !Box.Locked;
        // Hidden handles are also not hit-testable — the "Resizable pages" preference off = no resizing.
        _resizeRight.IsVisible = _resizeBottom.IsVisible = _resizeCorner.IsVisible =
            _canvas.CanResize && !Box.Locked;
```

4. `NoteBoxView.WireDrag` — locked boxes never start a drag. In the `PointerPressed` handler, after
the `IsLeftButtonPressed` check, add:

```csharp
            if (Box.Locked) return;                        // rigid starters: no move, no resize
```

- [ ] **Step 4: Run full suite — green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -4`
Expected: 0 failures.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): NoteBox.Locked — persisted, un-draggable, un-deletable, never evaporates"
```

---

### Task 3: PageStyleGuides — pure guide geometry (TDD)

**Files:**
- Create: `src/Lumenotepad/Editor/PageStyleGuides.cs`
- Create: `tests/Lumenotepad.Tests/PageStyleGuidesTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Lumenotepad.Tests/PageStyleGuidesTests.cs`:

```csharp
using Avalonia;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStyleGuidesTests
{
    private static readonly Size Vp = new(900, 600);
    private static readonly Size Canvas = new(1200, 1500);

    [Theory]
    [InlineData("Freeform")]
    [InlineData("Mindmap")]
    public void FreeformAndMindmap_drawNothing(string style)
    {
        var g = PageStyleGuides.For(style, Vp, Canvas);
        Assert.Empty(g.Lines);
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void Cornell_cueAndSummaryDividers()
    {
        var g = PageStyleGuides.For(PageStyles.Cornell, Vp, Canvas);
        Assert.Equal(2, g.Lines.Count);
        Assert.Equal((new Point(252, 0), new Point(252, 480)), g.Lines[0]);   // cue: 0.28×900, down to summary
        Assert.Equal((new Point(0, 480), new Point(1200, 480)), g.Lines[1]);  // summary: 0.80×600, full canvas width
        Assert.Empty(g.Boxes);
    }

    [Fact]
    public void TwoColumn_singleDivider_fullCanvasHeight()
    {
        var g = PageStyleGuides.For(PageStyles.TwoColumn, Vp, Canvas);
        var line = Assert.Single(g.Lines);
        Assert.Equal((new Point(450, 0), new Point(450, 1500)), line);
    }

    [Fact]
    public void Outline_threeIndentStops()
    {
        var g = PageStyleGuides.For(PageStyles.Outline, Vp, Canvas);
        Assert.Equal(3, g.Lines.Count);
        Assert.Equal(48, g.Lines[0].A.X);
        Assert.Equal(88, g.Lines[1].A.X);
        Assert.Equal(128, g.Lines[2].A.X);
        Assert.All(g.Lines, l => Assert.Equal(1500, l.B.Y));
    }

    [Fact]
    public void Charting_threeColumnsPlusHeader()
    {
        var g = PageStyleGuides.For(PageStyles.Charting, Vp, Canvas);
        Assert.Equal(3, g.Lines.Count);
        Assert.Equal((new Point(300, 0), new Point(300, 1500)), g.Lines[0]);
        Assert.Equal((new Point(600, 0), new Point(600, 1500)), g.Lines[1]);
        Assert.Equal((new Point(0, 64), new Point(1200, 64)), g.Lines[2]);    // header underline
    }

    [Fact]
    public void Boxing_fourRects()
    {
        var g = PageStyleGuides.For(PageStyles.Boxing, Vp, Canvas);
        Assert.Empty(g.Lines);
        Assert.Equal(4, g.Boxes.Count);
        Assert.Equal(new Rect(24, 24, 418, 268), g.Boxes[0]);   // (900−48−16)/2 × (600−48−16)/2
        Assert.Equal(new Rect(458, 24, 418, 268), g.Boxes[1]);
        Assert.Equal(new Rect(24, 308, 418, 268), g.Boxes[2]);
        Assert.Equal(new Rect(458, 308, 418, 268), g.Boxes[3]);
    }

    [Fact]
    public void Sentence_ruledLinesEvery28_fromY48_downTheCanvas()
    {
        var g = PageStyleGuides.For(PageStyles.Sentence, Vp, Canvas);
        Assert.Equal(52, g.Lines.Count);                        // 48 + 28k ≤ 1500 → k = 0..51
        Assert.Equal((new Point(0, 48), new Point(1200, 48)), g.Lines[0]);
        Assert.Equal(76, g.Lines[1].A.Y);
    }

    [Fact]
    public void ZeroViewport_fallsBackToCanvasSize()
    {
        var g = PageStyleGuides.For(PageStyles.TwoColumn, default, Canvas);
        Assert.Equal(600, Assert.Single(g.Lines).A.X);          // 0.5 × canvas width
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "PageStyleGuidesTests" 2>&1 | tail -8`
Expected: compile errors (`PageStyleGuides` doesn't exist).

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Editor/PageStyleGuides.cs`:

```csharp
using System.Collections.Generic;
using Avalonia;

namespace Lumenotepad.Editor;

/// <summary>Pure guide geometry for the page styles: which lines/boxes a style draws, computed from
/// the VIEWPORT (divider positions — the "one screen" the method is designed around) and the CANVAS
/// (how far lines extend as the page grows). Rendered by GuideLayer; unit-tested here.</summary>
public static class PageStyleGuides
{
    public sealed record GuideSet(
        IReadOnlyList<(Point A, Point B)> Lines,
        IReadOnlyList<Rect> Boxes)
    {
        public static readonly GuideSet Empty = new(new List<(Point, Point)>(), new List<Rect>());
    }

    public const double RuleSpacing = 28;   // Sentence/Ruled line pitch
    public const double RuleTop = 48;       // first Sentence rule
    public const double HeaderY = 64;       // Charting header underline
    public const double BoxMargin = 24;     // Boxing outer margin
    public const double BoxGap = 16;        // Boxing gap between boxes

    public static GuideSet For(string pageStyle, Size viewport, Size canvas)
    {
        // Divider positions come from the viewport; a zero viewport (not yet measured) uses the canvas.
        double vw = viewport.Width > 0 ? viewport.Width : canvas.Width;
        double vh = viewport.Height > 0 ? viewport.Height : canvas.Height;
        double cw = canvas.Width, ch = canvas.Height;
        var lines = new List<(Point, Point)>();
        var boxes = new List<Rect>();

        switch (pageStyle)
        {
            case PageStyles.Cornell:
                double cue = vw * 0.28, sum = vh * 0.80;
                lines.Add((new Point(cue, 0), new Point(cue, sum)));
                lines.Add((new Point(0, sum), new Point(cw, sum)));
                break;
            case PageStyles.TwoColumn:
                lines.Add((new Point(vw * 0.5, 0), new Point(vw * 0.5, ch)));
                break;
            case PageStyles.Outline:
                foreach (double x in new[] { 48.0, 88.0, 128.0 })
                    lines.Add((new Point(x, 0), new Point(x, ch)));
                break;
            case PageStyles.Charting:
                lines.Add((new Point(vw / 3, 0), new Point(vw / 3, ch)));
                lines.Add((new Point(vw * 2 / 3, 0), new Point(vw * 2 / 3, ch)));
                lines.Add((new Point(0, HeaderY), new Point(cw, HeaderY)));
                break;
            case PageStyles.Boxing:
                double bw = (vw - 2 * BoxMargin - BoxGap) / 2, bh = (vh - 2 * BoxMargin - BoxGap) / 2;
                boxes.Add(new Rect(BoxMargin, BoxMargin, bw, bh));
                boxes.Add(new Rect(BoxMargin + bw + BoxGap, BoxMargin, bw, bh));
                boxes.Add(new Rect(BoxMargin, BoxMargin + bh + BoxGap, bw, bh));
                boxes.Add(new Rect(BoxMargin + bw + BoxGap, BoxMargin + bh + BoxGap, bw, bh));
                break;
            case PageStyles.Sentence:
                for (double y = RuleTop; y <= ch; y += RuleSpacing)
                    lines.Add((new Point(0, y), new Point(cw, y)));
                break;
        }
        return lines.Count == 0 && boxes.Count == 0 ? GuideSet.Empty : new GuideSet(lines, boxes);
    }
}
```

- [ ] **Step 4: Run full suite — green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -4`
Expected: 0 failures.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): PageStyleGuides — pure guide geometry for the page styles"
```

---

### Task 4: PageStyleTemplate — starter containers (TDD)

**Files:**
- Create: `src/Lumenotepad/Editor/PageStyleTemplate.cs`
- Create: `tests/Lumenotepad.Tests/PageStyleTemplateTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Lumenotepad.Tests/PageStyleTemplateTests.cs`:

```csharp
using System.Linq;
using Avalonia;
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class PageStyleTemplateTests
{
    private static readonly Size Vp = new(900, 600);

    [Theory]
    [InlineData("Freeform")]
    [InlineData("Mindmap")]
    public void FreeformAndMindmap_noStarters(string style) =>
        Assert.Empty(PageStyleTemplate.StartersFor(style, PageStyles.ModeGuides, Vp));

    [Fact]
    public void Cornell_threeLabelledRegions()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Cornell, PageStyles.ModeGuides, Vp);
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Cue", boxes[0].Doc.GetText());
        Assert.Equal("Notes", boxes[1].Doc.GetText());
        Assert.Equal("Summary", boxes[2].Doc.GetText());
        Assert.Equal(16, boxes[0].X);            // cue region, margin 16
        Assert.Equal(220, boxes[0].Width);       // 252 − 32
        Assert.Equal(268, boxes[1].X);           // 252 + 16
        Assert.Equal(616, boxes[1].Width);       // 900 − 252 − 32
        Assert.Equal(492, boxes[2].Y);           // 480 + 12
        Assert.All(boxes, b => Assert.False(b.Locked));
        Assert.All(boxes, b => Assert.Equal(0, b.H));           // auto height when not rigid
    }

    [Fact]
    public void Rigid_locksAndFixesHeights()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Cornell, PageStyles.ModeRigid, Vp);
        Assert.All(boxes, b => Assert.True(b.Locked));
        Assert.Equal(448, boxes[0].H);           // 480 − 32
        Assert.Equal(448, boxes[1].H);
        Assert.Equal(92, boxes[2].H);            // 600 − 480 − 28
    }

    [Fact]
    public void Boxing_fourTopics_insetInsideGuideRects()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Boxing, PageStyles.ModeGuides, Vp);
        Assert.Equal(4, boxes.Count);
        Assert.Equal("Topic 1", boxes[0].Doc.GetText());
        Assert.Equal(36, boxes[0].X);            // 24 + 12 inset
        Assert.Equal(394, boxes[0].Width);       // 418 − 24
    }

    [Fact]
    public void Charting_threeBoldHeaders()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.Charting, PageStyles.ModeGuides, Vp);
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Column 1", boxes[0].Doc.GetText());
        Assert.True(boxes[0].Doc.Paragraphs[0].Runs[0].Bold);
        Assert.Equal(316, boxes[1].X);           // 300 + 16
    }

    [Fact]
    public void Outline_singleSkeletonBox()
    {
        var box = Assert.Single(PageStyleTemplate.StartersFor(PageStyles.Outline, PageStyles.ModeGuides, Vp));
        Assert.Equal("Topic\nMain idea\nSupporting detail", box.Doc.GetText());
        Assert.True(box.Doc.Paragraphs[0].Runs[0].Bold);
        Assert.Equal("dot", box.Doc.Paragraphs[1].Bullet);
        Assert.Equal("dot", box.Doc.Paragraphs[2].Bullet);
    }

    [Fact]
    public void Sentence_numberedStarter()
    {
        var box = Assert.Single(PageStyleTemplate.StartersFor(PageStyles.Sentence, PageStyles.ModeGuides, Vp));
        Assert.Equal("First point", box.Doc.GetText());
        Assert.Equal("num", box.Doc.Paragraphs[0].Bullet);
    }

    [Fact]
    public void TwoColumn_twoColumns()
    {
        var boxes = PageStyleTemplate.StartersFor(PageStyles.TwoColumn, PageStyles.ModeStartersOnly, Vp);
        Assert.Equal(2, boxes.Count);
        Assert.Equal("Column 1", boxes[0].Doc.GetText());
        Assert.Equal(466, boxes[1].X);           // 450 + 16
        Assert.All(boxes, b => Assert.False(b.Locked));         // starters-only never locks
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "PageStyleTemplateTests" 2>&1 | tail -8`
Expected: compile errors (`PageStyleTemplate` doesn't exist).

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/Editor/PageStyleTemplate.cs`:

```csharp
using System.Collections.Generic;
using Avalonia;

namespace Lumenotepad.Editor;

/// <summary>Pure starter-container templates: the labelled NoteBoxes a page style stamps onto a
/// fresh page, positioned to match PageStyleGuides' regions. Mode 2 (rigid) locks the boxes and
/// fixes their heights to the regions; other modes leave them movable with auto height.</summary>
public static class PageStyleTemplate
{
    public const double Margin = 16;

    public static IReadOnlyList<NoteBox> StartersFor(string pageStyle, int mode, Size viewport)
    {
        double vw = viewport.Width > 0 ? viewport.Width : 900;
        double vh = viewport.Height > 0 ? viewport.Height : 600;
        bool rigid = mode == PageStyles.ModeRigid;
        var list = new List<NoteBox>();

        void Add(double x, double y, double w, double h, RichDocument doc)
        {
            list.Add(new NoteBox(doc) { X = x, Y = y, Width = w, H = rigid ? h : 0, Locked = rigid });
        }

        switch (pageStyle)
        {
            case PageStyles.Cornell:
            {
                double cue = vw * 0.28, sum = vh * 0.80;
                Add(Margin, Margin, cue - 2 * Margin, sum - 2 * Margin, Label("Cue"));
                Add(cue + Margin, Margin, vw - cue - 2 * Margin, sum - 2 * Margin, Label("Notes"));
                Add(Margin, sum + 12, vw - 2 * Margin, vh - sum - 28, Label("Summary"));
                break;
            }
            case PageStyles.TwoColumn:
            {
                double half = vw * 0.5;
                Add(Margin, Margin, half - 2 * Margin, vh - 2 * Margin, Label("Column 1"));
                Add(half + Margin, Margin, half - 2 * Margin, vh - 2 * Margin, Label("Column 2"));
                break;
            }
            case PageStyles.Outline:
            {
                var doc = Label("Topic");
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Main idea" } }, Bullet = "dot" });
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "Supporting detail" } }, Bullet = "dot" });
                Add(Margin, Margin, vw - 200, vh - 2 * Margin, doc);
                break;
            }
            case PageStyles.Boxing:
            {
                double bw = (vw - 2 * PageStyleGuides.BoxMargin - PageStyleGuides.BoxGap) / 2;
                double bh = (vh - 2 * PageStyleGuides.BoxMargin - PageStyleGuides.BoxGap) / 2;
                int n = 1;
                foreach (var (rx, ry) in new[]
                {
                    (PageStyleGuides.BoxMargin, PageStyleGuides.BoxMargin),
                    (PageStyleGuides.BoxMargin + bw + PageStyleGuides.BoxGap, PageStyleGuides.BoxMargin),
                    (PageStyleGuides.BoxMargin, PageStyleGuides.BoxMargin + bh + PageStyleGuides.BoxGap),
                    (PageStyleGuides.BoxMargin + bw + PageStyleGuides.BoxGap, PageStyleGuides.BoxMargin + bh + PageStyleGuides.BoxGap),
                })
                    Add(rx + 12, ry + 12, bw - 24, bh - 24, Label($"Topic {n++}"));
                break;
            }
            case PageStyles.Charting:
            {
                double col = vw / 3;
                for (int i = 0; i < 3; i++)
                    Add(i * col + Margin, Margin, col - 2 * Margin, 40, Label($"Column {i + 1}"));
                break;
            }
            case PageStyles.Sentence:
            {
                var doc = new RichDocument();
                doc.Paragraphs.Clear();
                doc.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = "First point" } }, Bullet = "num" });
                Add(Margin, 40, vw - 2 * Margin, vh - 80, doc);
                break;
            }
        }
        return list;
    }

    /// <summary>A one-line bold label document.</summary>
    private static RichDocument Label(string text)
    {
        var d = new RichDocument();
        d.Paragraphs.Clear();
        d.Paragraphs.Add(new Paragraph { Runs = { new RichRun { Text = text, Bold = true } } });
        return d;
    }
}
```

- [ ] **Step 4: Run full suite — green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -4`
Expected: 0 failures.

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): PageStyleTemplate — starter containers per style + apply mode"
```

---

### Task 5: GuideLayer control + NoteCanvas integration

**Files:**
- Create: `src/Lumenotepad/Editor/GuideLayer.cs`
- Modify: `src/Lumenotepad/Editor/NoteCanvas.cs`

No new unit tests (rendering — geometry is already tested); the suite must stay green.

- [ ] **Step 1: Create the GuideLayer control**

Create `src/Lumenotepad/Editor/GuideLayer.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumenotepad.Editor;

/// <summary>The canvas's bottom layer: paints the grid-style paper background (tiled brush) and the
/// page-style guide lines/boxes in one Render pass. A plain Control (Panel.Render is sealed — the
/// same lesson as the Part-3 Border layer this replaces, but guides need real draw calls, and a
/// Control CAN override Render: RichTextEditor proves it).</summary>
public sealed class GuideLayer : Control
{
    private string _gridStyle = PageStyles.Blank;
    private string _pageStyle = PageStyles.Freeform;
    private int _mode;
    private IBrush? _gridBrush;

    public GuideLayer() => IsHitTestVisible = false;

    /// <summary>The viewport (visible page area) — divider positions anchor to it, not the growing
    /// canvas. Pushed by MainView from the ScrollViewer.</summary>
    public Size Viewport { get; set; }

    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _gridStyle = gridStyle;
        _pageStyle = pageStyle;
        _mode = mode;
        Refresh();
    }

    /// <summary>Rebuild the theme-derived brush + repaint (theme changes arrive via canvas Rebuild).</summary>
    public void Refresh()
    {
        _gridBrush = BuildGridBrush(_gridStyle);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (_gridBrush is not null) ctx.FillRectangle(_gridBrush, new Rect(size));
        if (_mode == PageStyles.ModeStartersOnly) return;          // starters-only: no guides
        var set = PageStyleGuides.For(_pageStyle, Viewport, size);
        if (set.Lines.Count == 0 && set.Boxes.Count == 0) return;
        var pen = new Pen(new SolidColorBrush(
            Color.Parse(Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x26))), 1);
        foreach (var (a, b) in set.Lines) ctx.DrawLine(pen, a, b);
        foreach (var r in set.Boxes) ctx.DrawRectangle(null, pen, r, 10, 10);
    }

    // ---- grid-style paper backgrounds (tiled brushes — one cell, GPU-repeated) ----

    private static IBrush? BuildGridBrush(string style)
    {
        var t = Services.ThemeManager.Current;
        if (style == PageStyles.Dots)
        {
            // Full dots at all four tile corners: each is clipped to its quarter inside the cell
            // and the neighbouring tiles complete it — whole dots exactly on the 20px lattice.
            var g = new GeometryGroup();
            foreach (var (x, y) in new[] { (0.0, 0.0), (GridMath.Cell, 0.0), (0.0, GridMath.Cell), (GridMath.Cell, GridMath.Cell) })
                g.Children.Add(new EllipseGeometry(new Rect(x - 1.1, y - 1.1, 2.2, 2.2)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Brush = new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x30))),
            }, GridMath.Cell);
        }
        if (style == PageStyles.Grid)
        {
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(GridMath.Cell, 0)));
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, GridMath.Cell)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            }, GridMath.Cell);
        }
        if (style == PageStyles.Ruled)
        {
            var g = new GeometryGroup();
            g.Children.Add(new LineGeometry(new Point(0, 0), new Point(PageStyleGuides.RuleSpacing, 0)));
            return Tile(new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(t.PaperText, 0x1E)))),
            }, PageStyleGuides.RuleSpacing);
        }
        return null;                                              // Blank
    }

    private static DrawingBrush Tile(Drawing cell, double size) => new()
    {
        Drawing = cell, TileMode = TileMode.Tile, Stretch = Stretch.None,
        SourceRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
    };
}
```

- [ ] **Step 2: Swap NoteCanvas onto the GuideLayer**

In `src/Lumenotepad/Editor/NoteCanvas.cs`:

1. DELETE the Part-3 members: the `GridStyle` property + `_gridStyle` field, the `_gridLayer` Border
   field, `RefreshGrid()`, `BuildGridBrush(...)`, and `Tile(...)` (their logic now lives in
   GuideLayer). KEEP `SnapToGrid` and all snap code exactly as-is.

2. Add the replacement members where `GridStyle` used to be:

```csharp
    // The bottom guide layer: grid-style paper background + page-style guide lines (M9).
    private readonly GuideLayer _guides = new();

    /// <summary>Push the page's effective styles (grid background, method guides, apply mode).</summary>
    public void SetStyles(string gridStyle, string pageStyle, int mode) =>
        _guides.SetStyles(gridStyle, pageStyle, mode);

    /// <summary>The visible page area — guide dividers anchor to it (MainView pushes it on layout).</summary>
    public void SetViewport(Size viewport)
    {
        _guides.Viewport = viewport;
        _guides.InvalidateVisual();
    }
```

3. In `Rebuild()`, the two lines that referenced the old layer:

```csharp
        Children.Add(_gridLayer);      // first child = bottom of z-order: under every container
        RefreshGrid();                 // theme changes arrive as a Document reset — re-tint here
```

become:

```csharp
        Children.Add(_guides);         // first child = bottom of z-order: under every container
        _guides.Refresh();             // theme changes arrive as a Document reset — re-tint here
```

4. In `ArrangeOverride`, the special case `if (ReferenceEquals(child, _gridLayer))` becomes
   `if (ReferenceEquals(child, _guides))` (body unchanged: arrange to `new Rect(finalSize)`,
   continue).

5. `MeasureOverride` needs no change (GuideLayer has no content; the generic
   `child.Measure(Size.Infinity)` path yields 0×0, and arrange fills anyway).

NOTE: `MainView.ApplyCanvasPrefs` still references the removed `PageCanvas.GridStyle` — the build
stays RED until Task 6 rewires MainView. To keep this task independently green, ALSO apply the ONE
MainView line-change now: in `src/Lumenotepad/Views/MainView.axaml.cs`, `ApplyCanvasPrefs()`, replace

```csharp
        PageCanvas.GridStyle = vm.PageGrid;
```

with

```csharp
        ApplyPageStyles();             // per-page effective styles (falls back to the global grid pref)
```

and add a minimal `ApplyPageStyles` right after `ApplyCanvasPrefs` (Task 6 keeps it, unchanged):

```csharp
    /// <summary>Resolve the selected page's effective grid + page styles (page ?? notebook ?? global
    /// pref) and push them onto the canvas guide layer.</summary>
    private void ApplyPageStyles()
    {
        if (Vm is not { } vm) return;
        string grid = vm.SelectedPage is { } pg && vm.SelectedNotebook is { } nb
            ? PageStyles.EffectiveGrid(pg, nb, vm.PageGrid)
            : PageStyles.MapGlobalGrid(vm.PageGrid);
        var (style, mode) = vm.SelectedPage is { } p && vm.SelectedNotebook is { } n
            ? PageStyles.EffectiveStyle(p, n)
            : (PageStyles.Freeform, 0);
        PageCanvas.SetStyles(grid, style, mode);
    }
```

- [ ] **Step 3: Build + full suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 0 test failures.

- [ ] **Step 4: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): GuideLayer — grid backgrounds + page-style guides on the canvas"
```

---

### Task 6: VM stamping + MainView wiring + context-menu pickers

**Files:**
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Modify: `src/Lumenotepad/Views/MainView.axaml` (name the canvas ScrollViewer)
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`
- Test: `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `tests/Lumenotepad.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public void AddPage_stampsStarters_whenNotebookDefaultsToAStyle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.SelectedNotebook!.DefaultPageStyle = "Cornell";

            vm.AddPageCommand.Execute(null);

            var doc = vm.DocumentFor(vm.SelectedPage!);
            Assert.Equal(3, doc.Boxes.Count);                   // Cue / Notes / Summary
            Assert.Equal("Cue", doc.Boxes[0].Doc.GetText());

            vm.SelectedNotebook!.DefaultPageStyle = "Freeform";
            vm.AddPageCommand.Execute(null);
            Assert.Empty(vm.DocumentFor(vm.SelectedPage!).Boxes);   // Freeform stamps nothing
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetPageStyleChoice_persists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var pg = vm.SelectedPage!;

            vm.SetPageStyleChoice(pg, "Boxing", 2);
            vm.SetPageGridStyle(pg, "Ruled");

            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);
            var rp = reloaded.Notebooks[0].Sections[0].Pages.First(p => p.Id == pg.Id);
            Assert.Equal("Boxing", rp.PageStyle);
            Assert.Equal(2, rp.PageStyleMode);
            Assert.Equal("Ruled", rp.GridStyle);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "AddPage_stampsStarters_whenNotebookDefaultsToAStyle|SetPageStyleChoice_persists" 2>&1 | tail -10`
Expected: compile errors (`SetPageStyleChoice` etc. don't exist).

- [ ] **Step 3: Implement the VM side**

In `src/Lumenotepad/ViewModels/MainViewModel.cs`:

1. Near `SettingsDir` (the misc public surface), add:

```csharp
    /// <summary>The canvas viewport (visible page area), pushed by the view — page-style templates
    /// and guides anchor their regions to it. Plain doubles: the VM stays Avalonia-layout-free.</summary>
    public (double W, double H) CanvasViewport { get; set; } = (900, 600);
```

2. `AddPage` currently adds the page, selects it, saves. Add stamping between select and save:

```csharp
    [RelayCommand]
    private void AddPage()
    {
        if (SelectedSection is not { } sec) return;
        var pg = new Page { Title = "Untitled page" };
        sec.Pages.Add(pg);
        SelectedPage = pg;
        StampPageStyle(pg);                    // starter containers per the effective page style
        Save();
    }
```

3. After `SetNotebookPaperTint`, add the style API:

```csharp
    /// <summary>Set (or clear with null) a page's grid style; persists the tree.</summary>
    public void SetPageGridStyle(Page pg, string? gridStyle)
    {
        pg.GridStyle = gridStyle;
        Save();
    }

    /// <summary>Set (or clear with null) a page's page style; an explicit style carries its mode.</summary>
    public void SetPageStyleChoice(Page pg, string? pageStyle, int? mode = null)
    {
        pg.PageStyle = pageStyle;
        if (mode is { } m) pg.PageStyleMode = m;
        Save();
    }

    /// <summary>Stamp the page's effective style's starter containers into its document (no-op for
    /// Freeform/Mindmap). Additive — never clears existing content. Flushes so the stamp persists.</summary>
    public void StampPageStyle(Page page)
    {
        var owner = FindOwner(page) ?? SelectedNotebook;
        if (owner is null) return;
        var (style, mode) = Editor.PageStyles.EffectiveStyle(page, owner);
        var starters = Editor.PageStyleTemplate.StartersFor(style, mode,
            new Avalonia.Size(CanvasViewport.W, CanvasViewport.H));
        if (starters.Count == 0) return;
        var doc = DocumentFor(page);
        foreach (var b in starters)
        {
            var added = doc.AddBox(b.X, b.Y, b.Width, b.Doc);
            added.H = b.H;
            added.Locked = b.Locked;
        }
        FlushDirtyDocs();
    }
```

(NOTE: `PageStyleTemplate.StartersFor` takes an `Avalonia.Size` — constructing one in the VM is fine;
`Avalonia.Size` is a plain struct and the VM assembly already references Avalonia.)

- [ ] **Step 4: Wire the view**

In `src/Lumenotepad/Views/MainView.axaml`, name the canvas ScrollViewer (the one wrapping
`PageCanvas`):

```xml
                        <ScrollViewer x:Name="CanvasScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                                      Margin="16,12,16,16">
                            <editor:NoteCanvas x:Name="PageCanvas"/>
                        </ScrollViewer>
```

In `src/Lumenotepad/Views/MainView.axaml.cs`:

1. In the constructor, after the `CanvasPlate.SizeChanged += ...` line, add:

```csharp
        // Guides + starter templates anchor to the visible page area.
        CanvasScroll.SizeChanged += (_, _) =>
        {
            PageCanvas.SetViewport(CanvasScroll.Bounds.Size);
            if (Vm is { } vvm) vvm.CanvasViewport = (CanvasScroll.Bounds.Width, CanvasScroll.Bounds.Height);
        };
```

2. In `SyncEditorDocument`, after `PageCanvas.Document = ...` is assigned (the line
`PageCanvas.Document = Vm?.SelectedPage is { } page ? Vm.DocumentFor(page) : null;`), add:

```csharp
        ApplyPageStyles();                     // the new page's effective grid + method guides
```

3. In `OnPagesContextRequested`, before the `delete` item is built, add the two picker submenus and
include them in `OpenMenu` BEFORE `delete`:

```csharp
        var gridMenu = new MenuItem { Header = "Grid style" };
        foreach (var (label, key) in new (string, string?)[]
                 { ("Inherit", null), ("Blank", Editor.PageStyles.Blank), ("Ruled", Editor.PageStyles.Ruled),
                   ("Grid", Editor.PageStyles.Grid), ("Dots", Editor.PageStyles.Dots) })
        {
            var item = new MenuItem
            {
                Header = label,
                FontWeight = string.Equals(pg.GridStyle, key, System.StringComparison.Ordinal)
                    ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var chosen = key;
            item.Click += (_, _) => { Vm?.SetPageGridStyle(pg, chosen); ApplyPageStyles(); };
            gridMenu.Items.Add(item);
        }

        var styleMenu = new MenuItem { Header = "Page style" };
        foreach (var (label, key) in new (string, string?)[]
                 { ("Inherit", null), ("Freeform", Editor.PageStyles.Freeform), ("Cornell", Editor.PageStyles.Cornell),
                   ("Two-column", Editor.PageStyles.TwoColumn), ("Outline", Editor.PageStyles.Outline),
                   ("Boxing", Editor.PageStyles.Boxing), ("Charting", Editor.PageStyles.Charting),
                   ("Sentence", Editor.PageStyles.Sentence) })
        {
            var item = new MenuItem
            {
                Header = label,
                FontWeight = string.Equals(pg.PageStyle, key, System.StringComparison.Ordinal)
                    ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var chosen = key;
            item.Click += (_, _) => PickPageStyle(pg, chosen);
            styleMenu.Items.Add(item);
        }
        styleMenu.Items.Add(new Separator());
        foreach (var (label, m) in new[] { ("Apply as: guides + starters", 0), ("Apply as: starters only", 1), ("Apply as: rigid (locked)", 2) })
        {
            var item = new MenuItem
            {
                Header = label, IsEnabled = pg.PageStyle is not null,
                FontWeight = pg.PageStyle is not null && pg.PageStyleMode == m
                    ? FontWeight.SemiBold : FontWeight.Normal,
            };
            int chosen = m;
            item.Click += (_, _) => { Vm?.SetPageStyleChoice(pg, pg.PageStyle, chosen); ApplyPageStyles(); };
            styleMenu.Items.Add(item);
        }
```

and change the method's final call to `OpenMenu(e, gridMenu, styleMenu, delete);`.

4. After `ApplyPageStyles` (added in Task 5), add the style-pick flow:

```csharp
    /// <summary>Temporary Part-1 entry point (the Part-4 Page dialog supersedes it): set the style,
    /// refresh the guides, and offer the starter containers — additive, never clears content.</summary>
    private async void PickPageStyle(Models.Page pg, string? style)
    {
        if (Vm is not { } vm) return;
        vm.SetPageStyleChoice(pg, style);
        ApplyPageStyles();
        if (style is null or Editor.PageStyles.Freeform) return;
        var doc = vm.DocumentFor(pg);
        if (doc.Boxes.Count > 0)
        {
            if (Window is not { } w) return;
            if (!await ConfirmDialog.Show(w, "Add starter layout?",
                $"Add the {style} starter containers to this page? Your existing notes stay untouched.",
                "Add", danger: false)) return;
        }
        vm.StampPageStyle(pg);
        if (ReferenceEquals(vm.SelectedPage, pg)) PageCanvas.Document = PageCanvas.Document;   // show the stamp
    }
```

(NOTE: `style is null or Editor.PageStyles.Freeform` — constant patterns require const values;
`PageStyles.Freeform` IS a const string, so this compiles. If the compiler disagrees, use
`style is null || style == Editor.PageStyles.Freeform`.)

- [ ] **Step 5: Build + full suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 0 failures.

- [ ] **Step 6: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m9): page-style stamping + effective-style push + context-menu pickers"
```

---

### Task 7: Final integration review + relaunch + checklist

- [ ] Dispatch a final integration reviewer (opus) over the Part 1 diff against this plan + the M9
  spec. Seams to scrutinize: the GuideLayer swap (z-order, arrange special case, theme re-tint via
  Rebuild, old GridStyle callers all gone), effective-style resolution firing on page switch AND on
  pref/notebook changes, viewport push timing (SizeChanged before first render?), locked-box gates
  (no move/resize/delete/evaporate, ✕ hidden), stamping additive-only + persisted, and the
  double-stamp path (re-picking a style on a stamped page → confirm prompt, no silent duplicates).
- [ ] Fix anything Important+ inline; re-run the suite.
- [ ] Rebuild + relaunch for the owner; update memory (`lumenotepad.md`).
- [ ] Owner checklist:
  1. Right-click a page → Page style → Cornell → cue/summary divider lines appear on the page (over
     the grid, under your notes); on a page with notes you're asked before starters are added.
  2. New page in a notebook after right-click-styling it… (notebook defaults arrive with the wizard
     in Part 2 — for now) → right-click an EMPTY new page → Cornell → Cue/Notes/Summary containers
     appear, movable; "Apply as: rigid" → re-add starters on a fresh page → they can't be moved,
     resized, or deleted, and have fixed heights.
  3. Grid style → Ruled → horizontal writing lines; Dots/Grid still work; Inherit returns to the
     global pref; theme switch keeps everything legible.
  4. Sentence → numbered starter + ruled lines; Boxing → 4 outlined topic boxes; Charting → 3
     columns + bold headers; Two-column → center divider; Outline → indent stops + skeleton.
  5. Restart: styles, guides, starters, and locked-ness all persist.
