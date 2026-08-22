<!-- summary: Light themes reach every surface, ruled paper snaps text onto its lines, wide-ruled paper, proper macOS cursors, and a Windows setup wizard and launcher. -->
# Lumenotepad 1.2.8

The largest release since the macOS port: theme fixes across every surface, smarter paper, native
cursors on macOS, and Windows gains a real installer.

## Themes reach everything now

A handful of surfaces were painted dark no matter which theme was active: the Deleted containers and
Tagged notes panels, the confirm and rename dialogs, the cover-crop dialog, and the group cards in
Preferences. All of them now draw from the theme, so Light, Pink and Light blue look like themselves
everywhere.

Text on accent-colored fills picks its own ink now. Each accent is measured and gets near-black or
white, whichever actually reads on it: Pink gets dark ink at 4.5:1 contrast where white only managed
3.0:1, the blues keep white. Selected pages and sections, the theme picker, filled buttons and the
title-bar mark all follow, including when the accent follows a notebook color.

## Paper that behaves like paper

Snap to grid now follows the paper style. Dots and squared grid share the snap cell as before, and on
ruled paper the snap places the first line of text centered between two rule lines rather than parking
the container's edge somewhere unrelated. Heights snap in whole rule steps so the bottom edge stays in
phase.

**Wide ruled** joins the paper grids, with wider line spacing than the standard rule. Pick it in
Preferences, per notebook in the wizard, or per section and page in the customize sheet.

## macOS cursors

Moving or diagonally resizing a container on macOS showed a plus sign, because macOS has no native
cursor for either and the fallback is a crosshair. Both now get proper drawn cursors in the standard
macOS style. Windows keeps its native ones.

## Windows: a setup wizard and a launcher

Two new ways to install, alongside the portable zip:

- **Lumenotepad-Setup** installs per user with a short wizard: no administrator prompt, a Start menu
  entry, and a real uninstall in Windows' Installed apps. Uninstalling offers to keep your notes.
- **Lumenotepad-Launcher** is the same wizard at a fraction of the size. It downloads the latest
  release when you install and refuses anything that does not match the published hash.

Your notes stay in the `userdata` folder beside the app in every case, and updating from inside
Lumenotepad keeps working exactly as before.

Also fixed: the in-app updater now says plainly when it cannot replace the running copy, such as from
a folder it cannot write to, instead of blaming the download.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
