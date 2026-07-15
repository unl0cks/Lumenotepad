# PF4 — Visible typing boxes, gliding caret, canvas smooth scroll, swatch hover, selection contrast

Owner feedback round (2026-07-15, two screenshots: wizard Step 2 dark theme; toolbar highlight
flyout with a gray-on-hover swatch; sections strip in dark + pink themes):

1. Name fields (wizard notebook/section/page names) should LOOK like boxes — a place to type,
   not text floating on the background.
2. The typing animation (the editor's smooth gliding caret) is missing in the wizard fields.
3. The canvas doesn't smooth-scroll (everything else does).
4. Hovering a text/highlight color swatch in the canvas toolbar turns the swatch GRAY.
5. Renaming a page/section: the typing box behind the text and the selected-text box are
   both too hard to see.

## Root causes

- **1 + 5 (box):** every single-line field shares `RoundedFieldTextBox` (Theme.axaml), whose
  default Background is `Transparent` and whose template border has no BorderBrush/Thickness
  hooks at all. The `.rename`/`.inlineedit` overrides in MainView use white-alpha fills
  (`#12FFFFFF`–`#1FFFFFFF`) — invisible on light paper and barely-there on dark.
- **2 (caret):** the app's only animated caret lives inside RichTextEditor (60fps glide +
  soft-fade blink). Plain TextBoxes use the stock TextPresenter caret: hard on/off blink,
  teleporting position.
- **3:** `SmoothScroll` is attached to Home, prefs, wizard, dropdown popups — never to
  `CanvasScroll` (MainView.axaml:532). Safe to attach: tunnel handler defers to Ctrl+wheel
  (future zoom) and inner ScrollViewers; note boxes contain none; Offset.X preserved.
- **4:** swatch Buttons (FormatToolbar.BuildSwatches, prefs bullet-color button) use the
  DEFAULT Fluent Button theme — its `:pointerover` swaps `Background` for the theme's gray
  hover brush, wiping the swatch color. (Wizard/prefs color chips are Borders — unaffected.)
- **5 (selection):** `FieldSelection` token = accent @ alpha 0x55 — faint over busy fills.

## Tasks

### T1 — RoundedFieldTextBox becomes a visible box (Theme.axaml)
- Template `PART_BorderElement`: bind `BorderBrush`/`BorderThickness` (TemplateBinding),
  add BrushTransitions (Background + BorderBrush, ~0.12s).
- Theme setters: `Background #1A808080`, `BorderBrush #40808080`, `BorderThickness 1`
  (neutral gray-alpha reads on light AND dark surfaces — no per-theme tokens needed).
- `^:focus` style on the part: `BorderBrush {DynamicResource AccentBrush}`,
  `Background #22808080`.
- SizeBox keeps its compact look via its existing part style (sets its own bg + border 0).

### T2 — GlidingCaret control (Controls/RoundedTextPresenter.cs, sibling class)
- `GlidingCaret : Control` with `Presenter` (TextPresenter) + `Brush` StyledProperties,
  IsHitTestVisible=false. Mirrors TextSelectionUnderlay's hook pattern + RichTextEditor's
  AnimTick: 16ms DispatcherTimer while the ancestor TextBox is focused; display rect lerps
  toward the caret rect (k≈0.35/frame; `Motion.Enabled==false` → snap, hard on/off blink);
  soft-fade blink (hold → fade to dim → hold → fade back); blink resets on caret moves.
- Caret rect = `Presenter.TextLayout.HitTestTextPosition(CaretIndex)` + TranslatePoint
  offset; empty-layout fallback height from FontSize.
- Template wiring: presenter's `CaretBrush` hard-set `Transparent` (stock caret off);
  `<controls:GlidingCaret Presenter="{Binding #PART_TextPresenter}"
  Brush="{TemplateBinding CaretBrush}"/>` drawn above the presenter.
- Covers wizard fields AND page/section/notebook renames + SizeBox in one move.

### T3 — Rename/inline field visibility (MainView.axaml styles)
- `.rename`: rest stays flat (transparent bg, transparent border — thickness stays 1 so
  focus doesn't shift layout); pointerover `#1A808080`; focus `#26808080` + accent border.
- `.inlineedit` (only exists while editing): always `#26808080` + accent border, radius 6.
- Replace all white-alpha fills with the neutral gray-alphas.

### T4 — Canvas smooth scroll (MainView.axaml.cs)
- `SmoothScroll.Attach(CanvasScroll);` in the ctor wiring block.

### T5 — SwatchButton theme (Theme.axaml + call sites)
- Minimal ControlTheme: Border (bg/border/radius TemplateBindings) + centered
  ContentPresenter; `:pointerover` does NOT touch Background; `:pressed` dims (Opacity .75).
- FormatToolbar.BuildSwatches: `b.Theme = FindResource("SwatchButton")` (keeps `swatch`
  class — sizing + accent hover ring styles still apply).
- PreferencesWindow bullet-color swatch button: same theme.

### T6 — Selection contrast (ThemePalettes.cs + Theme.axaml fallback)
- `FieldSelection` alpha 0x55 → 0x78 (both the palette table and the custom-accent branch);
  Theme.axaml static fallback `#554DA6FF` → `#784DA6FF`.

## Verify
- Build clean, full suite green (177 expected — no model/logic changes, no new tests).
- Manual (owner): wizard fields show boxes + gliding caret; renames show box + accent
  border while editing; canvas glides; swatches keep their color on hover; selection pops.
