# M9 Part 4 — Section + Page customization dialogs

Replace the TEMPORARY context-menu grid/style pickers (M9 Part 1) with real customization
dialogs, matching the wizard's look (chips with live GuideLayer previews, mode radios,
grid dropdown).

## Design

One chromeless dialog window, `CustomizeSheetWindow`, with two entry modes:

- **Page mode** (`new CustomizeSheetWindow(vm, page)`): NAME box, PAGE STYLE chips
  ("Inherit" + the 7 styles, current one ringed), APPLY AS radios (enabled only for an
  explicit style), GRID STYLE combo ("Inherit" + 4). Save = rename (blank keeps old) +
  `SetPageStyleChoice` + `SetPageGridStyle`; a NEWLY explicit style follows PickPageStyle's
  stamping flow (empty page stamps silently, page with content asks first).
- **Section mode** (`new CustomizeSheetWindow(vm, section)`): NAME box + the same pickers
  with a leading "Keep current" choice (default) — the style/grid only apply to the
  section's pages when the user actually picks something. Bulk apply goes through a new
  VM method (below). No per-page prompts: only pages with EMPTY canvases get starter
  boxes stamped.

## Tasks

### T1 — MainViewModel.ApplySectionStyle (TDD)
`ApplySectionStyle(sec, setStyle, style, mode, setGrid, grid)`: writes the overrides on
every page (mode only with an explicit style), saves once, then stamps starter boxes on
pages whose documents are empty (explicit non-Freeform styles only).

### T2 — CustomizeSheetWindow (axaml + cs)
520×620 chromeless sheet in the wizard's visual language: drag title bar, Esc/Cancel,
gray Cancel + accent Save, ScaleIn open, MenuFx.AttachDropDown on the combo, chip pops.
Chip previews are live mini GuideLayers (fresh instances, as in the wizard); "Inherit" /
"Keep current" chips show a dash placeholder instead.

### T3 — MainView rewire
- Pages context menu: "Customize page…" replaces the temporary Grid style/Page style
  submenus (Rename + Delete stay).
- Sections context menu: "Customize section…" added (Rename + Delete stay).
- After the dialog closes: `ApplyPageStyles()` + canvas document re-push so fresh stamps
  show immediately.

## Verify
Build clean; new ApplySectionStyle tests + suite green.
