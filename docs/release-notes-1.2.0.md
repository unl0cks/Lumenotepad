# Lumenotepad 1.2.0 — macOS

**Install:** download the zip for your Mac — `arm64` for Apple Silicon, `x64` for Intel — and drag
`Lumenotepad.app` into Applications.

The first launch is blocked by macOS, because this app is signed ad-hoc rather than with a paid
Apple Developer ID. Double-click it, click **Done** on the warning, then open **System Settings →
Privacy & Security**, scroll down, and click **Open Anyway**. That is a one-time step — see below.

---

## Updates no longer cost you anything

Preferences → General → **Updates** → *Check now*.

macOS quarantines anything a browser or chat client downloads, which is what forces the System
Settings trip — and that toll is per download, so it used to be payable on every single release.
The quarantine flag is applied by the *downloading* application, though, not by the network. A build
the app fetches itself is never marked. So you do the Gatekeeper dance once, on this install, and
never again.

Downloads are SHA-256 verified before anything is unpacked, and the bundle swap moves the old copy
aside rather than deleting it, so a failed update rolls back instead of leaving you with nothing.

## Installing is a drag-and-drop now

No more Terminal window, no `.command` file, no instructions to follow. The zip contains
`Lumenotepad.app` and nothing else.

That was previously impossible: Apple Silicon refuses to execute unsigned code, and a self-contained
.NET build ships around 220 unsigned libraries, so *something* had to sign them — which is why there
was a script running on your Mac. Signing now happens at build time instead.

The download is also half the size (48 MB, down from 99 MB), because you no longer receive the build
for the other processor architecture.

## Keyboard shortcuts work

Every editor shortcut previously tested for Ctrl only, so on macOS — where they are all typed with
Command — **none of them fired**. Copy, cut, paste, undo, redo, select-all and every formatting
shortcut were dead inside note containers.

Now Cmd works throughout, along with the conventions that follow from it:

- `Cmd+Shift+Z` redoes.
- `Option+←/→` moves by word; `Cmd+←/→` goes to the ends of the line.
- `Cmd`-click opens a hyperlink, and `Cmd`+wheel zooms the canvas — `Ctrl`-click is left alone,
  since that is how you right-click.

## Window and glass fixes

- **Zoomed and tiled windows lay out correctly.** A Windows-only correction for Aero Snap was running
  on macOS, where its geometry is meaningless, and it was crushing the entire UI into the top-left
  corner of a filled window.
- **Full screen keeps the glass.** A native full-screen window owns its Space and has nothing behind
  it, so system vibrancy has no source and collapses to flat grey — unfixable while the window is
  genuinely full screen. Native full screen is therefore off; the green button zooms instead, which
  fills the screen and keeps the glass.
- **Menus and dialogs are frosted and properly rounded.** Right-click menus, dropdowns and message
  boxes all sit on real system glass now, with corners that match their contents.
- **Glass material picker** (Preferences → Glass). macOS has no blur-strength setting to expose — the
  material *is* the recipe — so this switches between the system's materials. The default is the
  densest, closest to the Windows look.

## Fixes

- Dragging a container out of the deleted-containers panel **crashed the app**. macOS routes
  drag-and-drop through the system pasteboard and called back into code that could not represent the
  payload, killing the process. Restore-by-drag is rewritten and behaves identically on both platforms.
- Containers no longer delete themselves when moved or resized.
- Container borders fade in on hover, with a preference to keep them always visible.
- Horizontal scrolling (`Shift`+wheel) is smooth, matching every other scroll.
- Light, Pink and Light blue themes no longer render white-on-white text after a theme switch.
- PDF and image exports use a real macOS font instead of silently falling back.

---

**Your notes** live in `~/Library/Application Support/Lumenotepad` and are never touched by
installing or updating.

**Known limitations.** The app is not notarized, so first launch needs the System Settings step
above. Intel builds are produced and signed but have not been tested on hardware. Full screen via
the green button zooms rather than entering a Space, by design.
