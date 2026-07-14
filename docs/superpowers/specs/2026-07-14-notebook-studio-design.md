# M9 · Notebook Studio — design

Date: 2026-07-14
Status: approved (owner sign-off 2026-07-14; ready for spec review → plans)
Depends on: M3 canvas model, M8 Part 3 (paper-grid engine, per-notebook data), M6 theme tokens,
the existing cover picker + crop dialog, the Motion engine, the prefs/dialog patterns.

## Goal

A guided **notebook creation wizard** (replacing instant "New notebook"), reusable **customization
windows** for notebooks / sections / pages, and a new **page-style engine** that layers note-taking
method structures (Cornell, Boxing, Charting, …) and an interactive **Mindmap** onto the freeform
canvas — all native Avalonia, honoring the standing zero-web-components constraint.

## The model: two independent axes per page

The owner's key insight: "Blank/Ruled/Grid/Dots" are a *background* concept, not a note-taking method.
So every page carries **two** orthogonal style axes:

1. **Grid style** — the paper background pattern: `Blank | Ruled | Grid | Dots`. This is M8 Part 3's
   `PageGrid` concept, extended with **Ruled** (horizontal writing lines) and made **per-page**.
2. **Page style** — the note-taking *method* structure drawn OVER the grid, UNDER the note containers:
   `Freeform | Cornell | Two-column | Outline | Boxing | Charting | Sentence | Mindmap`.

They compose: e.g. a *Dots* grid with a *Cornell* page style. Both default from the notebook, which
defaults (for grid) from the existing global `PageGrid` pref — so nothing from Part 3 is lost.

### Effective-style resolution
- Effective grid style = `Page.GridStyle ?? Notebook.DefaultGridStyle ?? mapGlobal(PageGrid)`
  where `mapGlobal`: `None→Blank`, `Dots→Dots`, `Lines→Grid`.
- Effective page style = `Page.PageStyle ?? Notebook.DefaultPageStyle ?? "Freeform"`.
- `null` on a page means "inherit"; an explicit value overrides.

## Page-style catalog (what each draws + stamps)

Non-interactive page styles render **guide lines** (thin, in the paper-muted token, same engine as the
Part 3 grid layer) and, on page creation, may **stamp starter containers** (labelled `NoteBox`es).
Geometry is computed from the page's content size so guides scale with the page.

