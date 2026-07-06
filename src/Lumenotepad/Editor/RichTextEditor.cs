using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumenotepad.Editor;

/// <summary>A hand-built rich-text editor on Avalonia's text stack: one cached <see cref="TextLayout"/> per
/// paragraph (an <see cref="ITextSource"/> of styled runs gives mixed bold/italic inside a paragraph),
/// TextLayout hit-testing for caret/click/selection, OnTextInput typing, snapshot-based undo.
/// M3 vertical slice: type, click+drag select, Ctrl+B/I, Ctrl+Z/Y, Enter/Backspace/Delete, arrows,
/// Home/End, Ctrl+A, plain-text clipboard.</summary>
public sealed class RichTextEditor : Control
{
    // ---- appearance (slice: plain CLR props; styled props when the toolbar lands) ----
    public FontFamily FontFamily { get; set; } = new("Segoe UI Variable Text, Segoe UI");
    public double FontSize { get; set; } = 15;
    public IBrush Foreground { get; set; } = Brushes.White;
    public IBrush SelectionBrush { get; set; } = new SolidColorBrush(Color.Parse("#554DA6FF"));
    public IBrush CaretBrush { get; set; } = new SolidColorBrush(Color.Parse("#4DA6FF"));
    public double ParagraphSpacing { get; set; } = 4;

    private RichDocument _doc = new();
    public RichDocument Document
    {
        get => _doc;
        set
        {
            if (ReferenceEquals(_doc, value)) return;
            _doc.Changed -= OnDocChanged;
            _doc = value ?? new RichDocument();
            _doc.Changed += OnDocChanged;
            _caret = _anchor = new DocPos(0, 0);
            _undo.Clear(); _redo.Clear(); _typingBurst = false;
            InvalidateLayouts();
        }
    }

    // ---- caret / selection ----
    private DocPos _caret, _anchor;
    private bool _caretVisible = true;
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private double _desiredX = -1;           // sticky column for up/down
    private bool _pendingBold, _pendingItalic, _hasPending;   // Ctrl+B/I with a collapsed selection

    // ---- undo ----
    private readonly Stack<(DocSnapshot Snap, DocPos Caret, DocPos Anchor)> _undo = new();
    private readonly Stack<(DocSnapshot Snap, DocPos Caret, DocPos Anchor)> _redo = new();
    private bool _typingBurst;

    // ---- layout cache: one TextLayout per paragraph, keyed by (paragraph, version, width) so a keystroke
    // rebuilds ONLY the paragraph it touched (O(1) per edit, not O(document)) ----
    private readonly List<TextLayout> _layouts = new();
    private readonly Dictionary<Paragraph, (TextLayout Layout, int Version, double Width)> _cache = new();
    private double[] _tops = Array.Empty<double>();
    private double _contentHeight;
    private double _layoutWidth = -1;
    private bool _layoutsDirty = true;

