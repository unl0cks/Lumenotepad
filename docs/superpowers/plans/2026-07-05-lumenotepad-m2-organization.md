# Lumenotepad — M2 Organization & Portable Storage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Notebooks → sections → pages, persisted in a portable human-readable folder layout, driven by a 3-pane UI (notebooks rail · sections+pages nav · canvas placeholder) with the two independent collapse toggles.

**Architecture:** POCO-ish domain models (`Workspace` → `Notebook` → `Section` → `Page`) that are `ObservableObject`s (so they bind directly and still serialize with System.Text.Json). A `WorkspaceStore` service loads/saves them under `userdata/notebooks/` — one folder per notebook, structure in `notebook.json`, order in `order.json`. `MainViewModel` (MVVM) exposes the tree, selection, editing commands, and the two panel-visibility flags. Page *content* is deferred to M3; a page is just `{ Id, Title }` now.

**Tech Stack:** Avalonia 12 / .NET 10, CommunityToolkit.Mvvm, System.Text.Json, xUnit.

---

## Storage layout (portable, human-readable)

```
userdata/notebooks/
  order.json                         ["biology", "work", ...]  (notebook folder order)
  biology/
    notebook.json                    { Id, Folder, Name, Color, Sections:[ { Id, Name, Pages:[ {Id,Title} ] } ] }
    pages/                            (M3: <pageId>.page.json content files)
    assets/                          (M3: images/attachments)
  work/
    notebook.json
```

- `Notebook.Folder` is a slug of the name (deduped), set once at creation and stable across renames (display name lives in `notebook.json`). Sections + pages are nested in `notebook.json` for M2 (no content yet).
- Delete is explicit (`WorkspaceStore.DeleteNotebook`) — `Save` never destructively reconciles.

## File structure (M2)

```
src/Lumenotepad/
  Models/Workspace.cs           Workspace, Notebook, Section, Page (ObservableObject)
  Services/Slug.cs              Slugify + uniqueness helper
  Services/WorkspaceStore.cs    Load / Save / DeleteNotebook; first-run seed
  ViewModels/MainViewModel.cs   tree, selection, commands, panel toggles
  Views/MainView.axaml(.cs)     3-pane layout + collapse toggle buttons (replaces the M1 placeholder)
tests/Lumenotepad.Tests/
  SlugTests.cs
  WorkspaceStoreTests.cs
```

---

## Task 1: Domain models
**Files:** Create `src/Lumenotepad/Models/Workspace.cs`
- Plain `ObservableObject` classes; `Id`/`Folder` are plain auto-props (set once), `Name`/`Color`/`Title` are `[ObservableProperty]` (editable/renamable), children are `ObservableCollection<>`.
- [ ] Write the models. [ ] `dotnet build`. [ ] Commit.

## Task 2: Slug helper (TDD)
**Files:** Create `src/Lumenotepad/Services/Slug.cs`, `tests/Lumenotepad.Tests/SlugTests.cs`
- `Slug.Make("My Notebook!")` → `"my-notebook"`; `Slug.Unique(name, existing)` appends `-2`, `-3`… on collision; empty/symbol-only name → `"notebook"`.
- [ ] Failing tests. [ ] Run (fail). [ ] Implement. [ ] Run (pass). [ ] Commit.

## Task 3: WorkspaceStore (TDD)
**Files:** Create `src/Lumenotepad/Services/WorkspaceStore.cs`, `tests/Lumenotepad.Tests/WorkspaceStoreTests.cs`
- `Load()` → reads `order.json` + each `<folder>/notebook.json`; missing root → empty workspace; `Load` on empty returns a **seeded** default (one notebook "My Notebook" → section "Notes" → page "Welcome") via `LoadOrSeed()`.
- `Save(ws)` → writes `order.json` + each notebook's `notebook.json` (assigns `Folder` if unset).
- `DeleteNotebook(nb)` → removes its folder.
- Tests: round-trip a 2-notebook workspace (names, colors, nested sections/pages, order); missing-file → seed; delete removes folder + persists.
- [ ] Failing tests. [ ] Run (fail). [ ] Implement. [ ] Run (pass). [ ] Commit.

## Task 4: MainViewModel
**Files:** Create `src/Lumenotepad/ViewModels/MainViewModel.cs`
- `ObservableCollection<Notebook> Notebooks`; `SelectedNotebook/SelectedSection/SelectedPage`; `IsRailVisible`/`IsPagesVisible`.
- Commands: `AddNotebook`, `AddSection`, `AddPage`, `DeleteNotebook`, `DeleteSection`, `DeletePage`, `ToggleRail`, `TogglePages`.
- Selecting a notebook picks its first section; selecting a section picks its first page. Any mutation calls `Save()` (auto-save).
- [ ] Implement. [ ] `dotnet build`. [ ] Commit.

## Task 5: 3-pane UI + collapse toggles
**Files:** Modify `src/Lumenotepad/Views/MainView.axaml(.cs)`
- Title bar: add two toggle buttons (subjects rail, pages panel) left of the caption buttons, bound to `ToggleRail`/`TogglePages`.
- Body columns: notebooks rail (chips: color + initials, `ListBox` SelectedItem→SelectedNotebook, `+` add) · nav (notebook name, section tabs, pages `ListBox`, `+ page` / `+ section`) · canvas placeholder (shows `SelectedPage.Title` + "canvas — M3"). Rail/nav `IsVisible` bound to the toggle flags.
- Wire `DataContext = new MainViewModel(...)` in `App`/`MainWindow`.
- [ ] Implement. [ ] `dotnet build` + launch-check. [ ] **User visual verify** (create/select/rename/delete notebooks, sections, pages; toggles hide/show panels; restart persists). [ ] Commit.

## Self-review
- Covers spec §3 (notebook→section→page) and §4 (portable folder-per-notebook, auto-save) and the M2 slice of §6 (collapse toggles). Toolbar-docking + full canvas are later milestones.
- Models double as bind targets + serialization DTOs (ObservableObject + STJ) — verify STJ round-trips the source-generated properties in Task 3 tests.
