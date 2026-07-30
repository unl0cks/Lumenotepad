<!-- summary: Preferences gets grouped tabs, rounded cards and softer buttons. Small visual release. -->
# Lumenotepad 1.2.1

A visual pass over Preferences, plus the first real test of in-app updating — if you are on 1.2.0,
this one should install itself from **Preferences → About → Check for updates**.

## Preferences looks like the rest of the app now

- **Grouped tabs.** The category list is organised under headings — General, Workspace, Writing,
  Advanced, System — instead of one long list with a single divider.
- **Icons on every tab**, picking up the accent colour on the selected one.
- **Settings sit in cards.** Each section is a rounded panel rather than a run of loose rows, so
  related settings read as belonging together.
- **A title on every page**, so it is obvious where you are.
- **Rounder buttons** throughout the app. Plain buttons were inheriting a near-square default that
  looked hard next to everything else.

## Fixes

- **"Check for updates" was permanently greyed out.** It was disabled whenever the app could not
  install an update over itself — but *checking* is harmless, so it now always works. If a version is
  found that this particular copy cannot install itself, it says so and points at the download.
- **The Windows download link in the update manifest was broken.** The Windows build is published
  under its own tag, and the manifest assumed everything shared one — so a Windows update would have
  been found and then failed to download. Each platform now carries its own.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
