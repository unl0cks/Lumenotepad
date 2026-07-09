# Advanced Preferences — design

Date: 2026-07-09
Status: approved (owner) — pre-implementation
Milestone: M7 "Advanced Preferences"

## Goal

Turn the single-scroll `PreferencesWindow` into a **left-nav, categorized** preferences
surface, and add a large, cohesive set of customization controls — including the
owner's long-standing "advanced" requests (per-bullet colors, number-style overrides,
a fonts curation list, encodings/hashes) behind a **confirmation gate**, plus new
simple knobs (custom accent, glass tint, motion controls, editor defaults).

The guiding rule: **every control is a promise.** Nothing goes in the window that isn't
actually wired to behavior. Items that need more plumbing are phased later, not faked.

## Non-goals / deferred

- **Sections as a second sidebar** (queued feature): a real layout feature, not a pref
  toggle — its own future pass. Not in this milestone.
- **Variable acrylic blur radius**: the DWM backdrop API is on/off only (`DwmAcrylic.cs`).
  We ship a **glass tint** veil instead (honest, buildable). No fake blur slider.
- **Spell check / dictionaries**: out of scope.
- **True PDF text editing, ink, audio**: already deferred at the product level.
- **Corner roundness** is included only as an **optional final phase** (Phase 8): radii are
  hardcoded literals across `Theme.axaml` + inline XAML, so a global knob needs a
  tokenization refactor first. It can be cut without affecting anything else.

## Window architecture

Replace the current single `StackPanel` body with a **two-pane layout**:

- **Left rail** (`~158px`): a vertical category list. Simple categories always shown;
  an `ADVANCED` group below a divider holds the gated categories.
- **Right panel**: swaps content per selected category. Each category is its own
  `UserControl`-style section (kept as separate `Border` panels toggled by
  `IsVisible`, or a `Carousel`/`ContentControl` with the selected view — implementation
  detail; a simple selected-index → show-one-panel is fine and animates via `Motion`).

Categories:

| Category            | Gated | Contents (this milestone)                                            |
|---------------------|-------|----------------------------------------------------------------------|
| Appearance          | no    | theme, full theme, light paper, flat covers, glossy accents, **accent color**, **glass tint**, **motion** |
| Editor              | no    | default font + size, line/paragraph spacing, indent width, caret blink |
| Layout              | no    | toolbar position/scope, rail/pages default-visible                    |
| Canvas              | no    | resizable pages, deleted history                                      |
| Fonts               | **yes** | per-font enable/disable curation list                              |
| Bullets & numbers   | **yes** | per-bullet color pickers, number-style default (+ per-list override in editor) |
| Data & tools        | **yes** | export note (encoding), hash tools, open data folder, reset settings |

Window keeps: chromeless mini title bar, `Motion.ScaleIn` on open, `CollapseOut` on close,
drag-move, Escape-to-close (all already present). Width grows (`~640`), height sizes to the
tallest panel (or a fixed comfortable height with the right panel scrolling if a category
overflows — Fonts list scrolls internally).

## The Advanced gate

- New setting `AppSettings.AdvancedUnlocked` (default `false`).
- While locked, the three Advanced categories render but selecting one (or a single
  "Unlock advanced settings" row) shows a `ConfirmDialog`:
  > "Advanced settings change how notes are stored, exported, and rendered. They're meant
  > for power users — the defaults are right for most people. Unlock them?"
  with **Unlock** / **Cancel**.
- On confirm: `AdvancedUnlocked = true` (persisted), categories become selectable.
- A small "Lock advanced settings again" affordance in the last Advanced category flips it
  back to `false`.
- The gate is a soft guard (no password) — matches the specced "explaining confirm dialog."

## Settings model additions (`AppSettings`)

All new fields persist through the existing JSON round-trip and follow the current
`MainViewModel` observable-property + `OnXChanged → _settings.Save` pattern.

