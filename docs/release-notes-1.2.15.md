<!-- summary: Opening a PDF again brings back your highlights and notes instead of a blank copy. -->
# Lumenotepad 1.2.15

## Your PDF highlights stay put

Opening a PDF you had already added to a notebook made a brand-new copy of it every time, as
"Report (2).pdf", "Report (3).pdf" and so on. Highlights, sticky notes, text boxes and arrows belong
to the copy you drew them on, so opening the PDF again showed a clean page and it looked like
everything was gone.

Now, when you pick a PDF that is already in the notebook, Lumenotepad opens the copy that's there,
with everything you added to it.

- This works for Insert → PDF, Open PDF as a page and Open PDF as a section.
- A different file that happens to have the same name still gets its own copy. Lumenotepad compares
  what's inside the files, not their names.
- If earlier versions already left several copies of the same PDF, it opens the one with the most
  highlights and notes on it.

Nothing was deleted: your older copies are still in the notebook's `assets` folder with their
annotations.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
