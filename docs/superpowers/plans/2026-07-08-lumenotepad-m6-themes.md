# Lumenotepad — M6 Theme Engine + Preferences Window — Implementation Plan

> **Owner correction 2026-07-08:** NO stopgap title-bar theme flyout — theme controls belong in the
> PREFERENCES WINDOW (per the original spec), so the simple prefs window ships in this milestone,
> merged from M7. The advanced-settings gate stays deferred until there is content for it.

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Checkbox steps.

**Goal:** the owner's exact theme matrix, live-switchable. `Theme` sets the FRAME (Lumen glass /
Dark solid / Light solid / Pink solid / Light-blue solid). `Full theme` OFF (default) = the page
canvas is the CONTRASTING material (glass paper under solid frames; solid paper under the Lumen
glass frame, with a dark/light paper toggle, dark default). `Full theme` ON = canvas matches the
frame. Acrylic backdrop stays enabled whenever any region is glass.

**Regions & tokens (all DynamicResource, defaults = today's dark glass):**
- FRAME (title bar, notebooks rail, nav sidebar): FrameBackground/FrameBorder, TextPrimary/Secondary/Muted,
  ControlHover/Pressed, ScrollThumb(+Hover/Pressed).
- CANVAS (body backdrop incl. homepage): CanvasBackground, CanvasText(+Muted). Glass canvas = white text
  (dark acrylic); solid = theme text.
- PAPER (the page box): PaperBackground/Border, PaperText(+Muted), FieldSelection, NoteChromeHover/Focus,
  NoteGripFill/Bar. Editors/note-container chrome read these at construction; theme change re-sets
  PageCanvas.Document to rebuild views.
- ACCENT: AccentColor + SystemAccent* (Fluent recolor), AccentBrush/Hover/Soft/Deep/Gradient — derived
  from one accent hex per theme (Lumen/Dark #4DA6FF, Light #3E8EE0, Pink #FB6F92, Light-blue #5C85E6).

**Palettes (owner-provided):** Pink #FFE5EC #FFC2D1 #FFB3C6 #FF8FAB #FB6F92; Light-blue #EDF2FB #E2EAFC
#D7E3FC #CCDBFD #C1D3FE #B6CCFE #ABC4FF.

**Architecture:**
1. `Services/ThemePalettes.cs` — PURE resolver: (theme, fullTheme, paperLight) → ThemeTokens record of
   hex strings + DarkChrome + GlassWindow flags. Fully unit-tested (the matrix lives here).
2. `Services/ThemeManager.cs` — writes tokens into Application.Resources (overrides the Theme.axaml
   defaults), sets RequestedThemeVariant, DWM immersive dark; exposes Current for ReassertChrome.
3. AppSettings: Theme/FullTheme exist; add PaperLight. VM: Theme/FullTheme/PaperLight observable +
   persisted. MainWindow applies on open + on VM property change.
4. XAML: Theme.axaml ControlThemes + MainView regional backgrounds → DynamicResource; RoundedFieldTextBox
   selection + scrollbar thumbs tokenized. PageTitle gets PaperText, homepage header CanvasText.
5. PreferencesWindow (XAML, non-modal, single instance, opened from a title-bar gear button; themed
   via a solid WindowBackground token so it follows the active theme):
   - APPEARANCE: theme picker (5), "Full theme" toggle + explainer, "Light paper" toggle (enabled
     Lumen+FullOff only), "Flat covers" toggle (default off — solid covers, shadow kept).
   - LAYOUT: toolbar position (Top/Left/Right/Bottom) + attach-to (Window/Page) — mirrors the
     toolbar's own quick menu, both stay.
   - CANVAS: "Resizable pages", "Deleted pages history" toggles (plumbing already shipped).
   - Footer note that Advanced settings arrive later behind the confirm gate.
   Flat covers implementation: cards/chips keep solid color + shadow; the gradient overlay Borders
   get Classes="cardfx" and are hidden via an ItemsControl-level "flat" class style (no per-item
   VM bindings inside templates).

**Known v1 compromises:** ConfirmDialog/CoverCropDialog/TrashPanel stay dark-glass on every theme
(readable everywhere); gallery card fronts are notebook-colored (theme-independent by design);
scrollbar thumbs follow the paper region's darkness.

## Tasks
1. ThemePalettes + tests (matrix: 5 themes × full on/off + lumen paper toggle). Commit.
2. ThemeManager + settings/VM plumbing + XAML tokenization + switcher flyout + editor/container
   rebuild on change. Build + tests + launch. **User verifies** all matrix cells. Commit.
