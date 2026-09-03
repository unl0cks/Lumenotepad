<!-- summary: Lumenotepad no longer quits on an unexpected error, keeps a crash log, and lets you copy it from the About tab. -->
# Lumenotepad 1.2.12

A small, protective release.

## It stays open

If something unexpected goes wrong while you are working, Lumenotepad used to quit on the spot,
taking the moment with it. It now catches the problem, writes down what happened, and keeps
running. Whatever you had open stays open, and autosave keeps working.

## Crashes leave a trace

Details are written to a `crash.log` in the app folder, and that file is never wiped, so it is
still there even if the app was restarted before anyone looked.

## Easier to send along

Preferences, About tab, new Troubleshooting section:

- **Open app folder** opens the folder where your notes, settings and logs live.
- **Copy crash log** puts the crash details and build information on the clipboard together,
  ready to paste into a message. If nothing has ever gone wrong, it says so.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
