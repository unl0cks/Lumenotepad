# M9 Part 5 — Mindmap page style (drag bubbles together to link)

Owner spec: "a 'Mindmap' page style that allows you to drag different bubbles into each other
to create a mindmap" (the Mapping Method folded in). Bubbles = the existing note containers;
LINKS are the new concept.

## Design

- **Links live on CanvasDocument**: `List<(NoteBox A, NoteBox B)> Links` — object references
  (boxes have no ids), undirected, deduped. `ToggleLink(a,b)` links/unlinks and fires Changed
  (autosave). Removing/trashing a box drops its links; a restore comes back unlinked.
- **Persistence**: v2 page json gains `"links":[[i,j],…]` — box INDEX pairs resolved at load;
  omitted when empty, invalid pairs ignored (never throws).
- **Rendering**: new `LinkLayer : Control` between the guide layer and the boxes — draws a
  rounded 2px accent line between linked boxes' visual centers. Resolver = canvas's
  NoteBoxView bounds; ArrangeOverride invalidates it so lines FOLLOW a dragged bubble live.
  Links render in every style (they only get CREATED in Mindmap) — switching style never
  silently hides structure.
- **Linking gesture**: releasing a MOVE drag while the page's effective style is Mindmap and
  the bubble overlaps another toggles the link (drop again to unlink). A heavy overlap
  (>35% of the dragged bubble) nudges the bubble 24px past the target's nearest edge so
  both stay visible with the connector showing.
- **Starter**: one centered "Central idea" bubble (220 wide) — stamped like every style.
- **Chip preview**: GuideLayer gets a `PreviewMotif` flag (set by the wizard + customize
  dialog chip factories): Mindmap draws a mini three-bubble motif; the REAL page stays clean
  (bubbles + links only).

## Tasks
1. PageStyles.Styles += Mindmap (TDD: catalogs).
2. CanvasDocument.Links + ToggleLink + drop-on-remove/trash (TDD).
3. CanvasDocJson links round-trip (TDD, corrupt-safe).
4. LinkLayer + NoteCanvas wiring (layer order, resolver, OnBoxDragEnd + nudge, style field).
5. NoteBoxView move-release → OnBoxDragEnd (before CommitGeometry so the nudge persists).
6. PageStyleTemplate Mindmap starter (TDD).
7. GuideLayer motif + chip factories set PreviewMotif.

## Verify
Build clean, new tests + suite green, startup probe clean.