| Style | Guides drawn | Starter containers |
|---|---|---|
| **Freeform** | none | none (today's plain canvas — the default) |
| **Cornell** | vertical divider at ~28% width; horizontal divider at ~82% height | "Cue" (left), "Notes" (right), "Summary" (bottom bar) |
| **Two-column** | one vertical divider at 50% | "" ×2 (left, right) |
| **Outline** | 3 faint vertical indent-stop guides near the left margin | one box seeded with an outline skeleton (bold heading + `dot` bullets — flat for now; the editor model has no per-paragraph indent depth yet, nested indent is a future editor feature) |
| **Boxing** | subtle rounded box outlines in a 2×2 tidy grid | 4 labelled boxes ("Topic 1"…"Topic 4") |
| **Charting** | N vertical column dividers + a header underline (default 3 columns) | 3 header-label boxes across the top ("Column 1"…) |
| **Sentence** | horizontal ruled lines (like the Ruled grid) | one box seeded with a `num` (numbered) list |
| **Mindmap** | *interactive — see below* | one central "Main idea" bubble |

### Apply modes (guide-based styles)
When creating a page with a non-Freeform, non-Mindmap style, the user picks how it applies:
- **Guides + starters** (default): draw the guides AND stamp the starter containers.
- **Starters only**: stamp the starter containers, no guides.
- **Rigid (locked)**: draw the guides AND stamp starters **locked** to their regions — a new
  `NoteBox.Locked` flag disables that box's move/resize handles and pins it to the computed region.
- (`Outline`/`Sentence` are starter-driven; for them "Guides+starters" and "Starters only" differ only
  by the faint indent/rule lines. `Freeform` and `Mindmap` ignore the apply mode.)

### Mindmap (interactive — the one special style)
Fundamentally different from the guide overlays: a lightweight node-graph editor on the canvas.
- **Bubbles** are rounded node containers (reuse `NoteBox` with an `IsBubble` flag, or a parallel
  `MindNode` list on `CanvasDocument` — Part 5 decides during its own plan). A page starts with one
  central "Main idea" bubble.
- **Add** a bubble via a canvas affordance (double-click empty space / a "+ bubble" control).
- **Link by dragging**: dragging one bubble so it overlaps another **creates a connector** between
  them (parent/child); connectors are curved lines drawn under the bubbles and **redraw live** as
  bubbles move. Dragging a linked bubble away keeps the connector; a right-click "Unlink" removes it.
- Bubbles hold short rich text (the existing editor), are draggable, and are the whole interaction —
  no guides, no apply modes.
- This is the largest single piece and ships LAST, as its own part.

## Data model changes

`Models/Workspace.cs`:
- `Notebook`: add `DefaultGridStyle` (string?, null = inherit global), `DefaultPageStyle` (string,
  "Freeform"), `DefaultPageStyleMode` (int, 0), `DefaultFont` (string?, null = app default),
  `DefaultFontSize` (double, 15). All persisted in `notebook.json` (like `Color`/`PaperTint`).
- `Page`: add `GridStyle` (string?, null = inherit), `PageStyle` (string?, null = inherit),
  `PageStyleMode` (int, 0). Persisted in the notebook tree.

`Editor/CanvasModel.cs`:
- `NoteBox`: add `Locked` (bool, false) — rigid-mode boxes; and (for Part 5) `IsBubble` (bool) +
  a `Links` list (bubble id → id) OR a separate `MindNode`/edge model. Persisted via `CanvasDocJson`.

`Services/AppSettings.cs` / VM: the global `PageGrid`/`GridSnap` stay as the app-wide grid default;
no new *global* settings are required for M9 (notebook/page carry their own styles). The notebook
default font/size extend the existing `RichTextEditor.EditorFontPref`/`EditorFontSizePref` pattern —
a note box created on a page reads its notebook's default font/size when set, else the app default.

## The page-style engine (rendering + stamping)

- **Rendering** generalizes the Part 3 `NoteCanvas._gridLayer`: a bottom-of-z-order guide layer that
  paints (a) the effective grid-style background (tiled brush — Blank/Ruled/Grid/Dots) and (b) the
  effective page-style guide lines (Cornell/Two-column/Charting/Boxing/Outline/Sentence), computed
  from `finalSize`. Theme changes re-tint via the existing `Rebuild()` path. A pure `PageStyleGuides`
  helper (unit-tested) computes the guide geometry (divider positions, column count, box rects) from a
  size, so the layout math is testable without a UI.
- **Starter stamping** happens once, at page creation, in the VM: when a page's `CanvasDocument` is
  first built for a styled page, a pure `PageStyleTemplate` helper returns the starter `NoteBox` set
  (positions, sizes, labels, `Locked` flag) for `(style, mode, sizeHint)`. Unit-tested.
- **Locked boxes**: `NoteBoxView` reads `Box.Locked` and hides the drag grip + resize handles and
  ignores drags (like the existing `CanResize` gate, extended to move).

## Creation wizard (`NotebookWizardWindow`)

A themed, chromeless two-step window (same shell language as `PreferencesWindow`/dialogs — Motion
scale-in, resize border, drag title bar). Opens instead of instantly creating a notebook.

- **Step 1 — Notebook**: name field, color (the accent swatch grid + hue/shade flyout already used in
  the gallery), cover image (reuse `PickCover` + `CoverCropDialog`), and a **sections** editor (add /
  rename / remove / reorder; ≥1 required).
- **Step 2 — Pages**: **grid style** picker + **page style** picker (thumbnail catalog with the small
  previews from the mockup) + **apply mode** radio, **default font** + **default size**, and a
  per-section **pages** editor (add / rename / remove; each section can have 0+ pages, all sharing the
  chosen defaults, individually overridable later).
- **Buttons**: `Cancel` (creates nothing, closes) · `Back` (→ Step 1) · `Next` (→ Step 2) · `Create`
  (builds the whole tree: notebook + sections + pages, each page's doc stamped with its starter
  template, then opens the notebook).
- **Edit mode**: the same window, pre-filled from an existing notebook, retitled "Customize notebook";
  `Create` becomes `Save`. Adding/removing sections/pages in edit mode mutates the real tree
  (destructive removals go through the existing confirm-delete prompts).

## Re-opening customization

- **Notebook**: a "Customize notebook…" item added to the notebook context menus (home card + rail +
  notebook-name menu) opens the wizard in edit mode.
- **Toolbar button**: a single button pinned to the **opposite end** of the editor `FormatToolbar`
  from the formatting tools (the toolbar docks top/left/right/bottom; the tools cluster at the leading
  edge, this button sits at the trailing edge with a separator) → opens the current notebook's
  customization. Keeps it clearly apart from the actual formatting controls so nothing gets cluttered.
- **Section**: "Customize section…" (section tab context menu) → a small **Section** dialog: name,
  accent color, and its pages editor.
- **Page**: "Customize page…" (page row context menu) → a small **Page** dialog: title, grid style,
  page style + apply mode, and per-page default font/size override. Changing a page's style re-stamps
  guides live; changing to a starter style on an existing page offers to add the starters (never wipes
  existing content).

## Build decomposition (each = its own plan, subagent-driven, owner-eyeballed)

1. **Part 1 — Page-style engine**: model fields; `PageStyleGuides` (guide geometry, pure+TDD);
   `PageStyleTemplate` (starter boxes, pure+TDD); `NoteBox.Locked` + `NoteBoxView` lock; generalize
   `NoteCanvas` guide layer to render grid+page styles; page-creation stamps starters. (No wizard yet —
   verified by setting styles via a temporary hook / the Page dialog stub or unit tests.)
2. **Part 2 — Creation wizard**: `NotebookWizardWindow` (2 steps) replacing `AddNotebook`'s instant
   path; Create builds the tree with styles applied.
3. **Part 3 — Re-open entry points**: context-menu "Customize notebook…" (edit mode) + the far-end
   toolbar button.
4. **Part 4 — Section & Page dialogs**: the two smaller customization windows + their context items.
5. **Part 5 — Mindmap**: the interactive bubble/link style (nodes, drag-to-link connectors, add/unlink).

After M9, resume the queued **M8 Part 6** (canvas zoom, corner roundness, custom keybindings) →
**M8 Part 7** (prefs-window declutter, mockup-first).

## Testing

Pure helpers are unit-tested: `PageStyleGuides` (divider/column/box geometry from a size),
`PageStyleTemplate` (starter box sets per style+mode, including `Locked`), effective-style resolution
(page ?? notebook ?? global), and the wizard's tree-builder (notebook+sections+pages assembled
correctly, styles/fonts applied). Window/pointer behavior (wizard flow, drag-to-link, locked-box
non-dragging, guide rendering) is owner-verified in the real app between parts, per the M7/M8 regime.

## Non-goals / constraints

- Zero web components (native Avalonia only) — unchanged standing constraint.
- No real spreadsheet/table engine for Charting v1 — it's guide lines + header starters; cells are
  ordinary freeform notes the user aligns to columns.
- Mindmap v1 is a single-level-friendly graph (bubbles + links); no auto-layout, no collapse/expand —
  those can come later if wanted.
- Page styles never destroy existing note content; applying a starter style to a non-empty page only
  ADDS starters (with a confirm), it does not clear the canvas.
