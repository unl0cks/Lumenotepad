<!-- summary: Fixes the macOS install. Dragging the app across made macOS call it damaged; use the included installer. -->
# Lumenotepad 1.2.4

**Fixes the macOS install.** 1.2.0 through 1.2.3 could not be installed on macOS at all.

## What was wrong

Dragging `Lumenotepad.app` into Applications made macOS report **"Lumenotepad is damaged and can't be
opened"**, with nothing in System Settings to override it. The app was never damaged. The signature
and every file in the bundle verify clean.

macOS handles two cases differently. An **unsigned** app that you download gets the familiar "could
not verify" warning, and System Settings gives you an **Open Anyway** button. An app signed
**ad-hoc**, as this one is, gets validated by Gatekeeper instead. Gatekeeper finds a real signature
with no trusted authority behind it and no notarization ticket, and reports that as "damaged". No
override is offered, which is why Privacy & Security showed nothing.

Earlier builds shipped an installer script that stripped the download quarantine flag before opening
the app, so Gatekeeper never assessed it. Removing that script in 1.2.0, in favour of a plain
drag-install, is what broke it.

## The fix

The zip contains **Install Lumenotepad.command** again. Double-click it. It copies the app into
Applications, clears the quarantine flag, and opens it.

macOS blocks the installer itself the first time. Click **Done**, then System Settings → Privacy &
Security → **Open Anyway**, then double-click the installer again. Scripts get the override button
that app bundles don't.

If you already dragged the app across and saw "damaged": put it back in Applications and run the
installer. It repairs a copy that's already there.

You do this once. In-app updates are unaffected, because a build the app downloads itself is never
quarantined.

## Also in this release

The selection box you drag across a page is rounded now, with a fade in and out instead of snapping.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
