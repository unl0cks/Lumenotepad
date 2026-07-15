# PF5 — Rename overlay (Zen-style), invisible-new-items fix, toolbar font scroll, square size buttons, reliable toggles

Owner feedback round (2026-07-15, screenshots: dark + pink sections strips, a mid-rename chip,
two Zen-browser YouTube shots showing the blur-overlay pattern they want):

1. Smooth scrolling missing in the toolbar's font list flyout.
2. NEW pages/sections are invisible until the notebook is exited + re-entered.
3. Renaming a section/page still too hard to see → owner asks for a ZOOMED, CENTERED rename
   box with EVERYTHING behind it blurred (like Zen/YouTube's search overlay).
4. The toolbar "Smaller"/"Larger" size buttons aren't square (24×30).
5. Prefs toggle animations only work sometimes.

## Root causes

- **2 (CONFIRMED by code trace):** adding a section/page queues two Background posts — the new
  container's `Motion.RiseIn` (RiseAdded), then `ReassertListSelection` → `UpdateSelectionScale`
  → `ScaleSelect` which starts a selection-scale `Motion.Tween` on the SAME container. One tween
  per element: the scale tween `Stop()`s the rise at opacity ≈ 0 and carries NO opacity params →
  the container is stranded invisible. Exiting/re-entering re-prepares containers (PF1 reset) →
  visible again. Notebooks were immune (their scale targets the inner railchip Border, not the
  container). Fix: `ScaleSelect` tweens always carry `fromOpacity: current → toOpacity: 1`.
- **1:** SmoothScroll was attached to prefs fonts list + dropdown popups, never to the FontBtn
  flyout's ListBox. Attach once on first flyout open (inner ScrollViewer exists then).
- **3:** inline chip-sized rename is fundamentally cramped; replace with a modal overlay.
- **4:** SizeMinus/SizePlus are 24×30 → 30×30 (matches every other toolbar button).
- **5:** the knob slide is Fluent's own; transform-driven styling is unreliable in this build
  (documented: RenderTransform styles/transitions dead). Replace with a Motion-driven knob.

## Tasks

### T1 — ScaleSelect opacity guarantee (MainView.axaml.cs)
Both tweens in `ScaleSelect` get `fromOpacity: c.Opacity, toOpacity: 1`.

### T2 — Rename overlay (MainView.axaml + .cs)
- Wrap the root Grid in a Panel: `Panel > Grid#AppRoot (existing) + Panel#RenameOverlay`.
- Overlay = veil Border (#66000000, click = commit) + centered card (MenuBackground/Border,
  radius 12): muted title, big RoundedFieldTextBox (FontSize 20, Width ~420), hint
  "Enter to save · Esc to cancel".
- `BeginRenameOverlay(title, current, commit)`: blur `AppRoot` (BlurEffect, radius tweened
  0→16 by a small timer; reduce-motion snaps), veil FadeIn, card ScaleIn(0.92), focus +
  select-all. Enter/veil-click commits (trimmed, empty keeps old), Esc cancels; teardown
  reverses (radius →0 then Effect=null), refocus the source list.
- `BeginRenameSection` routes here (IsEditing inline path retired); pages get a "Rename"
  context item + DoubleTapped on PagesList → overlay editing `pg.Title`; commits `Vm.Save()`.

### T3 — Toolbar font list smooth scroll (FormatToolbar.axaml.cs)
FontBtn flyout `Opened` → first time, find FontList's inner ScrollViewer → SmoothScroll.Attach.

### T4 — Square size buttons (FormatToolbar.axaml)
SizeMinus/SizePlus Width 24 → 30.

### T5 — LumenToggle (Theme.axaml + new Views/ToggleFx.cs + App wiring)
- ControlTheme `{x:Type ToggleSwitch}`: 42×22 pill track (ControlHover fill, FrameBorder
  hairline; `:checked` → accent via BrushTransition) + 16px knob Border aligned left.
- `ToggleFx.Install()` (called once at app start): class handlers on
  `ToggleSwitch.IsCheckedProperty.Changed` (slide, animated) and `TemplateApplied` (snap to
  resting spot). Knob X = checked ? 20 : 0 via `Motion.Tween` (translate; honors
  reduce-motion); last X remembered on knob.Tag so mid-flight retoggles start from truth.
- Prefs' existing ToggleSwitch style (null contents, MinWidth 0, opacity transition) still fits.

## Verify
Build clean; suite green (177 — no logic changes under test). Manual (owner, later): add
section/page → chip visibly rises in; rename via pencil/right-click/double-click → centered
blurred overlay; font flyout glides; − / + square; toggles slide every time.
