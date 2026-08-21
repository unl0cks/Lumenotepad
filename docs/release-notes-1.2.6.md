<!-- summary: Records the exception behind a startup crash instead of only the stage it reached. -->
# Lumenotepad 1.2.6

The 1.2.5 log did its job. It ruled out signing and narrowed the crash to one place, and this build
narrows it the rest of the way.

## What 1.2.5 told us

The log from 1.2.5 stopped here:

    04:49:02.576  viewmodel
    04:49:02.576  installed fonts

and the crash report said `EXC_CRASH (SIGABRT)`, `abort() called`. That is not the kill macOS uses for
a bad signature, so the signing work in 1.2.5 landed and the app really was running its own code. It
died while building the main window.

## What this build adds

The startup log now records the exception itself, not just the stage before it: type, message, and the
full stack, written the moment it happens. Three separate handlers cover it, so a failure anywhere in
startup lands in the file.

Window construction is also split into two stages, `window built` and `window themed`, so the log
distinguishes the interface being assembled from the theme being applied to it.

Reading the result:

- The file names an exception. That is the answer, and the stack points straight at the line.
- The file stops with no exception recorded. Then the abort came from below the runtime, in native
  code, which is a different kind of fault and points at the window interop.

## One change beyond logging

The macOS window effects (glass, the native fullscreen behaviour) ran once while the window was still
being built, before it had a native window to act on. Every one of those calls checked for that and did
nothing, so the work was already pointless there, and it now waits until the window is actually open,
which is where the same calls have always run properly. That removes a suspect without changing what
you see.

## Getting the answer without waiting for this build

Running the app from Terminal prints the same failure straight to the window:

    /Applications/Lumenotepad.app/Contents/MacOS/Lumenotepad

That works on any version, including one already installed.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
