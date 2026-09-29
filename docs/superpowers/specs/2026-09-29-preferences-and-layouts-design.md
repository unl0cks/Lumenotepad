# Preferences cleanup and layout choices: design

Date: 2026-09-29
Status: approved (owner), pre-implementation
Target release: 1.4.0 (both parts ship together)

## Part 1: Preferences, reorganized the way Lumen's were

### Problems today

- General (5 groups) and Editor (6 groups) are long scrolls.
- Appearance's Theme and Accent groups are not in cards: their headings are wrapped in a row with a help
  button, so `IsSectionHeader` does not recognize them. A help button sits alone next to the custom
  accent box because that row has no label.
- Misplaced settings: Autosave is in General, not with data; the home screen settings are split between
  General (Homepage) and Appearance (Gallery); "Keep the last font I pick" is two groups away from
  "Note font".
- Near-empty pages: Layout (7 settings) and Bullets & numbers (4) each have a page of their own.

### New structure: 10 pages become 8

| Nav group | Page | Tabs and the cards in each |
|---|---|---|
| GENERAL | General | Startup: STARTUP, SYSTEM TRAY · Home screen: HOMEPAGE, GALLERY · Deleting: ASK BEFORE DELETING |
| | Appearance | Theme: THEME, ACCENT · Glass & shape: GLASS, SHAPE · Motion: MOTION |
| | Layout | Panels: LAYOUT, PANELS · Toolbar: TOOLBAR · Pages: PAGES · Paper: PAPER |
| WRITING | Writing | Typing: CARET, TYPING · Text: TEXT DEFAULTS · Spacing: SPACING · Lists: SMART LISTS, BULLET COLORS, NUMBERED LISTS · Colors: QUICK HIGHLIGHT, TOOLBAR PALETTES |
| | Shortcuts | no tabs |
| ADVANCED | Fonts | no tabs |
| | Data & tools | Saving & backup: SAVING, STORAGE, AUTOMATIC BACKUP · Import & export: IMPORT & EXPORT · Maintenance: MAINTENANCE |
| SYSTEM | About | no tabs |

- Canvas merges into Layout; Bullets & numbers merges into Writing (which replaces Editor). Bullet
  settings are no longer behind the Advanced unlock, which only ever guarded storage, export and fonts.
- The Editor's WRITING card splits: date format and auto-capitals go to TYPING, the quick highlight
  color to QUICK HIGHLIGHT. "Keep the last font I pick" moves into TEXT DEFAULTS, and Smart lists into
  SMART LISTS.
- Every group is a card. `IsSectionHeader` also accepts a horizontal StackPanel whose first child is a
  section heading. The custom accent row gets the label "Custom color".

### Tabs (as in Lumen)

- A plain text strip under the page title: the current tab in the accent color with a 2px accent rule
  under it, others muted. Not pills, because pill rows elsewhere on these pages are settings.
- A tab shows only its cards. Tabs are remembered per page while the window is open.
- Switching tabs swaps only the cards below the strip. They cascade in top to bottom; the title and
  strip do not move.
- Tab membership comes from a table in code keyed by section heading, so the markup keeps its flat
  heading-and-rows form.

### Search

- Search keeps filtering in place, and now ignores tabs: it looks through every card on every page.
- Results are grouped under headings like "Writing › Text" (tabbed pages) or "Shortcuts" (untabbed).
- A test harness types each setting's label into search and checks that the setting shows, under the
  right "Page › Tab" heading.

## Part 2: Layout choices

### Setting

- `AppSettings.UiLayout`: "Classic" (default), "Sidebar", "Tabs", "Focus".
- Preferences › Layout › Panels, first card LAYOUT: four picture tiles, each a small drawing of the
  layout and its name. Picking one applies it straight away with a cross-fade of the body.
- Classic-only rows (Button placement, Show notebooks rail, Sections in their own sidebar) show only
  when Classic is picked. "Show pages panel" stays; in the new layouts it means "show the sidebar" or
  "show the pages list".

### Shared across layouts

The page canvas, PDF viewer, home screen, themes, sub-pages and thread lines, and all right-click menus
are the same controls in every layout. New navigators live in `MainView.axaml` next to the existing
panels. Their code goes in `Views/MainView.Layouts.cs`. The page row template becomes a keyed resource
used by every page list.

### Classic

Unchanged, including Button placement and the bubble.

### One sidebar ("Sidebar")

- Replaces the notebook rail, sections sidebar and pages panel with one column (width = the pages panel
  width setting).
- Top: a notebook button (color chip, name, chevron) opening the notebook menu: every notebook,
  then All notebooks and New notebook.
- Below: one list mixing section headers and page rows, built by the pure `NavRows.Build`. A section
  header shows the name and a fold chevron. Clicking it folds or unfolds the section; the fold state is
  kept per section for the session. Page rows use the shared template (thread lines included), and
  clicking one selects its section and page.
- In single mode there are no section headers; pages are listed directly.
- Bottom: New page (adds to the selected section) and New section.
- Right-click on headers and rows uses the existing section and page menus; right-click on empty space
  offers New page and New section.
- Title bar: All notebooks and a sidebar toggle after the app name; Preferences at the top right.
  When the sidebar is hidden, the bubble brings it back.

### Tabs

- Title bar: All notebooks, the notebook button and a pages-list toggle after the app name; Preferences
  at the top right.
- Above the page: a strip of section tabs with a + at the end (New section), using the existing section
  menus on right-click. Hidden in single mode.
- A pages list on the right (width = the pages panel width setting) using the shared template, with New
  page at the top. When it is hidden, the bubble (bottom right) brings it back.

### Focus

- No side panels. Title bar: All notebooks after the app name, Preferences at the top right.
- Above the page: a path of three buttons, "Notebook ⌄ › Section ⌄ › Page ⌄" (no section button in
  single mode):
  - The notebook button opens the notebook menu.
  - The section button lists sections, then New section.
  - The page button lists pages indented by level, then New page and New sub-page.
- The formatting toolbar shows only while a text box is active (`NoteCanvas.ActiveEditorChanged` with
  an editor) and fades out when none is.

### Toolbar in Sidebar, Tabs and Focus

- `FormatToolbar.SetSimple(bool)`. Simple mode shows the everyday controls: bold, italic, underline,
  bullets, highlight, text color, size, font and a More button. More toggles the rest inline:
  strikethrough, superscript, subscript, text type, alignment, insert, table, tag, customize and toolbar
  position.
- Separators show only between two visible groups.
- Classic uses the full toolbar exactly as today.

### Buttons per layout

`ApplyButtonPlacement` gains the non-Classic cases:

| Layout | Title bar after the app name | Top right | Hidden |
|---|---|---|---|
| Sidebar | All notebooks, pages toggle (tooltip "Show / hide sidebar") | Preferences | notebooks toggle |
| Tabs | All notebooks, notebook button, pages toggle (tooltip "Show / hide pages") | Preferences | notebooks toggle |
| Focus | All notebooks | Preferences | both toggles |

## Testing

- Unit: `NavRows.Build` (sections and pages in order, folded sections, single mode, empty notebook),
  settings round trip and reset for `UiLayout`, Preferences tab table (every card heading belongs to
  exactly one tab).
- Headless probes:
  - Preferences: every page and tab renders its cards and no card is empty. Searching each label finds it
    under the right heading. The Layout tiles switch the layout.
  - Layouts: for each layout, switch to it, select a page from its navigator, add a page and a sub-page,
    open its right-click menus, hide and show its panel, check the toolbar mode, and render it in the
    Lumen, Dark and Light themes for review.
  - The existing 29-check probe still passes in Classic.
