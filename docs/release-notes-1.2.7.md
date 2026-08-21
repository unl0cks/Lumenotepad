<!-- summary: Fixes the macOS launch crash: a resize handler ran before the window contents existed. -->
# Lumenotepad 1.2.7

The Terminal run and the 1.2.6 log both named the same line, and this build fixes it. This is the
crash that has stopped every 1.2.x build from opening on macOS.

## What was happening

The window watches its own size so that zooming and tiling re-layout the content. On macOS, the very
first size assignment happens while the window object is still being constructed, before its contents
exist. The size watcher ran at that moment, reached for contents that were not there yet, and the app
died on the spot.

Two reasons it went unseen for five releases:

- On Windows that watcher steps aside entirely, so no Windows run or test could ever hit it.
- No Mac executed it either: 1.2.0 through 1.2.3 were blocked by Gatekeeper before reaching this
  code, and 1.2.4 and 1.2.5 crashed exactly here. The bug shipped in 1.2.0 and this is the first
  build without it.

## The fix

The window ignores property changes until its contents exist. Nothing is lost by waiting: everything
those early notifications would have set up is re-applied the moment the window opens, which was
already how the first working layout came about.

## If it opens and something looks wrong

This is the first time 1.2.x actually runs on a Mac, so the window chrome work from 1.2.0 gets its
first real test too. If the glass, corners, or fullscreen behave oddly, that is a separate matter
from this crash. Send a screenshot.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
