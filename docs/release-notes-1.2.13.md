<!-- summary: Fixes the freeze that happened while selecting or highlighting text in a narrow, wrapped note. -->
# Lumenotepad 1.2.13

## The freeze while selecting text is fixed

Selecting text, or having highlighted text on screen, could stop the window updating: the note
stayed on screen but nothing responded, and closing and reopening was the only way out. It showed
up most in narrow notes, such as the cue column of a Cornell page, where lines wrap often.

The cause was in the text engine Lumenotepad uses to lay out and draw text. When a line wrapped at
a point that fell inside a piece of text that cannot be broken apart, that piece was left out of
both the line before the break and the line after it. The line then claimed to hold more characters
than it actually held, so when the editor asked where to draw the selection, the answer could not be
worked out and drawing stopped for good.

This release moves to the text engine version where that is corrected. Wrapping is otherwise
unchanged: across 400 varied test paragraphs, in fonts, sizes and widths from all over the app, the
line breaks come out the same, apart from the one case where the old version was making the faulty
line in the first place.

The editor also no longer lets a drawing fault of this kind stop the window from updating. If one
ever happens again, that frame simply draws without the selection tint and the app carries on, with
the details written to the crash log so they are not lost.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
