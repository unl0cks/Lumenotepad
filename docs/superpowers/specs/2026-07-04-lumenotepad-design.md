# Lumenotepad — design spec

Date: 2026-07-04
Status: approved design, pre-implementation
Location: `E:\CLAUDE\Lumenotepad`

## 1. Vision

Lumenotepad is a member of the **Lumen family** of Windows-first apps: a lightweight-yet-comprehensive
note-taking app inspired by Microsoft OneNote, built for **organization**. It looks and feels like
[Lumen](../../../Lumen) and its sibling [Lumenote++](../../../Lumenote++) — the same frosted-glass chrome,
tokens, rounded corners, and motion — but it is its own thing: a **freeform, rich-text note organizer**,
not a media player and not a code editor.

Design north stars:
- **Plug and play.** Works out of the box for non-technical people. Sensible defaults, nothing scary up front.
- **Still deeply customizable.** Simple things are simple; power-user options exist but stay hidden behind an
  explicit gate. "Very customizable" is a stated requirement — layout, toolbar placement, panels, accent,
  glass, roundness, fonts, themes.
- **Ship-quality visuals.** Real DWM-acrylic glass, everything rounded, animate transitions. Not approximations.

## 2. Tech stack & family alignment

- **Avalonia 12 / .NET 10**, Windows-first (same stack as Lumen and Lumenote++). Chosen by the owner over WPF
  and over an Avalonia+WebView2 editor, accepting that the rich-text engine is hand-built (see §5, §14).
- **Reused from the family (port/adapt, do not blind-copy):**
  - Design tokens from `Lumen/src/Lumen/Themes/Theme.axaml` — base `#0B0C10`, accent `#4DA6FF` (hover
    `#73BAFF`, soft `#384DA6FF`), text tiers white / `#CCFFFFFF` / `#80FFFFFF`, glass border `#33FFFFFF`,
    `Segoe UI Variable Text` UI font, and the control themes (icon/caption/menu/ghost/accent/pill buttons).
  - Custom chrome: `WindowDecorations="None"` + `Platform/WinChrome.cs`, `Controls/Squircle.cs`,
    `Controls/WindowResizeBorder.cs`, caption buttons, entrance opacity+scale animation.
  - **Glass = DWM acrylic**, following the Lumenote++ approach (`DWMWA_SYSTEMBACKDROP_TYPE` + a tint overlay
    scaled by a blur-strength setting). Lumen's *video-frame* blur (`WindowFrostBackdrop`) is media-specific and
    is **not** used here — there is no video source.

## 3. Organization model

Three levels, OneNote-standard, confirmed with the owner:

`Notebook` (a subject / project / case) → `Section` (a topic group; the colored tabs) → `Page` (one canvas).

- Sections are the middle tier and are **optional and collapsible** — a casual user can ignore them and just
  make pages inside a notebook; a heavy user uses them to keep a busy subject from becoming a flat list.
- Notebooks are color-coded (rail chips with initials). Active notebook/section/page use the accent.

## 4. Storage format

Portable and human-readable, matching the family's beside-the-exe `userdata/` convention:

- `userdata/notebooks/<notebook>/` — one folder per notebook.
- `userdata/notebooks/<notebook>/<section>/<page>.<ext>` — one file per page: its rich content **and** the
  canvas positions/sizes of every container.
- `userdata/notebooks/<notebook>/assets/` — images and file attachments referenced by pages.
- `userdata/settings.json` — app settings. `userdata/fonts/` — user-installed fonts (see §11).
- Notebooks are just folders: back them up, sync via Dropbox/OneDrive, or copy between machines. Nothing is
  locked in a database. **Auto-save** as you type.
- Page file format: a structured document (JSON-based) capturing blocks, inline runs, container geometry, and
  content-type payloads. Exact schema defined at implementation; must round-trip losslessly.

## 5. The editor — freeform canvas + rich text

The heart of the app, and its highest-risk component (see §14).

- **Freeform canvas** (the OneNote feel the owner chose): an Avalonia `Canvas` of absolutely-positioned
  **containers**. Click empty space to create a text container there; drag to move, drag handles to resize,
  stack freely. Canvas is (near-)infinite/scrollable.
- **Per-container rich text**: a purpose-built `RichTextEditor` control over Avalonia's text stack
  (`TextLayout` for measure/hit-test/render). Document model: a `RichDocument` = ordered `Block`s
  (paragraph, heading, list item) where each block holds ordered `InlineRun`s (text + formatting). The control
  owns caret, selection, keyboard/IME input, clipboard, and an undo/redo stack. Bounded per container (smaller
  editable regions are more reliable).

