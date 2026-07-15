# M9 Part 3 — Re-open entry points + wizard edit mode

Owner spec (M9): the customization wizard must be re-openable for an EXISTING notebook — via
right-click menus and a toolbar button at the OPPOSITE end from the tools — pre-filled, with
Create acting as Save. Destructive edits (removing real sections/pages) confirm first.

## Tasks

### T1 — Draft grows edit-mode identity (NotebookDraft.cs, TDD)
- `SectionDraft.Source` (`Models.Section?`, null = new) + parallel `PageSources`
  (`List<Models.Page?>`; missing index = new page) with `SourceAt/AddPage/RemovePageAt`
  helpers that keep the lists aligned (pad-with-null so `New()`'s initializer stays legal).
- `NotebookDraft.FromNotebook(nb)` — name/color/cover-path/defaults + one SectionDraft per
  section carrying Source + per-page Sources.

### T2 — MainViewModel.ApplyNotebookCustomization(nb, draft) (TDD)
- Copies name (blank keeps old)/color/defaults.
- Rebuilds nb.Sections to draft order REUSING Source objects (ids, docs, content survive);
  sourceless drafts → new Section/Page ("Section"/"Untitled page" blank fallbacks; the
  single-section-pageless guard from CreateNotebook applies).
- Dropped real pages/sections → `ForgetPageDoc(deleteFile: true)` like the delete commands.
  `FlushDirtyDocs()` first. Collections only rewritten when actually changed (no flicker).
- Selection re-validated; `Save()`; new pages stamped AFTER save; cover last: null clears an
  existing cover, an unchanged CoverPath is untouched, a new temp path is copied in.

### T3 — Wizard edit mode (NotebookWizardWindow)
- Ctor `(MainViewModel vm, Models.Notebook? edit = null)`; edit → draft = FromNotebook,
  `_familyIx` derived from the draft color, Title/WizTitle "Customize notebook",
  CreateBtn "Save".
- Step 2 seeds controls FROM the draft (grid combo, font combo, size slider + label, mode
  radio) before handlers attach — new mode seeds identical defaults, so no behavior change.
- Removing a row whose Source exists asks first ("…permanently deleted when you press
  Save"); page rows via `RemovePageAt`, adds via `AddPage`.
- CreateAndClose: edit → ApplyNotebookCustomization, else CreateNotebook (unchanged).

### T4 — Entry points
- FormatToolbar: `CustomizeBtn` (palette glyph) docked at the DockBtn end (mirrored in
  SetPlacement), raises `CustomizeRequested`.
- MainView: `OpenNotebookWizard(Models.Notebook? edit = null)`; toolbar event → wizard for
  SelectedNotebook; "Customize notebook…" MenuItems on the home-card, rail, and
  notebook-name context menus.

## Verify
Build clean; new VM tests green (FromNotebook copies + sources; Apply renames/keeps
identity/adds/deletes with doc cleanup/selection/cover rules); suite green.
