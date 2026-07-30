<!-- summary: First Windows portable build, and the same fixes as the macOS release. -->
# Lumenotepad 1.2.0, Windows portable (beta)

**This is a beta.** Same codebase as the macOS release and it gets the same fixes, but the Windows
packaging itself has had far less real-world mileage. Back up anything you would mind losing, Preferences → Data & tools has a backup folder setting.

## Install

1. Download and extract the zip **somewhere writable**, your user folder, a tools folder, a USB
   stick. Not `C:\Program Files`.
2. Run `Lumenotepad.exe`.

SmartScreen will warn the first time, because this build is not code-signed: **More info → Run
anyway**.

## It is portable, not installed

Everything the app saves lives in a `userdata` folder beside `Lumenotepad.exe`. Nothing is written to
the registry and nothing is installed system-wide.

- **To update:** extract the new build and copy your old `userdata` folder into it, or extract over
  the top of the old folder and leave `userdata` where it is.
- **To uninstall:** delete the folder. That is genuinely all.
- **Keep it writable.** `C:\Program Files` blocks normal programs from writing, so your notes would
  fail to save there.

Windows has an in-app updater too: Preferences → **About** → *Check for updates*. It downloads the
new build, replaces the program files and restarts, leaving your `userdata` folder alone.

## Why portable rather than an installer

Because the Windows build keeps its data beside its own executable, an installer that put the app in
Program Files would put your notebooks somewhere Windows will not let it write. Shipping a real
installer means first moving the data directory to `%APPDATA%`, which is a migration for anyone
already running a portable copy, a deliberate change rather than a packaging detail. It is on the
roadmap.

## What is in this build

Everything in the [macOS 1.2.0 notes](https://github.com/unl0cks/lumenotepad/releases/tag/v1.2.0)
that is not macOS-specific, plus two changes that affect Windows directly:

- Containers no longer delete themselves when moved or resized.
- `Ctrl+Shift+Z` now redoes. It previously undid, which disagreed with the PDF viewer in the same app.

Windows chrome, acrylic, snapping and the tray are unchanged.
