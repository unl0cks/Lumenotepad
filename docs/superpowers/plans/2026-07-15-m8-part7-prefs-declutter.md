# M8 Part 7 — Preferences declutter (settings search)

Owner wanted this mockup-first but is away and asked for all tasks done, so this is the
LOW-REGRET declutter: a live **search box** that filters every category at once. Purely
additive — no setting is moved or removed, so it's the safest version to hand back for the
owner's own redesign pass.

## Approach

- Search box under the title bar (spanning the window, like Windows Settings).
- The panels container becomes a `StackPanel` (was an overlapping `Panel`) so multiple
  matching categories can stack as one results list. Normal single-panel mode is unchanged
  (only one panel visible → StackPanel collapses the rest).
- Each panel gets a hidden accent category heading (injected in code), shown only while
  searching so stacked results are labelled.
- `ApplySearch`: for every panel, `FilterPanel` shows only rows whose text OR "?" tooltip
  help contains the query; a SECTION header shows only when a row beneath it matched; the
  panel + its heading hide when nothing matched. A "No settings match" note covers the
  empty case. Original per-row visibility is captured on first touch and restored on clear,
  so designed-hidden/conditional rows are never wrongly revealed.
- Lazily-built panels (shortcuts/fonts/data) are primed once on first search so their rows
  are matchable.
- Search reaches EVERY category including Advanced (the nav gate still guards browsing) —
  flagged in code as a one-line flip if the owner prefers gating search too.
- Clicking a nav category clears the search (returns to that single panel).

## Verify
Build clean, 190 tests green, startup probe clean. Visual/interaction check is owner-only
(native window). This is the task most worth the owner's review — they wanted a mockup.