```
bool   AdvancedUnlocked          = false
string? CustomAccent             = null        // hex; null = theme's own accent
double GlassTint                 = 0.0         // -1..1: <0 darken glass, >0 lighten
bool   ReduceMotion              = false
string MotionSpeed               = "Normal"    // "Calm" | "Normal" | "Snappy"
string? EditorFont               = null        // default family for new notes; null = app default
double EditorFontSize            = 15          // default point size
double LineSpacingScale          = 1.0         // multiplies editor line height
double ParagraphSpacingScale     = 1.0         // multiplies inter-paragraph gap
double IndentScale               = 1.0         // multiplies bullet indent width
bool   CaretBlink                = true        // false = steady caret (still glides)
Dictionary<string,string> BulletColors = {}    // style key -> hex override; missing = built-in default
bool?  NumBoldDefault, NumItalicDefault, NumUnderlineDefault, NumStrikeDefault = null  // global number-style fallback
List<string> DisabledFonts       = []          // family names hidden from the toolbar menu
string DefaultExportEncoding     = "UTF-8"     // "UTF-8" | "UTF-8 BOM" | "UTF-16 LE" | "ANSI"
double CornerScale               = 1.0         // Phase 8 only; 1.0 = current radii
```

`ExtendedFonts` stays but is **subsumed** by the Fonts curation UI (curation list has an
"show all installed fonts" master switch that maps to `ExtendedFonts`; `DisabledFonts`
filters the resulting list).

## Category details

### Appearance (new controls)

- **Accent color.** A swatch row (the 6 `NotebookColors` families) + a "Custom…" entry
  (hex input / color picker). Selecting one sets `CustomAccent`; clearing returns to the
  theme's own accent. Wiring: `ThemePalettes` gains a pure helper
  `WithAccent(ThemeTokens t, string seed)` that recomputes every accent-derived field
  (`Accent/AccentHover/AccentSoft/AccentDeep/AccentGradTop/AccentGradBottom/FieldSelection/
  NoteChromeFocus`) from the seed via the existing `Shade`/`Alpha` math. `ThemeManager.Apply`
  applies the override after `Resolve` when `CustomAccent` is set. Fully testable (pure).
- **Glass tint.** A slider (-1..1). A new token brush `GlassTintBrush` = white/black at an
  alpha derived from the magnitude; painted as a full-window overlay `Border`
  (`IsHitTestVisible=False`) above the acrylic in `MainView`, only when the theme
  `GlassWindow` is true. Zero = no overlay.
- **Motion.**
  - `Reduce motion` toggle → `Motion.Enabled`. When false, `Motion.Tween`/`Reveal`
    short-circuit to the final frame (snap to target, fire `onDone`) so nothing breaks.
  - `Calm / Normal / Snappy` segmented → `Motion.SpeedScale` (1.4 / 1.0 / 0.6). All token
    durations multiply by it (tokens become `static` computed from the scale, or `Steps`
    multiplies ms). Owner-tuned `Fast/Base/Slow` ratios preserved.

### Editor

- **Default font + size** for *new* notes — read by the editor/toolbar when creating a note
  box (`EditorFont`, `EditorFontSize`). Existing runs keep their own formatting.
- **Line spacing / paragraph spacing / indent** — scale factors applied in
  `RichTextEditor` layout (`ParagraphSpacing`, line height, `IndentOf`). Live re-layout.
- **Caret blink** — `CaretBlink=false` keeps `_caretOpacity=1` (still glides; just no fade).

### Layout

- Toolbar position/scope (unchanged, moved here).
- **Rail / Pages default visible** — new toggles persisting the launch state of the two
  side panels (`IsRailVisible`/`IsPagesVisible` initial values).

### Canvas

- Resizable pages, deleted history (unchanged, moved here).

### Fonts (gated)

- A scrollable checklist of offered fonts (bundled always-on and locked; curated + installed
  toggleable). Unchecking adds to `DisabledFonts`. A master "Show all installed fonts"
  switch = `ExtendedFonts`.
- `AppFonts.ListNames(extended)` gains a `disabled` filter param (or reads `DisabledFonts`);
  bundled faces are never hidden. Null-guarded (virtualization recycles null datum — known
  gotcha). Toolbar font menu rebuilds on change.

