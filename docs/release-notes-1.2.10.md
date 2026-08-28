<!-- summary: Sub-bullets with Tab, formatting that survives the next bullet, a toolbar that wraps instead of colliding, and sideways trackpad panning. -->
# Lumenotepad 1.2.10

A release shaped by someone taking real school notes. Thank you for the reports, every one of them
landed.

## Lists can nest now

Press **Tab** on a bullet or numbered line to make it a sub-point, **Shift+Tab** to bring it back,
up to four levels deep. It works on a whole selection at once. Numbered lists count each level on
its own, the way you would write them by hand: 1. at the top, then a. inside it, then i. inside
that, and the outer numbering picks up where it left off afterwards.

The keys behave like you expect from other editors: Backspace at the start of a nested line steps
it out one level before removing the bullet, and pressing Enter on an empty nested line climbs out
one level at a time. Checklists nest too. Nesting is kept when pages are exported: Markdown and
plain text indent properly, and the PDF export indents both text and checkboxes.

## Your formatting follows you to the next line

Picking a font, size, color, bold, or anything else and then pressing Enter used to throw it all
away: the next bullet came out in the default font. The next line now continues with exactly the
formatting you were writing in. This was most visible with fonts, but it quietly ate bold and
italic too when they were switched on just before a new line.

## Small screens and split screen

- The toolbar wraps onto a second row when the window is narrow, instead of its last few buttons
  overlapping each other.
- Sideways panning with a trackpad works on the page now. Horizontal swipes used to be swallowed,
  or worse, turned into vertical scrolling. Diagonal panning works too, and Shift+wheel still
  scrolls sideways with a mouse.
- The window can go down to 560 pixels wide, so a half or even third of a laptop screen fits.

A tip that was already there but easy to miss: the two icons next to the home button in the title
bar hide the notebook rail and the pages sidebar, and the sidebar's right edge can be dragged to
resize it. Handy exactly when the window is small.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
