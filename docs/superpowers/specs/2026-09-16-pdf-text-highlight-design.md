# PDF text highlighting: design

Date: 2026-09-16
Status: approved (owner), pre-implementation
Target release: 1.2.14

## Goal

Highlighting in the PDF viewer currently means dragging a box over the page. On a MacBook trackpad that
is awkward (dragging means holding the click while moving), and a box never quite matches the words.
Highlights should follow the text itself, and nobody should have to drag to make one. The box stays
available for diagrams and pictures.

## What the user sees

### A. Select, then Highlight (primary path, no tool active)

- Over text, the pointer is a text cursor.
- Ways to select: click then Shift-click the end; double-click for a word; triple-click for a line;
  dragging also works.
- The selection is drawn as a soft blue tint that follows the lines of text.
- A small bar appears just above the selection (below it when there is no room above) with
  **Highlight** (showing the current colour) and **Copy**. Ctrl/Cmd+C also copies.
- Clicking empty page space or pressing Esc clears the selection.

### B. Highlight tool with a mode menu

A `▾` button next to Highlight (the same pattern as the Arrow options button) offers two modes:

- **Text** (default): click where the text starts, then click where it ends. Between the clicks the
  highlight previews in the current colour as the pointer moves, with no button held. Double-click
  highlights a single word. Esc cancels after the first click. Press, drag and release also works.
- **Area**: today's drag-a-box, unchanged.

The chosen mode is saved in settings. The tool tip on the Highlight button describes the active mode.

### Highlights

- A text highlight is one annotation however many lines it covers.
- Clicking a text highlight selects it: the × deletes all of it, a swatch click recolours all of it,
  Delete/Backspace remove it. Text highlights have no move or resize handles, because they belong to
  the words underneath. Box highlights keep their current handles.
- In Select mode, a press on a text highlight that is released without moving selects the highlight;
  a press that moves, or a Shift-click, makes a text selection instead, so text under a highlight can
  still be selected.
- Undo/redo cover text highlights like every other annotation.
- "Save a copy…" bakes text highlights in, one rectangle per line.

### Pages without text

Scanned pages are pictures and have no characters. On such a page Text mode behaves as Area mode and the
status line says: "This page is a picture, so there's no text to select. Drag to highlight an area."
Select mode shows no text cursor there. While a document's text is still being read, text actions are
ignored and the status line says "Reading the page text…".

### Limits

A selection stays within one page. Highlighting across a page break takes two highlights.

## Architecture

### 1. `Services/PdfText.cs` (new): the only code that calls PDFium's text API

- P/Invoke into the same `pdfium` library PDFtoImage already ships (`pdfium.dll` on Windows,
  `libpdfium.dylib` on macOS, both bblanchon.PDFium 137.0.7149; every export below is present in both).
- Entry point: `IReadOnlyList<PdfPageText> ReadAll(byte[] pdf)`. It opens the document once from a
  pinned copy of the bytes, walks every page, and closes everything in `finally` blocks.
