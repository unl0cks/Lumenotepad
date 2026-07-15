# M8 Part 6 — Canvas zoom, corner roundness, custom keybindings

## T1 — Canvas zoom (view-only)
- `LayoutTransformControl` wraps the NoteCanvas inside CanvasScroll; Ctrl+wheel (tunnel,
  before the ScrollViewer sees it — SmoothScroll already passes Ctrl through) steps a
  ×1.1 zoom clamped 50%–200%; Ctrl+0 resets. Layout transform = scrollbars stay correct.
- The guides viewport is in CANVAS coordinates: viewport pushes divide by the zoom, and
  re-push on every zoom change. Session-only by design (a zoom is a viewing posture, not
  a preference).

## T2 — Corner roundness preference
- `AppSettings.CornerRoundness` (0.5–1.5, default 1) + VM mirror (guarded save,
  ctor-load, reset line).
- `ThemeManager.PushRoundness(app, s)` writes CornerRadius resources: `RadiusPage` 14s,
  `RadiusPageInner` 13s, `RadiusMenu` 8s (defaults seeded in Theme.axaml); exposes
  `ThemeManager.Roundness` for code-drawn corners.
- Consumers: page box + paper tint veil (MainView.axaml), context menu/flyout presenters
  (App.axaml), canvas plate hole (UpdateCanvasPlateClip × Roundness), note containers
  (`NoteCanvas.NoteRadiusPref` static — chrome/grip corners; canvas rebuild applies).
- Prefs → Appearance: "Corner roundness" slider 50–150%, plain-language "?" help.

## T3 — Custom keybindings (editor formatting set)
- New `Services.Keymap`: 8 rebindable actions (bold, italic, underline, strikethrough,
  quick highlight, date insert, bullet list, numbered list) with the current combos as
  defaults; overrides parse via KeyGesture (invalid → default, never throws);
  `Matches(action, e)`, prettified display (D8 → 8), `FromEvent` builds a canonical
  gesture from a captured key press (modifier-only presses and bare letters rejected;
  F-keys may bind bare).
- `AppSettings.KeyOverrides` dict; VM `SetKeyBinding`/`ResetKeyBindings` + startup push +
  settings-reset clear.
- RichTextEditor consults Keymap FIRST (custom combos win); the 8 hardcoded switch cases
  are removed. Structural keys (Ctrl+A/Z/Y/C/X/V, arrows…) stay fixed.
- Prefs → Shortcuts: each rebindable action gets a capture button ("Press keys…" on
  click; Esc cancels, Backspace returns it to default), a "Reset all shortcuts" button,
  and the fixed-shortcut reference list stays below.

## Verify
Keymap tests (defaults/overrides/invalid/capture-format); build + suite green; probe.
