# Lumenotepad — M3 Rich-Text Editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Checkbox steps.

**Goal:** A hand-built native rich-text editor on Avalonia's text stack. **Vertical slice first** (M3.1):
one editable region proving type / caret / click+drag selection / bold+italic / undo-redo / Enter/Backspace
paragraph ops. Then (M3.2+) the full formatting set, page persistence, and the freeform canvas of containers.

**Architecture:** `Editor/RichModel.cs` = pure document model (`RichDocument` → `Paragraph` → `RichRun{Text,Bold,Italic}`,
`DocPos(Para,Off)`), fully unit-tested, no Avalonia deps. `Editor/RichTextEditor.cs` = custom `Control`:
one cached `TextLayout` per paragraph (ITextSource of styled runs → mixed bold/italic inside a paragraph),
`HitTestPoint`/`HitTestTextPosition`/`HitTestTextRange` for caret/click/selection, `OnTextInput` typing,
`OnKeyDown` nav/shortcuts (arrows, Home/End, Shift-select, Ctrl+A/B/I/Z/Y/C/X/V), blinking caret, snapshot undo.
Hosted in the M2 canvas placeholder; **slice content is session-only** (a per-page in-memory map in the VM) —
the persisted page format lands with M3.2.

**Known slice gaps (deliberate):** no IME client yet, clipboard is plain-text, no formatting toolbar buttons
(Ctrl+B/I only), content not saved to disk.

## Tasks
1. **RichModel (TDD):** insert (incl. multi-line paste), delete range across paragraphs (merge), split paragraph,
   run splitting/normalization on format toggles, all-bold/italic queries, snapshot/restore. Commit.
2. **RichTextEditor control:** render, caret, mouse, keyboard, undo. Build clean. Commit.
3. **Host in canvas:** ScrollViewer + editor under the page title; VM keeps `Dictionary<pageId, RichDocument>`;
   switching pages swaps documents. Launch-check. **User verifies** typing/selection/bold/undo feel. Commit.
