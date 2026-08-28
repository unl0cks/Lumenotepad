<!-- summary: Rewriting text keeps the font and formatting you chose, instead of falling back to the default. -->
# Lumenotepad 1.2.11

## Rewriting a line keeps your formatting

Pick a font, write something, change your mind, delete it, and type it again: it used to come back
in the default font, losing the font, size, bold, italic, colour and highlight you had chosen. The
same thing happened inside bullet points.

The reason was simple once found. Formatting lives on the text itself, so once the text was gone
there was nothing left to copy it from, and typing started over from the defaults.

Deleting now remembers the formatting it removed, so what you type next continues in the same style.
This covers every way of removing text: backspace, forward delete, selecting and deleting, selecting
and typing straight over it, and cut.

Two details worth knowing, because they are what you would expect but are easy to get wrong:

- If you pick a different font or press bold **after** deleting, your new choice wins.
- If you delete backwards out of styled text and into plain text, typing continues plain. The
  formatting that comes back is always the formatting at the spot where you stopped.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
