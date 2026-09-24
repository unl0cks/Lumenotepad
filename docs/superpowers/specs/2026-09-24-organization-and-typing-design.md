# Organization and typing round: design

Date: 2026-09-24
Status: approved (owner), pre-implementation
Target release: 1.3.0 (also carries the unreleased 1.2.15 PDF-reopen fix)

## 1. Font keeps resetting

Two mechanisms cause it:

- A font picked in the toolbar with nothing selected is held as a pending typing format, and any caret
  move (click or arrow) drops it.
- Formatting lives on runs of text. An emptied line, a new text box or a new page has no runs, so typing
  there falls back to the default font. The 1.2.11 carry only survives until the next caret move.

### 1a. Emptied lines remember their format (fix, no option)

`Paragraph` gains `RunFormat? Mark`, the format of its paragraph mark:

- When a paragraph becomes empty through deletion, `Mark` takes the format of the text that was removed
  from it.
- Enter from an empty paragraph with a `Mark` gives the new paragraph the same `Mark`.
- Typing into an empty paragraph with a `Mark` (and no pending format) uses the `Mark`.
- When text is typed into the paragraph, the runs carry the format and `Mark` is no longer consulted
  while the paragraph has text.
- `Mark` is saved in the page file (optional field, omitted when null). Older builds ignore it.

### 1b. "Keep the last font I pick" (option, default on)

- Preferences → Editor switch, `AppSettings.KeepPickedFont` (default true).
- Picking a font in the format toolbar stores it as `AppSettings.KeptFont` (null for the default entry).
- When typing into an empty paragraph with no pending format and no `Mark`, the kept font is used.
- Only the font face is kept, not the size.
- Existing text is never restyled; the default font of notebooks is unchanged.

Typing format precedence, highest first: pending format, the run at the caret, the paragraph `Mark`
(empty paragraph), the kept font (empty paragraph), the default.

## 2. Auto-capitals

- Preferences → Editor switch, `AppSettings.AutoCapitalize` (default true).
- A lowercase letter typed right after `. `, `! ` or `? ` (one or more spaces) becomes uppercase.
- Not applied when the word before the `.` is a known abbreviation (e.g, i.e, etc, vs, cf, approx, fig,
  mr, mrs, ms, dr, st, no, vol, pp, ca) or a single letter (initials like "J. Smith"), or contains an
  inner period ("e.g.").
- Not applied at the start of a line.
- Undo straight after an auto-capital restores the lowercase letter and nothing else.

## 3. Right-click menus

Rule: empty space offers what the area's **+** offers; an item offers **New …** first, then its own
actions.

- Pages list, empty space: New page, Open PDF as page…
- Pages list, on a page: New page, New sub-page, Make sub-page, Move up a level, then Rename, Customize
  page…, Export page…, Delete page.
- Sections, empty space: New section. On a section: New section, then the existing items.
- Notebook rail, empty space: New notebook. On a notebook: New notebook, then the existing items.
- Home screen, empty space: New notebook.

## 4. Sub-pages

- `Page` gains `int Level` (0 page, 1 sub-page, 2 sub-sub-page) and `bool Collapsed`.
- Pages stay one ordered list per section. A page's sub-pages are the pages directly after it with a
  higher level.
- A page with sub-pages shows ▸/▾ to fold them; folded sub-pages are hidden from the list.
- New sub-page: a new page inserted after the parent's last descendant, at parent level + 1 (max 2).
- Make sub-page: indent by one, allowed only when the page above has level ≥ the page's level. The
  page's own descendants move with it.
- Move up a level: outdent by one, with its descendants.
- Rearranging a page moves its descendants with it.
- Deleting a page promotes its direct descendants by one level; nothing else is deleted.
- Page content files are keyed by id and do not move. Older builds ignore `Level` and show a flat list.

## 5. Button placement

- Preferences setting `AppSettings.ButtonPlacement`: `TopLeft` (default), `AllTopLeft`, `InPanels`,
  `BottomLeft`.
  - TopLeft: All notebooks, Show/hide notebooks, Show/hide pages after the app name; Preferences at the
    top-right before the window buttons.
  - AllTopLeft: all four after the app name.
  - InPanels: All notebooks at the top of the notebook rail, Preferences at its bottom; the notebook and
    page toggles in the pages panel header.
  - BottomLeft: all four stacked above the **+** in the notebook rail.
- The four buttons are created once and moved between hosts when the setting changes.
- Bubble: whenever the notebook rail or the pages panel is hidden, a small floating bubble in the
  bottom-left corner shows a button to bring back each hidden panel. Any of the four buttons whose host
  is hidden moves into the bubble, so nothing becomes unreachable.
- On the home screen these buttons stay hidden, as today.

## Testing

- Unit: paragraph mark (delete to empty, Enter, typing, JSON round trip), kept-font precedence,
  auto-capital rule table, sub-page operations (indent, outdent, new sub-page, delete promotes, move with
  descendants, visibility when folded), settings round trips and reset.
- Headless probe driving the real app: typing after emptying and clicking away keeps the font; new box
  uses the kept font; auto-capital and its undo; every right-click menu's items per area; sub-page
  creation, fold, and delete; each button placement plus the bubble, with screenshots.