    public RichTextEditor()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ClipToBounds = true;
        _doc.Changed += OnDocChanged;
        _blink.Tick += (_, _) => { _caretVisible = !_caretVisible; InvalidateVisual(); };
        GotFocus += (_, _) => { _caretVisible = true; _blink.Start(); InvalidateVisual(); };
        LostFocus += (_, _) => { _blink.Stop(); _caretVisible = false; InvalidateVisual(); };
    }

    private void OnDocChanged()
    {
        // Incremental relayout on the spot (only touched paragraphs rebuild), then invalidate measure ONLY
        // if the content height actually changed — otherwise a full window layout pass runs per keystroke.
        if (_layoutWidth < 0) { InvalidateLayouts(); return; }
        double prevH = _contentHeight;
        _layoutsDirty = true;
        EnsureLayouts(_layoutWidth);
        if (Math.Abs(_contentHeight - prevH) > 0.5) InvalidateMeasure();
        InvalidateVisual();
    }

    private void InvalidateLayouts()
    {
        _layoutsDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    // =========================== layout ===========================

    private void EnsureLayouts(double width)
    {
        double w = double.IsFinite(width) && width > 1 ? width : double.PositiveInfinity;
        if (!_layoutsDirty && Math.Abs(w - _layoutWidth) < 0.5 && _layouts.Count == _doc.Paragraphs.Count)
            return;
        _layoutWidth = w;
        _layoutsDirty = false;

        _layouts.Clear();
        var next = new Dictionary<Paragraph, (TextLayout, int, double)>(_doc.Paragraphs.Count);
        foreach (var p in _doc.Paragraphs)
        {
            TextLayout layout;
            if (_cache.TryGetValue(p, out var e) && e.Version == p.Version && Math.Abs(e.Width - w) < 0.5)
                layout = e.Layout;                                     // untouched paragraph → reuse
            else
            {
                if (_cache.TryGetValue(p, out var stale)) stale.Layout.Dispose();
                layout = BuildLayout(p, w);
            }
            _cache.Remove(p);                                          // claimed (whatever's left is disposed below)
            next[p] = (layout, p.Version, w);
            _layouts.Add(layout);
        }
        foreach (var orphan in _cache.Values) orphan.Layout.Dispose();  // paragraphs deleted from the doc
        _cache.Clear();
        foreach (var kv in next) _cache[kv.Key] = kv.Value;

        // cumulative paragraph tops (Render/hit-testing read these instead of re-walking heights)
        if (_tops.Length != _layouts.Count) _tops = new double[_layouts.Count];
        double y = 0;
        for (int i = 0; i < _layouts.Count; i++)
        {
            _tops[i] = y;
            y += _layouts[i].Height + ParagraphSpacing;
        }
        _contentHeight = _layouts.Count > 0 ? y - ParagraphSpacing : 0;
    }

    private TextLayout BuildLayout(Paragraph p, double width)
    {
        double maxWidth = double.IsFinite(width) && width > 1 ? width : double.PositiveInfinity;
        if (p.Runs.Count == 0)
            return new TextLayout("", new Typeface(FontFamily), FontSize, Foreground,
                                  textWrapping: TextWrapping.Wrap, maxWidth: maxWidth);

        var defaultProps = new GenericTextRunProperties(new Typeface(FontFamily), FontSize, foregroundBrush: Foreground);
        var paraProps = new GenericTextParagraphProperties(
            FlowDirection.LeftToRight, TextAlignment.Left, true, false,
            defaultProps, TextWrapping.Wrap, double.NaN, 0, 0);
        return new TextLayout(new RunsTextSource(p, this), paraProps, maxWidth: maxWidth);
    }

    private sealed class RunsTextSource : ITextSource
    {
        private readonly Paragraph _p;
        private readonly RichTextEditor _e;
        public RunsTextSource(Paragraph p, RichTextEditor e) { _p = p; _e = e; }

        public TextRun? GetTextRun(int index)
        {
            int acc = 0;
            foreach (var r in _p.Runs)
            {
                int end = acc + r.Text.Length;
                if (index < end)
                {
                    var typeface = new Typeface(_e.FontFamily,
                        r.Italic ? FontStyle.Italic : FontStyle.Normal,
                        r.Bold ? FontWeight.Bold : FontWeight.Normal);
                    var props = new GenericTextRunProperties(typeface, _e.FontSize, foregroundBrush: _e.Foreground);
                    return new TextCharacters(r.Text.Substring(index - acc), props);
                }
                acc = end;
            }
            return null;
        }
    }

    private double ParagraphTop(int para) => para >= 0 && para < _tops.Length ? _tops[para] : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayouts(availableSize.Width);
        return new Size(availableSize.Width, _contentHeight + 4);
    }

    // =========================== rendering ===========================

    public override void Render(DrawingContext ctx)
    {
        // A control with no rendered fill is NOT hit-testable — clicks would pass straight through and
        // the editor could never take focus. Transparent still registers for hit-testing.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        EnsureLayouts(Bounds.Width);

        // selection highlight
        var (selA, selB) = SelOrdered();
        if (selA != selB)
        {
            for (int pi = selA.Para; pi <= selB.Para && pi < _layouts.Count; pi++)
            {
                int start = pi == selA.Para ? selA.Off : 0;
                int end = pi == selB.Para ? selB.Off : _doc.Paragraphs[pi].Length;
                double top = ParagraphTop(pi);
                if (end > start)
                {
                    foreach (var r in _layouts[pi].HitTestTextRange(start, end - start))
                        ctx.FillRectangle(SelectionBrush, new Rect(r.X, r.Y + top, Math.Max(r.Width, 2), r.Height));
                }
                else if (_doc.Paragraphs[pi].Length == 0)
                {
                    // fully-selected empty paragraph → a thin stub so the selection reads continuously
                    ctx.FillRectangle(SelectionBrush, new Rect(0, top, 6, _layouts[pi].Height));
                }
            }
        }

        // text
        for (int i = 0; i < _layouts.Count; i++)
            _layouts[i].Draw(ctx, new Point(0, ParagraphTop(i)));

        // caret
        if (_caretVisible && IsFocused)
        {
            var rect = CaretRect();
            ctx.FillRectangle(CaretBrush, new Rect(rect.X, rect.Y, 1.6, rect.Height));
        }
    }

    private Rect CaretRect()
    {
        var p = _caret;
        _doc.Clamp(ref p);
        if (p.Para >= _layouts.Count) return new Rect(0, 0, 1.6, FontSize * 1.35);
        var r = _layouts[p.Para].HitTestTextPosition(p.Off);
        return new Rect(r.X, r.Y + ParagraphTop(p.Para), r.Width, r.Height <= 0 ? FontSize * 1.35 : r.Height);
    }

    private (DocPos a, DocPos b) SelOrdered() => _anchor <= _caret ? (_anchor, _caret) : (_caret, _anchor);
    private bool HasSelection => _anchor != _caret;

    // =========================== hit testing ===========================

    private DocPos PosFromPoint(Point pt)
    {
        EnsureLayouts(Bounds.Width);
        if (_layouts.Count == 0) return new DocPos(0, 0);
        double y = 0;
        for (int i = 0; i < _layouts.Count; i++)
        {
            double h = _layouts[i].Height + (i < _layouts.Count - 1 ? ParagraphSpacing : 0);
            if (pt.Y < y + h || i == _layouts.Count - 1)
            {
                var hit = _layouts[i].HitTestPoint(new Point(pt.X, Math.Max(0, pt.Y - y)));
                return new DocPos(i, hit.TextPosition);
            }
            y += h;
        }
        return _doc.End;
    }

    // =========================== undo ===========================

    private void PushUndo(bool typing = false)
    {
        if (typing && _typingBurst) return;                 // coalesce a typing burst into one undo step
        _undo.Push((_doc.TakeSnapshot(), _caret, _anchor));
        _redo.Clear();
        _typingBurst = typing;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push((_doc.TakeSnapshot(), _caret, _anchor));
        var (snap, caret, anchor) = _undo.Pop();
        _doc.Restore(snap);
        _caret = caret; _anchor = anchor;
        _typingBurst = false;
        ClampSel();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push((_doc.TakeSnapshot(), _caret, _anchor));
        var (snap, caret, anchor) = _redo.Pop();
        _doc.Restore(snap);
        _caret = caret; _anchor = anchor;
        _typingBurst = false;
        ClampSel();
    }

    private void ClampSel()
    {
        var c = _caret; _doc.Clamp(ref c); _caret = c;
        var a = _anchor; _doc.Clamp(ref a); _anchor = a;
    }

    // =========================== editing ===========================

    private void DeleteSelection()
    {
        var (a, b) = SelOrdered();
        _doc.DeleteRange(a, b);
        _caret = _anchor = a;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        var text = new string(e.Text.Where(c => c >= ' ' || c == '\t').ToArray());
        if (text.Length == 0) return;

        PushUndo(typing: !HasSelection);
        if (HasSelection) DeleteSelection();

        var (bold, italic) = _hasPending ? (_pendingBold, _pendingItalic) : _doc.FormatAt(_caret);
        _caret = _anchor = _doc.InsertText(_caret, text, bold, italic);
        _hasPending = false;
        AfterEdit();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool handled = true;

        switch (e.Key)
        {
            case Key.Left or Key.Right when !ctrl:
                MoveCaret(_doc.Move(_caret, e.Key == Key.Right ? 1 : -1), shift);
                break;
            case Key.Left when ctrl:
                MoveCaret(PrevWordPos(_caret), shift);
                break;
            case Key.Right when ctrl:
                MoveCaret(NextWordPos(_caret), shift);
                break;
            case Key.Up or Key.Down:
                MoveCaretVertical(e.Key == Key.Down ? 1 : -1, shift);
                break;
            case Key.Home:
                MoveCaret(LineEdge(_caret, start: true), shift);
                break;
            case Key.End:
                MoveCaret(LineEdge(_caret, start: false), shift);
                break;
            case Key.Enter:
                PushUndo();
                if (HasSelection) DeleteSelection();
                _caret = _anchor = _doc.SplitParagraph(_caret);
                AfterEdit();
                break;
            case Key.Back:
                PushUndo(typing: !HasSelection && !ctrl);
                if (HasSelection) DeleteSelection();
                else
                {
                    var prev = ctrl ? PrevWordPos(_caret) : _doc.Move(_caret, -1);   // Ctrl+Backspace = delete word
                    if (prev != _caret) { _doc.DeleteRange(prev, _caret); _caret = _anchor = prev; }
                }
                AfterEdit();
                break;
            case Key.Delete:
                PushUndo(typing: !HasSelection && !ctrl);
                if (HasSelection) DeleteSelection();
                else
                {
                    var next = ctrl ? NextWordPos(_caret) : _doc.Move(_caret, 1);    // Ctrl+Delete = delete next word
                    if (next != _caret) _doc.DeleteRange(_caret, next);
                }
                AfterEdit();
                break;
            case Key.A when ctrl:
                _anchor = new DocPos(0, 0); _caret = _doc.End;
                InvalidateVisual();
                break;
            case Key.B when ctrl:
                ToggleFormat(isBold: true);
                break;
            case Key.I when ctrl:
                ToggleFormat(isBold: false);
                break;
            case Key.Z when ctrl:
                Undo(); AfterEdit(pushedUndo: false);
                break;
            case Key.Y when ctrl:
                Redo(); AfterEdit(pushedUndo: false);
                break;
            case Key.C when ctrl:
                _ = CopyAsync(cut: false);
                break;
            case Key.X when ctrl:
                _ = CopyAsync(cut: true);
                break;
            case Key.V when ctrl:
                _ = PasteAsync();
                break;
            default:
                handled = false;
                break;
        }
        if (handled) e.Handled = true;
    }

    /// <summary>Toggle bold/italic on the selection, or set the pending format for the next typed text.</summary>
    public void ToggleFormat(bool isBold)
    {
        if (HasSelection)
        {
            PushUndo();
            var (a, b) = SelOrdered();
            bool all = _doc.RangeAll(a, b, r => isBold ? r.Bold : r.Italic);
            _doc.ApplyFormat(a, b, r => { if (isBold) r.Bold = !all; else r.Italic = !all; });
            _typingBurst = false;
            InvalidateVisual();
        }
        else
        {
            var cur = _hasPending ? (_pendingBold, _pendingItalic) : _doc.FormatAt(_caret);
            (_pendingBold, _pendingItalic) = isBold ? (!cur.Item1, cur.Item2) : (cur.Item1, !cur.Item2);
            _hasPending = true;
        }
    }

    private void MoveCaret(DocPos to, bool extend)
    {
        _doc.Clamp(ref to);
        _caret = to;
        if (!extend) _anchor = to;
        _typingBurst = false;
        _hasPending = false;
        _desiredX = -1;
        ResetBlink();
        BringCaretIntoView();
        InvalidateVisual();
    }

    private void MoveCaretVertical(int dir, bool extend)
    {
        EnsureLayouts(Bounds.Width);
        var rect = CaretRect();
        if (_desiredX < 0) _desiredX = rect.X;
        double targetY = dir > 0 ? rect.Bottom + rect.Height * 0.5 : rect.Y - rect.Height * 0.5;
        var to = PosFromPoint(new Point(_desiredX, targetY));
        double keepX = _desiredX;
        MoveCaret(to, extend);
        _desiredX = keepX;                       // MoveCaret resets it; up/down keeps the sticky column
    }

    private DocPos LineEdge(DocPos p, bool start)
    {
        EnsureLayouts(Bounds.Width);
        _doc.Clamp(ref p);
        var layout = _layouts[p.Para];
        foreach (var line in layout.TextLines)
        {
            int ls = line.FirstTextSourceIndex, le = ls + line.Length;
            if (p.Off >= ls && (p.Off < le || le == layout.TextLines[^1].FirstTextSourceIndex + layout.TextLines[^1].Length))
            {
                if (p.Off <= le)
                    return p with { Off = start ? ls : Math.Max(ls, le - line.NewLineLength) };
            }
        }
        return p with { Off = start ? 0 : _doc.Paragraphs[p.Para].Length };
    }

    private void AfterEdit(bool pushedUndo = true)
    {
        _ = pushedUndo;
        _hasPending = _hasPending && !HasSelection;
        _desiredX = -1;
        ResetBlink();
        BringCaretIntoView();
        InvalidateVisual();
    }

    private void ResetBlink()
    {
        _caretVisible = true;
        if (IsFocused) { _blink.Stop(); _blink.Start(); }
    }

    private void BringCaretIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureLayouts(Bounds.Width);
            var r = CaretRect().Inflate(new Thickness(0, 12));
            // Skip the ScrollViewer round-trip entirely when the caret is already visible — poking it on
            // every keystroke costs a layout pass even when nothing needs to scroll.
            if (this.FindAncestorOfType<ScrollViewer>() is { } sv)
            {
                var visible = new Rect(sv.Offset.X, sv.Offset.Y, sv.Viewport.Width, sv.Viewport.Height);
                if (visible.Contains(r)) return;
            }
            this.BringIntoView(r);
        }, DispatcherPriority.Background);
    }

    // =========================== clipboard ===========================

    private string SelectedText()
    {
        var (a, b) = SelOrdered();
        if (a == b) return "";
        var sb = new StringBuilder();
        for (int pi = a.Para; pi <= b.Para; pi++)
        {
            var text = _doc.Paragraphs[pi].Text;
            int s = pi == a.Para ? a.Off : 0;
            int e = pi == b.Para ? b.Off : text.Length;
            if (pi > a.Para) sb.Append('\n');
            sb.Append(text, s, e - s);
        }
        return sb.ToString();
    }

    private async System.Threading.Tasks.Task CopyAsync(bool cut)
    {
        try
        {
            var text = SelectedText();
            if (text.Length == 0) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(text);
            if (cut)
            {
                PushUndo();
                DeleteSelection();
                AfterEdit();
            }
        }
        catch { }
    }

    private async System.Threading.Tasks.Task PasteAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            PushUndo();
            if (HasSelection) DeleteSelection();
            var (bold, italic) = _doc.FormatAt(_caret);
            _caret = _anchor = _doc.InsertText(_caret, text, bold, italic);
            AfterEdit();
        }
        catch { }
    }

    // =========================== mouse ===========================

    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var pos = PosFromPoint(pt);

        if (e.ClickCount == 2) { SelectWordAt(pos); }
        else if (e.ClickCount >= 3)
        {
            _anchor = new DocPos(pos.Para, 0);
            _caret = new DocPos(pos.Para, _doc.Paragraphs[pos.Para].Length);
        }
        else
        {
            _caret = pos;
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _anchor = pos;
            _dragging = true;
            e.Pointer.Capture(this);
        }
        _typingBurst = false;
        _hasPending = false;
        _desiredX = -1;
        ResetBlink();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        _caret = PosFromPoint(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) { _dragging = false; e.Pointer.Capture(null); }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Start of the previous word (Windows convention): skip whitespace, then the run of
    /// same-class characters. At paragraph start, crosses to the previous paragraph's end.</summary>
    private DocPos PrevWordPos(DocPos p)
    {
        _doc.Clamp(ref p);
        if (p.Off == 0) return _doc.Move(p, -1);
        var text = _doc.Paragraphs[p.Para].Text;
        int i = p.Off;
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        if (i > 0)
        {
            bool word = IsWordChar(text[i - 1]);
            while (i > 0 && !char.IsWhiteSpace(text[i - 1]) && IsWordChar(text[i - 1]) == word) i--;
        }
        return p with { Off = i };
    }

    /// <summary>Start of the next word: skip the current run of same-class characters, then whitespace.
    /// At paragraph end, crosses to the next paragraph's start.</summary>
    private DocPos NextWordPos(DocPos p)
    {
        _doc.Clamp(ref p);
        var text = _doc.Paragraphs[p.Para].Text;
        if (p.Off >= text.Length) return _doc.Move(p, 1);
        int i = p.Off;
        if (!char.IsWhiteSpace(text[i]))
        {
            bool word = IsWordChar(text[i]);
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && IsWordChar(text[i]) == word) i++;
        }
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return p with { Off = i };
    }

    private void SelectWordAt(DocPos pos)
    {
        _doc.Clamp(ref pos);
        var text = _doc.Paragraphs[pos.Para].Text;
        if (text.Length == 0) { _anchor = _caret = pos; return; }
        int i = Math.Min(pos.Off, text.Length - 1);
        static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        if (!WordChar(text[i]) && i > 0 && WordChar(text[i - 1])) i--;
        int s = i, e2 = i;
        while (s > 0 && WordChar(text[s - 1])) s--;
        while (e2 < text.Length && WordChar(text[e2])) e2++;
        _anchor = new DocPos(pos.Para, s);
        _caret = new DocPos(pos.Para, e2);
    }
}
