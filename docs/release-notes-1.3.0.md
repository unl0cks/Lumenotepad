<!-- summary: Your font stays put, sentences start with a capital, right-click menus everywhere, sub-pages, and a choice of where the buttons live. -->
# Lumenotepad 1.3.0

## Your font stays put

Two things made the font jump back to the default:

- A font picked with nothing selected only lasted until you clicked somewhere else.
- An empty line, a new text box or a new page had no text to remember a font from.

Now:

- A line you empty keeps the font it had, even after you click away or restart.
- **Keep the last font I pick**, a new switch in Preferences → Editor, is on from the start. The font
  you pick in the toolbar becomes your writing font for new text boxes, new pages and empty lines,
  until you pick another. Text you already wrote keeps its look.

## Capitals at the start of sentences

**Capitalize the first letter of sentences**, also in Preferences → Editor and on from the start:
after a period, exclamation mark or question mark and a space, the next letter becomes a capital.

- Abbreviations such as e.g., i.e., etc. and vs., and initials like "J. Smith", are left alone.
- Changed your mind? Undo straight away puts the small letter back.

## Right-click menus everywhere

- Right-click empty space in the pages list for **New page** or **Open a PDF as a page**.
- Right-click a page for **New page**, **New sub-page**, **Make sub-page** and **Move up a level**,
  as well as the usual Rename, Customize, Export and Delete.
- Sections, the notebooks strip and the home screen offer **New section** or **New notebook** the
  same way.

## Sub-pages

- Right-click a page and choose **New sub-page**, or **Make sub-page** to tuck a page under the one
  above it. Pages can go two levels deep.
- A page with sub-pages shows a small arrow on its right. Click it to fold them away or bring them
  back.
- Rearranging pages carries each page's sub-pages along with it.
- Deleting a page never deletes its sub-pages. They move up a level instead.

## Put the buttons where you like

The All notebooks, Show notebooks, Show pages and Preferences buttons used to sit in the top-right
corner, far from the panels they open. **Button placement** in Preferences → Layout offers four
spots:

- **Top left**, the new default: the three navigation buttons sit just after the app name, above the
  panels, and Preferences stays at the top right.
- **All top left**: all four together after the app name.
- **Inside the panels**: in the notebooks strip and beside the notebook's name.
- **Bottom left**: stacked above the + in the notebooks strip.

Whenever the notebooks or pages panel is hidden, a small bubble in the bottom-left corner of the page
brings it back.

## Your PDF highlights stay put

Opening a PDF you had already added to a notebook made a brand-new copy of it every time, so its
highlights, sticky notes, text boxes and arrows seemed to vanish. Now Lumenotepad opens the copy that
is already there, with everything you added to it. It compares what is inside the files, so a
different PDF with the same name still gets its own copy. Nothing was deleted: older copies are still
in the notebook's `assets` folder with their annotations.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
