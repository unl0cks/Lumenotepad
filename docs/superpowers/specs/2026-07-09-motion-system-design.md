# Lumenotepad Motion System — Design

**Date:** 2026-07-09
**Status:** Approved (design); implementation phased.
**Goal:** Make the whole app feel fluid and animated — every view change, panel, list mutation,
button, dialog, and window open — using ONE shared motion vocabulary so it reads as a single
coherent system rather than ad-hoc tweens. North star: "as fluid as the drag."

## Context

Today's M6.8 work animated the gallery (hover scale, selection scale, drag + reflow) and, in doing
so, mapped this Avalonia 12 build's animation primitives. The rest of the app is still instant:
home↔editor and opening a notebook, section/page switches (content pops in), rail/pages panel
show-hide, add/delete/rename of notebooks·sections·pages, dialog & Preferences window open, theme
changes. This spec makes all of that fluid.

## Spike findings (measured 2026-07-09 — these decide the mechanism)

Ran `tools/CardRepro` advancing the headless render/animation clock and reading properties mid-flight:

- **Opacity `Transitions`** → produced an intermediate value (0.974 mid-flight): **works.**
- **`Animation.RunAsync` on Opacity** (keyframe) → intermediate value: **works.**
- **`Animation.RunAsync` on `RenderTransform`** → **THROWS** `No animator registered for the property
  RenderTransform`. Transform (scale/translate) keyframe animation is unavailable; `RenderTransform`
  `Transitions` are already known-dead (M6.8 gotcha #10).
- **`TransitioningContentControl` (CrossFade)** → produced opacity changes: **works** (it is
  opacity-based).

**Conclusion:** Opacity / simple-property animation is reliable and declarative. Transform (scale,
translate) animation must be driven by the proven code-behind per-frame tween (the one that already
runs the drag flawlessly). We never animate `RenderTransform` via `Transitions`/`Animation` again.

## 1. Motion vocabulary

Central constants in a `Motion` static class (`src/Lumenotepad/Views/Motion.cs`):

- **Durations:** `Fast = 120ms` (micro-feedback: button press, toggle), `Base = 190ms` (most
  transitions: fades, reveals, item enter/exit, panel slides), `Slow = 280ms` (big view swaps:
  home↔editor, opening a notebook).
- **Easings:** ease-out cubic for enters/moves (decelerate into place), ease-in cubic for exits,
  ease-in-out for reversible toggles.
- **Offsets:** `Rise = 8px` — content and list items enter with a small upward translate + fade.

## 2. Mechanism (chosen by the spike)

- **Opacity-based motion** (fade-in/out, cross-fade, reveal) → declarative: Avalonia `Transitions`
  on `Opacity`, or `TransitioningContentControl` with `CrossFade`, or `Animation.RunAsync` on
  `Opacity`. Reliable.
- **Transform-based motion** (scale, translate, rise, slide) → the code-behind `Motion` helper,
  generalizing the drag's per-frame `DispatcherTimer` tween (build transforms with
  `TransformOperations.CreateBuilder`, never per-frame string `Parse`). Reliable.
- **Never** use `RenderTransform` `Transitions`/`Animation` (throws / silently dead).

## 3. The `Motion` helper (one small file)

Static methods on `Views/Motion.cs`, each **cancellable per element** (reuses the drag's tween
infrastructure — one running tween per element, starting a new one cancels the old):

- `FadeIn(control, dur = Base)` — opacity 0→1.
- `RiseIn(control, dur = Base)` — opacity 0→1 + translate Rise→0 (transform via tween).
- `ScaleIn(control, from = 0.96, dur = Base)` — opacity 0→1 + scale from→1.
- `FadeOut(control, dur = Base, onDone)` — opacity →0, then onDone (e.g. remove).
- `CollapseOut(control, dur = Base, onDone)` — fade + shrink for deleted list items.
- `CrossFade(host, oldChild, newChild, dur)` — where a `TransitioningContentControl` isn't the fit.

The existing drag `Tween`/`Make`/`StopTween`/`_tweens` move into or are shared with `Motion` so
there is a single tween engine. Opacity is tweened in the same per-frame loop as translate/scale.

## 4. Application catalog (one spec, phased build)

**Phase 1 — Foundation + navigation/content (highest impact)**
- `Motion` helper + tokens; move the drag tween engine into it.
- Home↔editor and opening a notebook: cross-fade (Slow) with a subtle scale/rise on the entering view.
- Section switch: the pages list + page content cross-fade / rise-in (Base) instead of popping.
- Page switch: page title + canvas content rise-in / cross-fade (Base) — blank→content no longer pops.

**Phase 2 — Panels & layout**
- Rail and pages panel show/hide: animate width collapse + fade (Base). *Caveat: width is a layout
  property (animates but re-lays-out each frame); if janky, switch to a clip/translate reveal.*
- Toolbar re-dock: fade the toolbar out/in across the dock change.

**Phase 3 — Add / delete / rename**
- New notebook/section/page: `RiseIn`.
- Deleted notebook/section/page: `CollapseOut` before removal.
- Rename fields (section tab, notebook/page title) appearing: `FadeIn`/`ScaleIn`.

**Phase 4 — Micro-interactions & chrome**
- Buttons: press feedback (quick scale-down to ~0.96 on press, back on release) via the tween.
- Dialogs (confirm, cover-crop) and the Preferences window: fade + scale-in on open, fade-out on close.
- Theme / color change: cross-fade the affected surfaces instead of an instant token swap.
- Any loading/spinner states: consistent fade-in.

## Non-goals / caveats

- No shared-element "card morphs into editor" transition in v1 (complex); a cross-fade + scale is
  enough and cohesive. Can revisit later.
- Motion respects existing prefs (e.g. Flat covers / Glossy accents) — animations are additive, not
  a new visual style.
- All durations/easings come from `Motion`; no ad-hoc values scattered in views.

## Testing / verification

- Unit-testable: `Motion` easing math and tween interpolation (pure functions).
- Structure/tokens verifiable in the headless `CardRepro` harness.
- **Visual smoothness must be verified in the REAL app** (headless clock is unreliable for timing;
  compositor behaviour differs) — build → launch → observe each phase.
