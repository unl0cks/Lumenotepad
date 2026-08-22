<!-- summary: Updates offer themselves on startup, new paper and text options for every theme, and appearance settings that rearrange themselves smoothly. -->
# Lumenotepad 1.2.9

Lumenotepad now tells you when there's a new version instead of waiting to be asked, and the
appearance settings gained the options people kept asking for.

## Updates come to you

When a newer version is available, Lumenotepad now offers it shortly after launch: a dialog in your
theme showing what changed, how large the download is, and two answers. **Update now** downloads it,
checks it against the published hash, and restarts into it. **Later** says nothing more until the
next launch.

It only asks when it can actually install the update, and it respects the existing "Check for updates
automatically" switch in Preferences, so turning that off keeps things quiet.

## Paper and text, your way

- **Light paper** now works with the Dark theme too, when Full theme is on: a light page inside a
  dark window.
- **Dark paper** is new, and does the reverse for Light, Pink and Light blue: a dark page inside a
  light window. It appears in place of Light paper on those themes, since only one applies at a time.
- **White text** is new for Light, Pink and Light blue. Text on accent-colored fills, like a selected
  page or the New notebook button, normally picks near-black on those themes because it reads better.
  Turn this on if you prefer white.
- **Disable blue tint** is new and works on all five themes. Dark parts of the interface use a plain
  dark gray instead of the blue-leaning Lumen dark, everywhere they appear.

Options that don't apply to your theme no longer sit there greyed out. They slide out of the way, and
the settings below them close the gap.

## Fixes

Dragging a notebook on the home screen left a pale rectangle behind the card on Light, Pink and Light
blue, and could snapshot the card mid-hover with clipped corners. Both are gone, and taking a
screenshot mid-drag no longer strands the floating card on screen.

On ruled and wide-ruled paper, snapping measured the line height rather than estimating it, so text
lands on the lines with any font.

Changing a theme is quicker and steadier: the page no longer rebuilds while the window is still
animating, and switching between two light themes skips re-styling work it never needed to do.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
