# M8 "Make It Yours" — customization & user-friendliness expansion

Date: 2026-07-11
Status: approved (owner picked the full menu; ONE exclusion below)
Depends on: M7 (prefs shell, gates, version-bump channels, Motion, statics-push pattern)

## Scope

The owner approved the entire feature menu EXCEPT per-page "export with encoding choice + hashes"
(spec-M7 phase 7's encoding/hash tools are DROPPED — they don't fit the app). Plain-UTF-8 notebook
export to Markdown and .txt/.md import stay (portability, not tooling). Everything below is native
Avalonia — the zero-web-components constraint holds.

All settings follow the established pattern (AppSettings POCO → VM observable + guard-save →
consumer applies; version-bump channels for non-scalar state; `ResetSettingsToDefaults` covers
everything). New prefs categories: **General** (ungated, FIRST in the nav), **Shortcuts** (ungated,
last before ADVANCED), **Backup** (gated, under Data & tools or its own row — Part 4 decides).

## Parts (each = its own plan doc, executed subagent-driven, owner-eyeballed between)

### Part 1a — Behavior basics (all S)
| Feature | Setting(s) | Wiring |
|---|---|---|
| Launch behavior | `LaunchTarget` "Home"\|"LastPage" (Home); `LastPageId` string? auto-tracked in `OnSelectedPageChanged` | VM ctor: when LastPage + id resolves in the tree → select notebook/section/page, `IsHomeVisible=false` |
| Autosave interval | `AutosaveMs` int (900, range 100–5000) | MainView `_autosave` debounce reads the VM value when arming |
| Confirmation prompts | `ConfirmDeleteNotebook/Section/Page/Container` bools (all true) | each delete site short-circuits the ConfirmDialog when its pref is false (container = `PageCanvas.ConfirmDelete` lambda) |
| Jump back in count | `RecentCount` int (5, 0–10) | `RefreshHome` takes N; 0 → `HasRecents=false` |
| Always on top | `AlwaysOnTop` bool (false) | `MainWindow.Topmost` on load + change |
| Start with Windows | none — the REGISTRY is the source of truth (HKCU\...\Run, value "Lumenotepad" = exe path) | `Microsoft.Win32.Registry` package; toggle reads actual state on open, writes on flip |

### Part 1b — Personal touches (all S)
| Feature | Setting(s) | Wiring |
|---|---|---|
| Caret color & width | `CaretColor` string? (null=accent), `CaretWidth` double (1.6, 1–3) | editor statics pushed like bullet prefs; Render uses width; caret brush override |
| Caret blink | `CaretBlink` bool (true) — the M7-spec phase-6 item | blink loop holds opacity 1 when off (glide stays) |
| Default highlight | `DefaultHighlight` hex ("#FFD666") | Ctrl+Shift+H + toolbar default swatch read a pushed static |
| Insert date/time | `DateFormat` preset string | Ctrl+Shift+T inserts `DateTime.Now` formatted at the caret; format picker in prefs |
| Default container size | `NewNoteWidth/NewNoteHeight` doubles (current defaults) | NoteCanvas creation reads them |
| Accent follows notebook | `AccentFollowsNotebook` bool (false) | `MainWindow.ApplyTheme`: inside a notebook, `WithAccent(nb.Color)` wins over CustomAccent; re-apply on SelectedNotebook/IsHomeVisible |
| Greeting personalization | `UserName` string (""), `ShowHomeStats` bool (true) | greeting says "Good morning, X"; stats line hideable |
| Gallery card size | `CardSize` "Small\|Medium\|Large" (Medium) | gallery card dimensions from three fixed size sets |
| Shortcut reference | none | new ungated "Shortcuts" prefs category — static list of every binding |

### Part 2 — Editor defaults & smart input (M)
- `EditorFont` string? / `EditorFontSize` double (15): defaults for NEW note text (M7-spec phase 6).
- `LineSpacingScale` / `ParagraphSpacingScale` / `IndentScale` doubles (1.0): editor layout math,
  live re-layout via canvas rebuild.
- **Smart lists** `SmartLists` bool (true): typing `1. `/`- `/`* ` at paragraph start auto-starts
  num/dot lists (typed prefix removed); Enter on an EMPTY list paragraph exits the list.
- **Custom toolbar palettes** `TextPalette`/`HighlightPalette` List<string>: the toolbar's
  color/highlight swatch rows become user-editable (prefs UI: add/remove/reset).

### Part 3 — Canvas (M)
- **Paper grid** `PageGrid` "None|Dots|Lines" + `GridSnap` bool: NoteCanvas draws the grid under
  containers; drag/resize snaps to the 20px cell when on.
- **Paper tint per notebook**: `Notebook.PaperTint` (nullable hex, PER-NOTEBOOK data not a global
  setting) via the notebook context menu; tints the paper region for that notebook's pages.

### Part 4 — Backup & portability (M)
- **Auto-backup**: `BackupFolder` string?, `BackupEveryDays` int (0=off), `BackupKeep` int (5),
  `LastBackupUtc`. On startup, if due → zip `userdata` off-thread to the folder, prune to K.
  "Back up now" button. Uses System.IO.Compression — no new deps.
- **Notebook export**: folder picker → `<notebook>/<section>/<page>.md` (UTF-8), text assembled
  from each page's CanvasDocument note boxes (shared markdown assembler, unit-tested).
- **Import**: .txt/.md file picker → new page in the current section, one note box with the text.

### Part 5 — Windows integration (M)
- **Tray**: `CloseToTray`/`MinimizeToTray` bools; Avalonia `TrayIcon` (Open/Exit menu); closing
  hides instead of exits when on. Uses a simple generated icon until the real app icon ships.
- **Global summon hotkey**: `SummonHotkey` bool; fixed Ctrl+Alt+N v1 via `RegisterHotKey` +
  `Win32Properties.AddWndProcHookCallback`.

### Part 6 — Large items, each standalone
- **Canvas zoom** (Ctrl+wheel + default-zoom pref).
- **UI corner roundness** (radius tokenization — the M7-spec phase 8).
- **Custom keybindings** (keymap layer + editor prefs UI).

### Part 7 — Preferences UX pass (owner-requested 2026-07-11, runs LAST)
The window has grown "cluttered and confusing, a little unorganized" (owner). After all feature
parts land: reorganize with fresh eyes — regroup rows into clearer sections, consistent
section/hint hierarchy, possibly sub-grouping cards or separators, panel order review, maybe
search/filter. Design proposed to the owner BEFORE implementing (mockup-first).

## Testing
Same regime as M7: settings round-trips, VM logic (launch-target resolution, recents count,
markdown assembly, backup pruning) unit-tested; pointer/visual behavior verified in the real app
by the owner between parts.
