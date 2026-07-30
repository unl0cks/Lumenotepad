<p align="center">
  <img src="assets/lumenotepad-icon-256.png" alt="Lumenotepad icon" width="176" height="176">
</p>

<h1 align="center">Lumenotepad</h1>

<p align="center">
  <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="https://avaloniaui.net/"><img alt="Avalonia" src="https://img.shields.io/badge/Avalonia-12-6B57FF?style=for-the-badge"></a>
  <img alt="macOS" src="https://img.shields.io/badge/macOS-13%2B-000000?style=for-the-badge&logo=apple&logoColor=white">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%2F11-0078D4?style=for-the-badge&logo=windows&logoColor=white">
  <img alt="Tests" src="https://img.shields.io/badge/tests-307%20passing-3FB950?style=for-the-badge">
</p>

Lumenotepad is a freeform note organizer for macOS and Windows, built with Avalonia. Notes
are not documents in a list — every page is a canvas you drop containers onto, anywhere you
like, the way OneNote works. Each container holds its own rich text, and pages can also carry
tables, images, file attachments, annotated PDFs, and mind maps.

It is a native app by design. There is no browser engine anywhere in it: PDF pages are
rasterized with native PDFium, text is laid out through Skia and HarfBuzz, and the window
chrome is real platform chrome — DWM acrylic on Windows, `NSVisualEffectView` vibrancy on
macOS.

## Contents