- Per character: `FPDFText_GetUnicode`, `FPDFText_IsGenerated`, `FPDFText_GetLooseCharBox` (the loose
  box spans the full line height, so a line's highlight has an even height).
- Each box corner is mapped with `FPDF_PageToDevice` into a 100000 × 100000 device rectangle with
  rotation 0, then divided by 100000. This gives the same 0 to 1, top-left-origin page units that
  annotations already use, with the page's own rotation and crop box applied exactly as rendering
  applies them.
- `FPDF_InitLibrary` is called once before first use (idempotent in this PDFium version); the viewer
  has always called PDFtoImage first anyway.
- Output is plain data: `PdfPageText(int Page, IReadOnlyList<PdfChar> Chars)` and
  `PdfChar(int CodePoint, double X0, double Y0, double X1, double Y1, bool Generated)`.
- Any exception (missing export, corrupt page) makes that page, or the whole document, come back with
  no characters. It is logged once through `StartupLog.Mark`. It never throws to the caller.

### 2. One lock around all PDFium use

`PdfRenderer` gains `internal static readonly object Gate`. Every PDFtoImage call in `PdfRenderer`
and every call in `PdfText` runs inside `lock (Gate)`. PDFium is not thread-safe, and PDFtoImage's own
internal lock does not cover calls made outside it. `PdfRenderer` remains the only other place in the
app that touches PDFium.

### 3. `Editor/PdfTextSelection.cs` (new): pure geometry, no PDFium, no UI

A selection is a pair of caret positions (0 to N, between characters), like a text editor.

- `int CaretAt(chars, Point p)`: the character whose box contains `p`, else the nearest character on
  the nearest line, else the nearest box. Right half of a character gives the caret after it.
  Returns -1 when the page has no characters or the point is farther than a line height from any box.
- `(int, int) WordAt(chars, int index)`: letters and digits, plus apostrophes inside a word.
- `(int, int) LineAt(chars, int index)`: the run of characters on the same visual line.
- `IReadOnlyList<Rect> Rects(chars, int start, int end)`: one rectangle per visual line. A character
  joins the current line when its vertical centre lies within the line's vertical span and it does not
  jump back left of the line's start; otherwise a new line starts. Characters with an empty box
  (generated spaces and line breaks) are skipped for geometry.
- `string TextOf(chars, int start, int end)`: code points in order, generated line breaks become `\n`,
  trailing whitespace trimmed.

### 4. `Views/PdfTextSelector.cs` (new): the interaction

Owns the selection state (page, anchor, focus), the pending first click of the two-click flow, the
selection and preview drawing, and the pop-up bar. `PdfViewer` forwards pointer and key events to it
and supplies callbacks for "create highlight from these rects" and "copy this text". The pop-up bar
uses the existing `Button.pill` style.

Each page's visual stack changes from `[Image, Overlay]` to `[Image, SelectionLayer, Overlay, BarLayer]`:

- `SelectionLayer`: not hit-testable; selection tint and two-click preview. Sits under the
  annotations and is independent of `RedrawPage`, which clears only the overlay.
- `BarLayer`: a Canvas with no background, so only the bar itself takes clicks.

Selection is stored as character positions, so zooming just redraws it from the normalized rectangles.

### 5. Data model

`PdfAnnotation` gains:

```
[JsonPropertyName("rects")] List<double[]>? Rects   // each [x, y, w, h], 0 to 1 page units; omitted when null
```

`X/Y/W/H` hold the union of the rectangles, so code that places the × button or computes bounds keeps
working. Highlights without `rects` behave exactly as today. An older build opening a newer sidecar
draws each text highlight as one box around it; nothing is lost.

Drawing: highlights with `Rects` draw one rounded rectangle per line, each hit-testable, with the ×
at the top-right of the last line when selected, and no handles. `FlattenHighlight` draws each rectangle.

### 6. Setting

`AppSettings.PdfHighlightText` (bool, default true), plumbed exactly like `RoundedPdfCorners`: a
`MainViewModel` property pushed into a `PdfViewer` static at startup. When the user switches mode in
the viewer, the change flows back through the view model and is saved.

### 7. Loading

After `LoadAsync` finishes rendering the page images, one background task runs `PdfText.ReadAll`
(under the gate) and hands the result to the selector. A newer load (`_loadGen`) discards a stale result.

## Files touched

New: `Services/PdfText.cs`, `Editor/PdfTextSelection.cs`, `Views/PdfTextSelector.cs`, tests.
Changed: `Services/PdfRenderer.cs` (gate), `Editor/PdfAnnotations.cs` (`Rects`), `Views/PdfViewer.axaml`
(`▾` button), `Views/PdfViewer.axaml.cs` (page stack, forwarding, drawing, export),
`Services/AppSettings.cs`, `ViewModels/MainViewModel.cs`, `Views/MainView.axaml.cs` (setting),
`docs/release-notes-1.2.14.md`. No new packages.

## Testing

- **Unit (xUnit, synthetic boxes):** `CaretAt` (inside, between, margins, far away, empty page),
  `WordAt`, `LineAt`, `Rects` (single line, wrapped, two columns, generated spaces, superscript staying
  on its line), `TextOf`.
- **End-to-end (real PDFium):** the tests build tiny PDFs in code (Helvetica text at known positions)
  in three variants: plain, `/Rotate 90`, and a `/CropBox` offset. `PdfText.ReadAll` must place a known
  character where it was drawn, against positions derived independently in the test (for example,
  under `/Rotate 90` a user-space point `(px, py)` on a `W × H` media box lands at `(py / H, px / W)`).
- **Round trip:** a highlight with `Rects` survives sidecar save/load, and undo/redo.
- **Visual:** a headless render of a real page with a text highlight on top, saved as PNG and checked.
- **Regression:** the full suite, Windows and macOS publish builds, then a check on the MacBook.