### Formatting tools (always in)
Bold, italic, underline, strikethrough, highlight (color), text color, per-run **font family + size**,
headings, alignment, links, and lists: **bullet / numbered / checkbox**, including **cute bullet styles**
(a small set of decorative glyph/vector bullets — flower, spark, heart, dot, etc.).

### Content types — v1
- **Images** — drag / paste / insert; move + resize on the canvas.
- **Tables** — insert; rich text in cells.
- **Checklists** — tickable to-do items.
- **Tags** — OneNote-style taggable items (Important, Question, Idea, …) that are searchable/filterable.
- **File attachments** — drop a PDF / zip / any file onto a page; stored in the notebook `assets/`.
- **PDF open + annotate** — open a PDF (and similar fixed-layout files), render its pages, and **write over
  it**: highlights + typed notes/callouts layered on top, saved alongside the source. Rendering via a
  PDFium-based .NET library (verify at build).

### Deferred to a future version
- **Freehand ink / drawing** (pen strokes on the canvas). Freehand pen-on-PDF markup rides along with this.
- **Audio recording** (voice notes).
- **True PDF text/layout editing** (reflowing/replacing the PDF's native text) — flagged stretch goal; honest
  limits (even Acrobat does this imperfectly). v1 is view + annotate only.

## 6. Layout & customization

"Very customizable" is a first-class requirement.

- **Two independent collapse toggles** in the title bar: one hides the **subjects (notebooks) rail**, a
  separate one hides the **pages panel**. Each slides away smoothly; hiding both gives an edge-to-edge canvas.
- **Dockable toolbar**: a setting *and* a one-click control to move the formatting toolbar to
  **top / left / right / bottom**. Docked left/right it reflows into a slim vertical strip; the font/size
  pickers become compact.
- **Appearance knobs** (simple, up front): accent color, glass/blur strength, corner roundness, default font,
  and theme. Technical knobs live behind the advanced gate (§10).

## 7. Themes

Two controls govern surface material:

1. **Theme** sets the **frame** (chrome, toolbar, rails, panels): **Light** (solid light), **Dark**
   (solid dark), or **Lumen** (frosted glass).
2. **Full theme** decides whether the **writing canvas** matches the frame or contrasts it.

| Theme (frame) | Full theme **off** (default) | Full theme **on** |
|---|---|---|
| **Light** | light frame + **glass canvas** | light frame + **light canvas** |
| **Dark**  | dark frame + **glass canvas**  | dark frame + **dark canvas** |
| **Lumen** | glass frame + **solid canvas** (dark by default · toggle for light) | glass frame + **glass canvas** |

Mental model: **Full theme on = the canvas adopts the frame's material (matches). Full theme off = the canvas
is the contrasting material** — glass paper under a solid frame, or solid paper under the glass frame. A
**dark/light paper toggle** is exposed for the Lumen case (glass frame doesn't imply a paper tone; dark default).

Implementation: theme = a set of swappable resource brushes + a per-surface material flag (glass vs solid).
The canvas surface reads `Theme × FullTheme (× paper tone)` from that table.

## 8. Preferences + the advanced gate

- A clean window in plain sections: **Appearance · Editor · Notebooks & storage · Fonts · About**. Sensible
  defaults; works out of the box.
- One master **Advanced settings** toggle at the bottom. Turning it on raises a **confirm dialog** that explains
  in plain language what it unlocks (technical options most people never need) and asks **Yes / No** — only Yes
  reveals them.
- Behind the gate: text `encoding` (UTF-8 / ANSI / UTF-16, line endings), file-integrity `hashes`
  (MD5 / SHA-256), glass render quality / blur strength internals, autosave interval, storage location, export
  formats, and diagnostics.

## 9. Font installer + bundled fonts

- **In-app font browser** (requires internet): pulls catalogs from **Google Fonts** and **Fontshare** — search,
  live preview with the user's own sample text, one-click **download & install**.
- Installs into the per-user `userdata/fonts/` folder, **loaded at runtime** (no admin rights; appears in the
  picker immediately). Optional "install to Windows" for system-wide use.
- **Bundled + offline on first launch**: a curated set across sans / serif / mono / handwriting / display, and
  required by name — **Gambarino, Bebas Neue, Yuyu, Caveat**. Bebas Neue + Caveat from Google Fonts (OFL);
  Gambarino + Yuyu from Fontshare (free). Actual font files bundled; each license verified at build.

## 10. The icon

A **standalone object** icon (not a squircle badge) that *is* the program: a **notebook/pad with a pencil**
(optional brush accent) and the **"lumen" lightbulb nestled into a folded page corner**, casting a soft
accent-`#4DA6FF` glow across the page — the light-meets-page motif, carrying the Lumen identity through the glow
and accent color. Transparent background.

- One **SVG master** → export a multi-resolution `.ico` (16, 24, 32, 48, 64, 128, 256) for the program icon.
- A **simplified version** becomes the in-app title-bar mark and the About screen.

## 11. Visual system (tokens & motion)

Reuse the Lumen tokens and control themes verbatim where possible; extend for note-specific surfaces:
base `#0B0C10`, accent `#4DA6FF`, glass border `#33FFFFFF`, text tiers, `Segoe UI Variable`. Squircle corners
everywhere; scale-on-hover/press micro-animations; cubic-ease transitions; entrance opacity+scale. Panels are
translucent over the DWM acrylic in Lumen/glass surfaces.

## 12. Architecture & project structure

Avalonia MVVM app, Windows head first (cross-platform heads optional later, like Lumen).

```
Lumenotepad.slnx
src/Lumenotepad/
  App.axaml, Program.cs
  Themes/           Theme.axaml (ported tokens) + Light/Dark/Lumen variants
  Platform/         WinChrome.cs, DwmAcrylic (ported/adapted)
  Controls/         Squircle, WindowResizeBorder, GlassBackdrop,
                    NoteCanvas, NoteContainer, RichTextEditor, PdfView, CuteBullet
  Views/            MainWindow, MainView, PreferencesWindow, FontInstallerWindow, dialogs
  ViewModels/       MainViewModel, NotebookVM, SectionVM, PageVM, ...
  Models/           Notebook, Section, Page, CanvasItem, RichDocument/Block/InlineRun
  Services/         AppSettings, NotebookStorage, FontService, PdfService, ThemeService
  Assets/           icon (.ico/.svg), bundled fonts
docs/               this spec + roadmap/parity
userdata/           (runtime) notebooks/, fonts/, settings.json
```

## 13. Build / run

- Family conventions: a `build.ps1` / `publish.ps1` for a self-contained `win-x64` `dist/`. Portable
  `userdata/` beside the exe. Newest stable LTS toolchain (.NET 10), matching the owner's preference.
- Owner runs and verifies on their machine; the assistant commits each logical step and never takes over the
  screen. No heavy multi-agent workflows unless asked.

## 14. Key technical risks & validation plan

1. **Custom rich-text engine (highest risk).** Build a **thin vertical slice first** — a single container where
   you can type, select, toggle bold/italic, and see a correct blinking caret + working undo — and prove it on
   Avalonia's text stack **before** committing the full formatting/list feature set. If the stack fights a
   specific behavior (IME, complex selection), surface the tradeoff rather than shipping something janky.
   Evaluate any viable existing Avalonia rich-text component during the slice; plan for custom.
2. **PDF rendering + annotation.** Confirm a PDFium-based .NET library renders pages acceptably and that an
   overlay-annotation model round-trips. Deep editing stays out of scope.
3. **DWM acrylic glass.** Proven in Lumenote++; port the approach. Verify the Light/Dark/Lumen × Full-theme
   material table renders correctly per §7.
4. **Runtime font loading + installer.** Confirm fonts in `userdata/fonts/` load into Avalonia at runtime and
   appear in the picker; confirm Google Fonts / Fontshare fetch + install flow (offline-friendly with the
   bundled set).

## 15. Out of scope (v1)

Freehand ink/drawing, audio recording, true PDF text/layout editing, non-Windows heads, real-time
collaboration/cloud sync (folder-based sync via the user's own Dropbox/OneDrive is supported implicitly).

## 16. Success criteria

- Create notebooks → sections → pages; organize and navigate them; everything persists portably and auto-saves.
- Freeform canvas with movable rich-text containers; bold/italic/underline/strike, highlight, color, per-run
  font+size, cute bullets, lists, headings, images, tables, checklists, tags, attachments, PDF view+annotate.
- Collapsible subjects rail and pages panel; toolbar dockable to all four edges.
- Light / Dark / Lumen themes with the Full-theme material table behaving exactly as §7.
- Simple preferences that work out of the box; advanced gate with an explaining confirm dialog.
- Font installer pulling Google Fonts + Fontshare; Gambarino, Bebas Neue, Yuyu, Caveat bundled by default.
- Lumen-inspired standalone icon (in-app + `.ico`), and the family's frosted-glass look and motion throughout.