- [Highlights](#highlights)
- [Project Status](#project-status)
- [Install](#install)
- [Updates](#updates)
- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [Publishing](#publishing)
- [User Data](#user-data)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Architecture](#architecture)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [License](#license)

## Highlights

- **Freeform pages.** Click anywhere to start a container; drag, resize, and stack them.
  Containers you create and never type into tidy themselves away.
- **Notebooks → sections → pages**, or a flat notebooks → pages mode if sections are more
  structure than you want.
- **Rich text** with its own editor: styles, highlights, bullet and numbered lists with
  configurable furniture, links, and per-notebook paper styles and templates.
- **Mind maps** with typed bubbles (title pill, information squircle, callout), labelled
  connectors, a nominated central bubble, and three auto-arrange layouts (radial, hybrid,
  top-down).
- **PDF viewing and annotation** through native PDFium — highlights and notes over the page,
  with a flattened-copy export.
- **Export** to Markdown, HTML, RTF, PDF, DOCX, PNG, and EPUB.
- **Five themes** — Lumen (glass), Dark, Light, Pink, Light blue — with accent overrides, an
  optional accent that follows the open notebook, and adjustable corner roundness.
- **Real platform glass.** Acrylic on Windows; on macOS the system vibrancy materials, with a
  material picker because macOS exposes no blur-strength setting of its own.
- **Font browser and installer** that fetches faces from Google Fonts and Fontshare into the
  app's own font folder, no system install required.
- **Deleted-container history** per page, restorable by dragging back onto the canvas.
- **Automatic backups** to a folder you choose, with a retention count.

## Project Status

Actively developed. macOS is the newer target and the one under active test; Windows is the
platform it grew up on.

| Area | Status |
| --- | --- |
| Windows desktop | Stable, the primary development platform |
| macOS (Apple Silicon) | Working, under active testing |
| macOS (Intel) | Built and signed, untested on hardware |
| Editor, canvas, notebooks | Stable |
| Mind maps | Stable |
| PDF annotation | Usable |
| Export formats | Usable |
| In-app updates | New in 1.2.0; verified end-to-end on Windows, untried on macOS |
| Windows packaging | Portable zip, beta |
| Code signing / notarization | Ad-hoc only — see [Install](#install) |

## Install

### macOS

Download the zip for your Mac (`arm64` for Apple Silicon, `x64` for Intel), then drag
`Lumenotepad.app` into Applications.

The first launch is blocked, because the app is signed ad-hoc rather than with a paid Apple
Developer ID:

1. Double-click Lumenotepad → "Apple could not verify Lumenotepad" → **Done**.
2. System Settings → Privacy & Security → scroll down → **Open Anyway**, then confirm.
3. It opens. This is a one-time step, not once per launch.

Builds are code-signed at package time with [rcodesign](https://github.com/indygreg/apple-platform-rs),
which matters more than it sounds: Apple Silicon refuses to execute unsigned Mach-O, and a
self-contained .NET publish ships around 220 unsigned dylibs. Signing them during packaging is
what makes a plain drag-install possible at all.

### Windows (beta)

Download the portable zip, extract the folder anywhere **writable**, and run
`Lumenotepad.exe`. SmartScreen will warn the first time — "More info" → "Run anyway".

It is portable on purpose. All user data lives in a `userdata` folder beside the executable,
nothing touches the registry, and uninstalling means deleting the folder. Do not extract it
into `C:\Program Files`: Windows blocks normal processes from writing there, and saving would
fail. An installer would first require moving the Windows data directory to `%APPDATA%`, which
is a migration for existing portable copies rather than a packaging change.

## Updates

**Both platforms** update in place: Preferences → **About** → **Check for updates**. That opens the
updater window, which finds the right build for whichever OS is running, downloads it with progress,
verifies it, and restarts into it.

This exists specifically to dodge Gatekeeper. An ad-hoc signature satisfies the kernel but not
Gatekeeper, so any build macOS sees arrive from a browser or a chat client is quarantined and
costs another trip through System Settings — and that toll is per download, meaning per
release. The quarantine flag is applied by the *downloading* application, though, not by the
network, so a build the app fetches itself is never marked. Pay the toll once on first install
and never again.

Downloads are SHA-256 verified before anything is unpacked, and the bundle swap runs from a
detached script that waits for the app to exit and moves the old bundle aside rather than
deleting it, so a failed swap rolls back instead of leaving nothing behind.

On **Windows** the reason is different but the shape is the same: the build is portable, so updating
means replacing the program files around the `userdata` folder and leaving it alone. The swap uses
`robocopy` without `/MIR`, which is precisely what preserves your notebooks.

A running executable cannot overwrite itself on either OS, so both hand the swap to a detached script
that waits for the app to exit first.

## Requirements

- **Running:** macOS 13+, or Windows 10/11 x64. Nothing else to install — builds are
  self-contained.
- **Building:** .NET 10 SDK.
- **Packaging macOS builds:** `cargo install apple-codesign` (provides `rcodesign`). Works from
  Windows or Linux; no Mac required.
- **Regenerating icons:** `dotnet run --project tools/icongen`.
- **Regenerating the icon font:** Python with `fonttools`, then
  `python tools/lumenicons/build_lumenicons.py`.

## Quick Start

```bash
dotnet restore Lumenotepad.slnx
dotnet build
dotnet run --project src/Lumenotepad/Lumenotepad.csproj
```

## Publishing

Both scripts read the version from `<Version>` in `src/Lumenotepad/Lumenotepad.csproj`, which is
the single source of truth — the binary, the macOS `Info.plist`, and the update manifest cannot
drift apart.

```bash
tools/publish-macos.sh
```

Publishes both architectures, code-signs each `.app`, and writes to `dist/`:

```text
Lumenotepad-macOS-<version>-arm64.zip
Lumenotepad-macOS-<version>-x64.zip
```

```bash
tools/publish-windows.sh
```

Writes `dist/Lumenotepad-<version>-win-x64-portable.zip`.

```bash
python tools/publish-manifest.py
```

Writes `dist/latest.json` — one manifest covering every platform, hashed from whatever zips are in
`dist/`. `UpdateService.PlatformKey` picks its own entry (`macos-arm64`, `macos-x64`, `win-x64`), so a
single file on a single release serves both operating systems and nobody can be handed the wrong build.

For the in-app updater to find a release, the zips **and** `latest.json` have to be uploaded to it. `UpdateService.ManifestUrl` points at the newest non-prerelease GitHub release, so
publishing a Windows build as a *prerelease* deliberately keeps it out of the updater's path.
Override the endpoints with `LUMENOTEPAD_RELEASE_BASE` when packing and `LUMENOTEPAD_UPDATE_URL`
at runtime to test the whole flow against a local file server.

## User Data

| Platform | Location |
| --- | --- |
| macOS | `~/Library/Application Support/Lumenotepad` |
| Windows | `userdata` beside `Lumenotepad.exe` (portable) |

They differ for a reason: the macOS app ships as a bundle that gets replaced wholesale on every
update, so data cannot live inside it.

Either way it holds `settings.json`, the `notebooks` tree, installed `fonts`, and nothing else.
Installing or updating never touches it.

## Keyboard Shortcuts

Shortcuts use **Cmd on macOS** and **Ctrl on Windows**. The formatting ones are rebindable in
Preferences → Shortcuts; the structural ones are fixed.

| Action | Shortcut |
| --- | --- |
| Bold / Italic / Underline | `Cmd/Ctrl` + `B` / `I` / `U` |
| Strikethrough | `Cmd/Ctrl` + `Shift` + `S` |
| Quick highlight | `Cmd/Ctrl` + `Shift` + `H` |
| Insert date & time | `Cmd/Ctrl` + `Shift` + `T` |
| Bullet / numbered list | `Cmd/Ctrl` + `Shift` + `8` / `7` |
| Copy / Cut / Paste | `Cmd/Ctrl` + `C` / `X` / `V` |
| Undo | `Cmd/Ctrl` + `Z` |
| Redo | `Cmd/Ctrl` + `Shift` + `Z`, or `Ctrl` + `Y` |
| Select all | `Cmd/Ctrl` + `A` |
| Word-wise motion | `Option` + `←`/`→` (macOS), `Ctrl` + `←`/`→` (Windows) |
| Line start / end | `Cmd` + `←`/`→` (macOS), `Home` / `End` |
| Duplicate bubble | `Cmd/Ctrl` + `D` |
| New linked child bubble | `Tab` on a focused bubble |
| Open a hyperlink | `Cmd`-click (macOS), `Ctrl`-click (Windows) |
| Zoom the canvas | `Cmd/Ctrl` + wheel, `Cmd/Ctrl` + `0` to reset |
| Pan horizontally | `Shift` + wheel |
| Summon the window (Windows) | `Ctrl` + `Alt` + `N`, if enabled |

## Architecture

```text
src/Lumenotepad/
  Editor/      the canvas and the rich-text engine (document model, layout, JSON persistence)
  Views/       windows, the main view, dialogs, popup effects, animation helpers
  ViewModels/  MainViewModel and the notebook draft model (CommunityToolkit.Mvvm)
  Services/    settings, themes, export, fonts, PDF rendering, backups, updates
  Platform/    the OS boundary — DWM acrylic, Win32 chrome, macOS vibrancy, window geometry
  Themes/      the Fluent-derived theme and control styles
tools/         icon generation, the icon-font builder, publishing scripts
tests/         xUnit coverage of the model, services, and pure geometry
```

Two conventions worth knowing before changing anything:

- **`Platform/` is the only place with OS-specific code.** Every Win32 P/Invoke is guarded with
  `OperatingSystem.IsWindows()`, and the macOS side reaches AppKit through the Objective-C
  runtime because Avalonia does not surface what the glass needs.
- **Animation goes through `Views/Motion`**, not XAML transitions. Declarative transform
  transitions did not run reliably in this build; `Motion` drives them from a clock instead.

## Testing

```bash
dotnet test
```

307 tests covering the document model, canvas persistence, export formats, theme palettes,
settings, fonts, the keymap, PDF annotations, and pure geometry. They are deliberately
platform-agnostic: no test needs a window, so the suite runs the same on either OS.

What tests cannot cover is the platform chrome — glass, vibrancy, window tiling, Gatekeeper.
Those need a real machine on each OS.

## Troubleshooting

### macOS: the app will not open at all

If it bounces or dies immediately rather than showing the Gatekeeper dialog, the ad-hoc
signature is the thing to suspect. Check what macOS actually objected to:

```bash
log show --predicate 'process == "Lumenotepad"' --last 5m
```

### macOS: the glass looks flat grey, or opaque

First check Accessibility → Display → **Reduce transparency** is off; it disables vibrancy
system-wide and produces exactly this.

The app writes a diagnostics file on every launch that says whether macOS actually granted a
frost layer:

```bash
cat ~/Library/Application\ Support/Lumenotepad/macos-chrome-diagnostics.txt
```

`frostLayers = 0` means the OS declined the frost, which is a different problem from the app
requesting it wrongly.

### macOS: glass disappears in full screen

Expected, and unfixable while the window is genuinely full screen: a native full-screen window
owns its Space and has nothing behind it, so behind-window vibrancy has no source to sample.
Native full screen is therefore disabled — the green button zooms instead, which keeps the
window on the desktop and the glass intact.

### macOS: the glass is too bright, or too blurred

macOS has no blur-radius API; the `NSVisualEffectMaterial` *is* the recipe. Preferences → Glass
→ **Glass material** switches between them, and the glass tint slider layers on top.

### Windows: notes will not save

The folder is not writable — most likely it was extracted into `C:\Program Files`. Move it
somewhere under your user profile.

## Roadmap

Near-term:

- Prove the macOS update path over a real release-to-release hop.
- Notarize with an Apple Developer ID, which would remove the first-launch Gatekeeper step
  entirely.
- Screenshots in this README once the macOS chrome settles.
- A signed Windows installer, which needs the data directory moved to `%APPDATA%` first.

Later:

- Sync between machines.
- A real DMG, which needs a macOS runner to build.

## License

[GNU General Public License v3.0](LICENSE). In short: you may use, study, share and modify
Lumenotepad, but anything you distribute that is built from it has to carry the same freedoms and
ship its source. There is no warranty.

Every dependency is permissively licensed (MIT or BSD), so nothing here conflicts. Third-party
attributions are recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
