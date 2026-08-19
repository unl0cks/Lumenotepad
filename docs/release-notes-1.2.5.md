<!-- summary: Signs every library during install, and records a startup log so a crash can be traced. -->
# Lumenotepad 1.2.5

1.2.4 got past the "damaged" block and then quit on launch. This addresses the most likely cause and,
if it is not the cause, makes the next attempt diagnosable instead of guesswork.

## Signing during install

The 1.2.4 installer used `codesign --force --deep` on the app. `--deep` only descends into nested
bundles, and the ~220 native libraries here sit loose in `Contents/MacOS`, so they are not nested
bundles and can be skipped. Apple Silicon kills a process the moment it loads a library whose
signature does not check out, which looks exactly like this: the app starts, then quits.

The installer now signs every library individually before signing the app, which is what the older
working builds did.

## A startup log

The app writes each stage of startup to a file as it happens:

    ~/Library/Application Support/Lumenotepad/startup.log

A healthy run ends with `ready`. If it quits again, the last line in that file says exactly how far it
got, which turns "it crashes" into a specific place to look. Send the file over.

## Also worth sending if it still quits

macOS keeps its own crash report:

    ~/Library/Logs/DiagnosticReports/

Look for the newest file starting with `Lumenotepad` or `Avalonia`. The first 30 lines are the useful
part.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