### Bullets & numbers (gated)

- **Per-bullet colors.** A row per style (dot, arrow, star, heart, flower, spark) with the
  glyph, a color swatch/picker, and a "reset to default" affordance. Writes
  `BulletColors[style]`. Wiring: `RichTextEditor.BulletGlyphs` color lookup changes from the
  hardcoded dict to a static `BulletColorFor(style)` that returns `BulletColors` override ?? built-in.
  A static `RichTextEditor.BulletColors` dict is populated from settings at startup and on
  change; changing it bumps a static version + invalidates open editors (same idea as the
  theme-change canvas rebuild). Defaults unchanged: dot/arrow `#4DA6FF`, star `#E9B865`,
  heart `#E27BA6`, flower `#7FD1A6`, spark `#FFD966`.
- **Number-style default.** Four toggles (bold/italic/underline/strike) with tri-state
  feel: unset = "inherit the line's text" (today's behavior), set = force. Wiring: the
  render fallback in `DrawBullet` for `"num"` becomes
  `p.NumBold ?? settings.NumBoldDefault ?? fr?.Bold ?? false` (per-paragraph override wins,
  then global default, then text inheritance).
- **Per-list override (in editor, the "both").** When the caret is on a `"num"` paragraph,
  the toolbar bullet flyout shows a small B/I/U/S number-style control that sets the
  paragraph's `NumBold/NumItalic/NumUnderline/NumStrike` for that list (walks the contiguous
  `"num"` run). Uses the existing per-paragraph model + undo snapshot.

### Data & tools (gated)

- **Export note.** Export the current page to `.txt` or `.md` with `DefaultExportEncoding`.
  Walks the page's `CanvasDocument` note boxes → each box's `RichDocument.GetText()` →
  joined text (`.md` adds bullet/heading markers where sensible). Writes with the chosen
  `System.Text.Encoding`. Gives the encoding control a real home.
- **Hash.** Compute MD5 / SHA-256 of the current page's exported text; show + copy.
  Self-contained (`System.Security.Cryptography`).
- **Open data folder.** Reveal `AppSettings.DefaultDir` in Explorer; show workspace size.
- **Reset settings to defaults.** Confirm → overwrite settings with `new AppSettings()`,
  re-apply theme/motion/etc. (Does not touch notebooks/notes.)

## Testing

- `ThemePalettes.WithAccent` — pure unit tests (accent derivatives recomputed correctly,
  round-trips, invalid hex guarded).
- `AppSettings` round-trip with all new fields (serialize/deserialize defaults + set values).
- `AppFonts.ListNames` honoring `DisabledFonts` (bundled never hidden; disabled removed;
  null-guarded).
- Number-style fallback resolution (`per-paragraph ?? default ?? text`).
- Export text assembly + encoding byte output (UTF-8 vs UTF-16 BOM) — pure on a built
  `CanvasDocument`.
- Hash correctness against known vectors.
- Motion reduce/speed: `Motion.Enabled=false` fires `onDone` and lands on target;
  `SpeedScale` changes `Steps`.
- Visual/compositor behavior (left-nav swap animation, pickers, live editor bullet recolor)
  verified in the **real app** — headless can't show pointer/selection/compositor state
  (known harness limitation).

## Build order (phases)

1. **Restructure** — left-nav shell, move existing settings into categories, no behavior
   change. Panel-swap animates via `Motion`.
2. **Advanced gate** — `AdvancedUnlocked` + confirm dialog + lock/unlock.
3. **Appearance additions** — custom accent (`WithAccent`), glass tint, motion controls.
4. **Bullets & numbers** — per-bullet colors (editor injection), number-style default +
   per-list editor override.
5. **Fonts** — curation list + `DisabledFonts` filtering.
6. **Editor** — default font/size, spacing/indent scales, caret blink.
7. **Data & tools** — export/encoding, hash, open folder, reset.
8. **Corner roundness (optional)** — tokenize radii → `CornerScale`. Cut if not worth it.

Each phase builds, tests green, commits, and is relaunched for the owner to eyeball before
the next (the established rhythm).
