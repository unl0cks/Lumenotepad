# Lumenotepad — M3.5 Freeform Canvas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Checkbox steps.

**Goal:** The OneNote soul — a page is a freeform canvas of movable, width-resizable note containers,
each holding its own rich document. Click empty space → a new container appears there with the caret
ready; a container that loses focus while still empty evaporates (OneNote behavior).

**Model (pure, unit-tested):** `Editor/CanvasModel.cs` — `NoteBox{X,Y,Width,Doc}` (height always follows
content) + `CanvasDocument{Boxes, Changed}`. `Changed` fires on add/remove, geometry commit (drag end,
not per pointer-move), and any edit inside any box's doc — the existing VM dirty-tracking/autosave
pipeline hooks it unchanged.

**Persistence:** page files move to v2 `{"v":2,"boxes":[{"x","y","w","paras":[…]}]}` (`Editor/CanvasJson.cs`,
reusing RichDocJson's run/para DTOs). **Migration:** a v1 file (`{"v":1,"paras":…}`) loads as ONE wide box
(680px) at the origin; empty v1 docs → zero boxes. Corrupt input → empty canvas, never throws.

**View:** `Editor/NoteCanvas.cs` — a Panel measured to the boxes' bounding rect + breathing room
(+220/+320 px), arranged from box geometry; transparent background so bare-canvas clicks register
(gotcha #4). Each box = `NoteBoxView`: hover/focus chrome border, top drag-grip (SizeAll cursor,
right-click → Delete container), right-edge width-resize strip (SizeWE, min 140px). Canvas exposes
`ActiveEditor` (last focused container's editor) — MainView re-targets the FormatToolbar from it.

**Known gaps (deliberate):** no undo for container move/resize; no container z-order UI; no
type-to-create ghost caret (click creates the container immediately instead).

## Tasks
1. **CanvasModel + CanvasJson (TDD):** change-event bubbling incl. unhook-on-remove, IsEmpty semantics,
   v2 round-trip, v1 migration (content → 1 box, empty → 0 boxes), corrupt input. Commit.
2. **NoteCanvas/NoteBoxView + rewire:** store/VM types → CanvasDocument; MainView hosts NoteCanvas in a
   both-axes ScrollViewer; toolbar follows ActiveEditor. Build + tests + launch. **User verifies** the feel
   (create/move/resize/evaporate/format across multiple containers). Commit.
