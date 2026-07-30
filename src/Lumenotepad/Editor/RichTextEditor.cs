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

public sealed class RichTextEditor : Control
{

    public FontFamily FontFamily { get; set; } = new("Segoe UI Variable Text, Segoe UI");
    public double FontSize { get; set; } = 15;
    public IBrush Foreground { get; set; } = Brushes.White;
    public IBrush SelectionBrush { get; set; } = new SolidColorBrush(Color.Parse("#554DA6FF"));
    public IBrush CaretBrush { get; set; } = new SolidColorBrush(Color.Parse("#4DA6FF"));

    public IBrush LinkBrush { get; set; } = new SolidColorBrush(Color.Parse("#4DA6FF"));
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

    private DocPos _caret, _anchor;
    private double _desiredX = -1;
    private RunFormat _pending;
    private bool _hasPending;

    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Rect _caretDisplay;
    private bool _caretSeeded;
    private double _caretOpacity = 1, _blinkMs;

    private readonly Dictionary<int, (double P, bool On)> _checkAnim = new();

    public event Action? SelectionChanged;
    private void RaiseSelectionChanged() => SelectionChanged?.Invoke();

    private readonly Stack<(DocSnapshot Snap, DocPos Caret, DocPos Anchor)> _undo = new();
    private readonly Stack<(DocSnapshot Snap, DocPos Caret, DocPos Anchor)> _redo = new();
    private bool _typingBurst;

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
        _anim.Tick += (_, _) => AnimTick();
        GotFocus += (_, _) => { ResetBlink(); _caretSeeded = false; _anim.Start(); InvalidateVisual(); };
        LostFocus += (_, _) => { _anim.Stop(); _checkAnim.Clear(); InvalidateVisual(); };
    }

    private void OnDocChanged()
    {

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
                layout = e.Layout;
            else
            {
                if (_cache.TryGetValue(p, out var stale)) stale.Layout.Dispose();
                layout = BuildLayout(p, w);
            }
            _cache.Remove(p);
            next[p] = (layout, p.Version, w);
            _layouts.Add(layout);
        }
        foreach (var orphan in _cache.Values) orphan.Layout.Dispose();
        _cache.Clear();
        foreach (var kv in next) _cache[kv.Key] = kv.Value;

        if (_tops.Length != _layouts.Count) _tops = new double[_layouts.Count];
        double y = 0;
        for (int i = 0; i < _layouts.Count; i++)
        {
            _tops[i] = y;
            y += _layouts[i].Height + ParagraphSpacing;
        }
        _contentHeight = _layouts.Count > 0 ? y - ParagraphSpacing : 0;
    }

    private const double BulletIndent = 26;

    private const double TagIndent = 22;

    private double ScaleOf(Paragraph p)
    {
        double eff = p.Runs.Count > 0 ? p.Runs[0].Size ?? FontSize : FontSize;
        return Math.Clamp(eff / FontSize, 0.75, 2.5);
    }

    private double BulletIndentOf(Paragraph p) =>
        p.Bullet is null ? 0 : BulletIndent * IndentScalePref * Math.Max(1, ScaleOf(p));
    private double TagIndentOf(Paragraph p) =>
        p.Tag is null ? 0 : TagIndent * Math.Max(1, ScaleOf(p));

    private double IndentOf(Paragraph p) => TagIndentOf(p) + BulletIndentOf(p);

    private double IndentOf(int pi) =>
        pi >= 0 && pi < _doc.Paragraphs.Count ? IndentOf(_doc.Paragraphs[pi]) : 0;

    public static double BaseSizeFor(ParaStyle s, double bodySize) => s switch
    {
        ParaStyle.Title => bodySize * 2.0,
        ParaStyle.Subtitle => bodySize * 1.35,
        ParaStyle.Heading1 => bodySize * 1.7,
        ParaStyle.Heading2 => bodySize * 1.4,
        ParaStyle.Heading3 => bodySize * 1.18,
        _ => bodySize,
    };

    public static FontWeight BaseWeightFor(ParaStyle s) => s switch
    {
        ParaStyle.Title => FontWeight.Bold,
        ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3 => FontWeight.SemiBold,
        _ => FontWeight.Normal,
    };

    private bool _forceCenter;
    public bool ForceCenter
    {
        get => _forceCenter;
        set { if (_forceCenter == value) return; _forceCenter = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    private bool _forceBold;
    public bool ForceBold
    {
        get => _forceBold;
        set { if (_forceBold == value) return; _forceBold = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    private static TextAlignment MapAlign(TextAlign a) => a switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private TextLayout BuildLayout(Paragraph p, double width)
    {
        if ((p.Bullet is not null || p.Tag is not null) && double.IsFinite(width)) width -= IndentOf(p);
        double maxWidth = double.IsFinite(width) && width > 1 ? width : double.PositiveInfinity;
        double baseSize = p.Footnote ? Math.Max(9, FontSize * 0.82) : BaseSizeFor(p.Style, FontSize);
        var align = _forceCenter ? TextAlignment.Center : MapAlign(p.Align);
        var baseW = _forceBold ? FontWeight.Bold : BaseWeightFor(p.Style);
        if (p.Runs.Count == 0)
            return new TextLayout("", new Typeface(FontFamily, weight: baseW), baseSize, Foreground,
                                  align, TextWrapping.Wrap, maxWidth: maxWidth);

        var defaultProps = new GenericTextRunProperties(
            new Typeface(FontFamily, weight: baseW), baseSize, foregroundBrush: Foreground);
        double lineHeight = double.NaN;
        if (LineSpacingScalePref > 1.005 && p.Runs.Count > 0)
            lineHeight = p.Runs.Max(r => r.Size ?? baseSize) * 1.35 * LineSpacingScalePref;
        var paraProps = new GenericTextParagraphProperties(
            FlowDirection.LeftToRight, align, true, false,
            defaultProps, TextWrapping.Wrap, lineHeight, 0, 0);
        return new TextLayout(new RunsTextSource(p, this), paraProps, maxWidth: maxWidth);
    }

    private sealed class RunsTextSource : ITextSource
    {
        private readonly Paragraph _p;
        private readonly RichTextEditor _e;
        public RunsTextSource(Paragraph p, RichTextEditor e) { _p = p; _e = e; }

        public TextRun? GetTextRun(int index)
        {
            double baseSize = _p.Footnote ? Math.Max(9, _e.FontSize * 0.82) : BaseSizeFor(_p.Style, _e.FontSize);
            var baseWeight = _e._forceBold ? FontWeight.Bold : (_p.Footnote ? FontWeight.Normal : BaseWeightFor(_p.Style));
            int acc = 0;
            foreach (var r in _p.Runs)
            {
                int end = acc + r.Text.Length;
                if (index < end)
                {
                    var typeface = new Typeface(
                        r.Font is { Length: > 0 } f ? Services.AppFonts.Family(f) : _e.FontFamily,
                        r.Italic ? FontStyle.Italic : FontStyle.Normal,
                        r.Bold ? FontWeight.Bold : baseWeight);
                    bool link = r.Link is { Length: > 0 };

                    bool underline = r.Underline || link;
                    TextDecorationCollection? deco =
                        (underline, r.Strike) switch
                        {
                            (true, true) => new TextDecorationCollection(
                                TextDecorations.Underline.Concat(TextDecorations.Strikethrough)),
                            (true, false) => TextDecorations.Underline,
                            (false, true) => TextDecorations.Strikethrough,
                            _ => null,
                        };

                    double size = r.Size ?? baseSize;
                    if (r.Baseline != Baseline.Normal) size *= 0.72;
                    var baseline = r.Baseline switch
                    {
                        Baseline.Super => BaselineAlignment.Superscript,
                        Baseline.Sub => BaselineAlignment.Subscript,
                        _ => BaselineAlignment.Baseline,
                    };
                    var fg = link ? _e.LinkBrush : BrushFor(r.Color) ?? _e.Foreground;
                    var props = new GenericTextRunProperties(typeface, size, textDecorations: deco,
                        foregroundBrush: fg, baselineAlignment: baseline);
                    return new TextCharacters(r.Text.Substring(index - acc), props);
                }
                acc = end;
            }
            return null;
        }
    }

    private static readonly Dictionary<string, IBrush> _brushCache = new();

    private static IBrush? BrushFor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (_brushCache.TryGetValue(hex, out var b)) return b;
        try { b = new SolidColorBrush(Color.Parse(hex)); }
        catch { return null; }
        _brushCache[hex] = b;
        return b;
    }

    private double ParagraphTop(int para) => para >= 0 && para < _tops.Length ? _tops[para] : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayouts(availableSize.Width);
        return new Size(availableSize.Width, _contentHeight + 4);
    }

    public override void Render(DrawingContext ctx)
    {

        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        EnsureLayouts(Bounds.Width);

        for (int pi = 0; pi < _layouts.Count && pi < _doc.Paragraphs.Count; pi++)
        {
            int acc = 0;
            double top = ParagraphTop(pi);
            foreach (var run in _doc.Paragraphs[pi].Runs)
            {
                int len = run.Text.Length;
                if (run.Highlight is { } hl && BrushFor(hl) is { } hlBrush && len > 0)
                {
                    foreach (var r in _layouts[pi].HitTestTextRange(acc, len))
                        ctx.FillRectangle(hlBrush, new Rect(r.X + IndentOf(pi), r.Y + top, r.Width, r.Height), 3f);
                }
                acc += len;
            }
        }

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
                        ctx.FillRectangle(SelectionBrush, new Rect(r.X + IndentOf(pi), r.Y + top, Math.Max(r.Width, 2), r.Height), 3f);
                }
                else if (_doc.Paragraphs[pi].Length == 0)
                {

                    ctx.FillRectangle(SelectionBrush, new Rect(IndentOf(pi), top, 6, _layouts[pi].Height), 3f);
                }
            }
        }

        for (int i = 0; i < _layouts.Count; i++)
        {

            if (i < _doc.Paragraphs.Count && _doc.Paragraphs[i].Footnote &&
                (i == 0 || !_doc.Paragraphs[i - 1].Footnote))
            {
                double dy = ParagraphTop(i) - ParagraphSpacing * 0.5;
                var pen = new Pen(new SolidColorBrush(Color.Parse(
                    Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x33))), 1);
                ctx.DrawLine(pen, new Point(0, dy), new Point(Math.Min(180, Bounds.Width), dy));
            }
            DrawBullet(ctx, i, ParagraphTop(i));
            _layouts[i].Draw(ctx, new Point(IndentOf(i), ParagraphTop(i)));
        }

        if (IsFocused && _caretOpacity > 0.01)
        {
            var r = _caretSeeded ? _caretDisplay : CaretRect();
            using (ctx.PushOpacity(_caretOpacity))
                ctx.FillRectangle(CaretBrush, new Rect(r.X, r.Y, CaretWidthPref, r.Height), 0.8f);
        }
    }

    public static readonly Dictionary<string, string> BulletColorOverrides = new();

    public static bool? NumBoldDefault, NumItalicDefault, NumUnderlineDefault, NumStrikeDefault;

    public static bool NumFlag(bool? para, bool? global, bool run) => para ?? global ?? run;

    public static string? CaretColorOverride;
    public static double CaretWidthPref = 1.6;
    public static bool CaretBlinkPref = true;

    public static string DefaultHighlightPref = "#66FFD666";

    public static string DateFormatPref = "yyyy-MM-dd";

    public static double NewNoteWidthPref = 360;

    public static string? EditorFontPref;
    public static double EditorFontSizePref = 15;

    public static double LineSpacingScalePref = 1.0;
    public static double ParagraphSpacingScalePref = 1.0;
    public static double IndentScalePref = 1.0;

    public static bool SmartListsPref = true;

    public static string? SmartListKind(string beforeCaret) => beforeCaret switch
    {
        "1." => "num",
        "-" or "*" => "dot",
        _ => null,
    };

    public static (string Glyph, string Color)? BulletGlyphInfo(string style) =>
        BulletGlyphs.TryGetValue(style, out var g) ? g : null;

    private static readonly Dictionary<string, (string Glyph, string Color)> BulletGlyphs = new()
    {
        ["dot"] = ("●", "#4DA6FF"),
        ["arrow"] = ("➤", "#4DA6FF"),
        ["star"] = ("★", "#E9B865"),
        ["heart"] = ("♥", "#E27BA6"),
        ["flower"] = ("✿", "#7FD1A6"),
        ["spark"] = ("✦", "#FFD966"),
    };

    private static readonly FontFamily GlyphFont = new("Segoe UI Symbol, Segoe UI Emoji, Segoe UI");

    private readonly record struct GlyphCal(double UnitSize, double CenterXFrac, double CenterYFrac);
    private static readonly Dictionary<string, GlyphCal> _glyphCal = new();

    private static GlyphCal Calibrate(string glyph)
    {
        if (_glyphCal.TryGetValue(glyph, out var c)) return c;
        const double probe = 24.0;
        var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(GlyphFont), probe, Brushes.Black);
        var b = ft.BuildGeometry(new Point(0, 0))?.Bounds ?? new Rect(0, 0, ft.Width, ft.Height);
        double h = b.Height > 0.1 ? b.Height : probe;
        c = new GlyphCal(probe / h, b.Center.X / probe, b.Center.Y / probe);
        _glyphCal[glyph] = c;
        return c;
    }

    private IBrush ForegroundAlpha(double opacity)
    {
        var c = (Foreground as ISolidColorBrush)?.Color ?? Colors.White;
        return new SolidColorBrush(c, opacity);
    }

    private void DrawBullet(DrawingContext ctx, int pi, double top)
    {
        var p = _doc.Paragraphs[pi];
        if (p.Bullet is null && p.Tag is null) return;
        double s = ScaleOf(p);
        double lineH = _layouts[pi].TextLines.Count > 0 ? _layouts[pi].TextLines[0].Height : FontSize * 1.35;
        double cy = top + lineH / 2;

        if (p.Tag is not null && TagStyles.Info(p.Tag) is { } tg)
        {
            double tInk = 8.8 * s;
            var tCal = Calibrate(tg.Glyph);
            double tSize = tCal.UnitSize * tInk;
            var tf = new FormattedText(tg.Glyph, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(GlyphFont, FontStyle.Normal, FontWeight.Bold),
                tSize, BrushFor(tg.Color));
            double tcx = TagIndentOf(p) / 2 - 1;
            ctx.DrawText(tf, new Point(tcx - tCal.CenterXFrac * tSize, cy - tCal.CenterYFrac * tSize));
        }

        if (p.Bullet is null) return;
        double indent = BulletIndentOf(p);
        double tagInd = TagIndentOf(p);
        double cx = tagInd + indent / 2 - 2 * s;

        if (p.Bullet == "check")
        {

            bool anim = _checkAnim.TryGetValue(pi, out var ca);
            double a = anim ? (ca.On ? ca.P : 1 - ca.P) : (p.Checked ? 1 : 0);
            double pop = anim ? 1 + 0.18 * Math.Sin(Math.PI * ca.P) : 1;
            double half = 7.5 * s * pop, r = 4 * s;
            var box = new Rect(cx - half, cy - half, half * 2, half * 2);
            if (a < 0.999)
                ctx.DrawRectangle(null, new Pen(ForegroundAlpha(0.45 * (1 - a)), 1.5 * s), box, r, r);
            if (a > 0.001)
            {
                var fill = CaretBrush is ISolidColorBrush cb ? new SolidColorBrush(cb.Color, a) : CaretBrush;
                ctx.DrawRectangle(fill, null, box, r, r);
                var pen = new Pen(new SolidColorBrush(Colors.White, a), 1.8 * s) { LineCap = PenLineCap.Round };
                double m = a * pop;
                Point P(double dx, double dy) => new(cx + dx * s * m, cy + dy * s * m);
                ctx.DrawLine(pen, P(-4, 0.5), P(-1, 3.5));
                ctx.DrawLine(pen, P(-1, 3.5), P(4.5, -3));
            }
            return;
        }

        if (p.Bullet == "num")
        {
            int n = 1;
            for (int j = pi - 1; j >= 0 && _doc.Paragraphs[j].Bullet == "num"; j--) n++;

            var fr = p.Runs.Count > 0 ? p.Runs[0] : null;
            bool bold = NumFlag(p.NumBold, NumBoldDefault, fr?.Bold ?? false);
            bool italic = NumFlag(p.NumItalic, NumItalicDefault, fr?.Italic ?? false);
            bool under = NumFlag(p.NumUnderline, NumUnderlineDefault, fr?.Underline ?? false);
            bool strike = NumFlag(p.NumStrike, NumStrikeDefault, fr?.Strike ?? false);
            var brush = ForegroundAlpha(0.8);
            var ft = new FormattedText($"{n}.", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, italic ? FontStyle.Italic : FontStyle.Normal,
                             bold ? FontWeight.Bold : FontWeight.Normal),
                12.5 * s, brush);
            var origin = new Point(tagInd + indent - 7 - ft.Width, cy - ft.Height / 2);
            ctx.DrawText(ft, origin);
            var decoPen = new Pen(brush, Math.Max(1, s));
            if (under)
                ctx.DrawLine(decoPen, new Point(origin.X, origin.Y + ft.Height * 0.92),
                                      new Point(origin.X + ft.Width, origin.Y + ft.Height * 0.92));
            if (strike)
                ctx.DrawLine(decoPen, new Point(origin.X, origin.Y + ft.Height * 0.55),
                                      new Point(origin.X + ft.Width, origin.Y + ft.Height * 0.55));
            return;
        }

        if (BulletGlyphs.TryGetValue(p.Bullet, out var g))
        {
            double targetInk = 7.6 * s;
            var cal = Calibrate(g.Glyph);
            double size = cal.UnitSize * targetInk;
            var ft = new FormattedText(g.Glyph, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(GlyphFont), size,
                BrushFor(BulletColorOverrides.TryGetValue(p.Bullet, out var oc) ? oc : g.Color));

            ctx.DrawText(ft, new Point(cx - cal.CenterXFrac * size, cy - cal.CenterYFrac * size));
        }
    }

    private Rect CaretRect()
    {
        var p = _caret;
        _doc.Clamp(ref p);
        if (p.Para >= _layouts.Count) return new Rect(0, 0, CaretWidthPref, FontSize * 1.35);
        var r = _layouts[p.Para].HitTestTextPosition(p.Off);
        return new Rect(r.X + IndentOf(p.Para), r.Y + ParagraphTop(p.Para), r.Width, r.Height <= 0 ? FontSize * 1.35 : r.Height);
    }

    private (DocPos a, DocPos b) SelOrdered() => _anchor <= _caret ? (_anchor, _caret) : (_caret, _anchor);
    public bool HasSelection => _anchor != _caret;

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
                var hit = _layouts[i].HitTestPoint(new Point(Math.Max(0, pt.X - IndentOf(i)), Math.Max(0, pt.Y - y)));
                return new DocPos(i, hit.TextPosition);
            }
            y += h;
        }
        return _doc.End;
    }

    private void PushUndo(bool typing = false)
    {
        if (typing && _typingBurst) return;
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

        var fmt = _hasPending ? _pending : _doc.FormatAt(_caret);
        _caret = _anchor = _doc.InsertText(_caret, text, fmt);
        _hasPending = false;

        if (SmartListsPref && text == " " && _caret.Off >= 2)
        {
            var para = _doc.Paragraphs[_caret.Para];
            if (para.Bullet is null && SmartListKind(para.Text[..(_caret.Off - 1)]) is { } kind)
            {
                PushUndo();
                _doc.DeleteRange(new DocPos(_caret.Para, 0), _caret);
                _caret = _anchor = new DocPos(_caret.Para, 0);
                _doc.SetBullet(_caret, _caret, kind);
            }
        }

        AfterEdit();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool cmd = Services.Keymap.HasCommand(e.KeyModifiers);
        bool ctrl = OperatingSystem.IsMacOS() ? e.KeyModifiers.HasFlag(KeyModifiers.Alt) : cmd;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (HandleKeymapShortcut(e)) { e.Handled = true; return; }

        bool handled = true;

        switch (e.Key)
        {

            case Key.Left or Key.Right when cmd && OperatingSystem.IsMacOS():
                MoveCaret(LineEdge(_caret, start: e.Key == Key.Left), shift);
                break;
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

                if (_doc.Paragraphs[_caret.Para].Bullet is not null && _doc.Paragraphs[_caret.Para].Length == 0)
                    _doc.SetBullet(_caret, _caret, null);
                else
                    _caret = _anchor = _doc.SplitParagraph(_caret);
                AfterEdit();
                break;
            case Key.Back:
                PushUndo(typing: !HasSelection && !ctrl);
                if (HasSelection) DeleteSelection();
                else if (_caret.Off == 0 && _doc.Paragraphs[_caret.Para].Bullet is not null)
                {
                    _doc.SetBullet(_caret, _caret, null);
                }
                else
                {
                    var prev = ctrl ? PrevWordPos(_caret) : _doc.Move(_caret, -1);
                    if (prev != _caret) { _doc.DeleteRange(prev, _caret); _caret = _anchor = prev; }
                }
                AfterEdit();
                break;
            case Key.Delete:
                PushUndo(typing: !HasSelection && !ctrl);
                if (HasSelection) DeleteSelection();
                else
                {
                    var next = ctrl ? NextWordPos(_caret) : _doc.Move(_caret, 1);
                    if (next != _caret) _doc.DeleteRange(_caret, next);
                }
                AfterEdit();
                break;
            case Key.A when cmd:
                _anchor = new DocPos(0, 0); _caret = _doc.End;
                InvalidateVisual();
                RaiseSelectionChanged();
                break;

            case Key.Z when cmd && shift:
                Redo(); AfterEdit(pushedUndo: false);
                break;
            case Key.Z when cmd:
                Undo(); AfterEdit(pushedUndo: false);
                break;
            case Key.Y when cmd:
                Redo(); AfterEdit(pushedUndo: false);
                break;
            case Key.C when cmd:
                _ = CopyAsync(cut: false);
                break;
            case Key.X when cmd:
                _ = CopyAsync(cut: true);
                break;
            case Key.V when cmd:
                _ = PasteAsync();
                break;
            default:
                handled = false;
                break;
        }
        if (handled) e.Handled = true;
    }

    private bool HandleKeymapShortcut(KeyEventArgs e)
    {
        if (Services.Keymap.Matches("bold", e)) ToggleBold();
        else if (Services.Keymap.Matches("italic", e)) ToggleItalic();
        else if (Services.Keymap.Matches("underline", e)) ToggleUnderline();
        else if (Services.Keymap.Matches("strike", e)) ToggleStrike();
        else if (Services.Keymap.Matches("highlight", e)) ToggleDefaultHighlight();
        else if (Services.Keymap.Matches("date", e)) InsertDateTime();
        else if (Services.Keymap.Matches("bullets", e)) ApplyBullet("dot");
        else if (Services.Keymap.Matches("numbers", e)) ApplyBullet("num");
        else return false;
        return true;
    }

    public RunFormat CurrentFormat
    {
        get
        {
            if (_hasPending && !HasSelection) return _pending;
            if (!HasSelection) return _doc.FormatAt(_caret);
            var (a, b) = SelOrdered();
            var f = _doc.FormatAt(_doc.Move(a, 1));
            return f with
            {
                Bold = _doc.RangeAll(a, b, r => r.Bold),
                Italic = _doc.RangeAll(a, b, r => r.Italic),
                Underline = _doc.RangeAll(a, b, r => r.Underline),
                Strike = _doc.RangeAll(a, b, r => r.Strike),
            };
        }
    }

    public string? CurrentBullet
    {
        get
        {
            var (a, _) = SelOrdered();
            _doc.Clamp(ref a);
            return _doc.Paragraphs[a.Para].Bullet;
        }
    }

    public void ApplyBullet(string? style)
    {
        PushUndo();
        var (a, b) = SelOrdered();
        bool clear = style is null || _doc.BulletAll(a, b, style);
        _doc.SetBullet(a, b, clear ? null : style);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public (bool Bold, bool Italic, bool Underline, bool Strike)? CurrentNumStyle
    {
        get
        {
            var (a, _) = SelOrdered();
            _doc.Clamp(ref a);
            var p = _doc.Paragraphs[a.Para];
            if (p.Bullet != "num") return null;
            var fr = p.Runs.Count > 0 ? p.Runs[0] : null;
            return (NumFlag(p.NumBold, NumBoldDefault, fr?.Bold ?? false),
                    NumFlag(p.NumItalic, NumItalicDefault, fr?.Italic ?? false),
                    NumFlag(p.NumUnderline, NumUnderlineDefault, fr?.Underline ?? false),
                    NumFlag(p.NumStrike, NumStrikeDefault, fr?.Strike ?? false));
        }
    }

    public void ToggleNumStyle(char flag)
    {
        if (CurrentNumStyle is not { } cur) return;
        var (a, _) = SelOrdered();
        _doc.Clamp(ref a);
        bool eff = flag switch { 'b' => cur.Bold, 'i' => cur.Italic, 'u' => cur.Underline, _ => cur.Strike };
        PushUndo();
        _doc.SetNumFlag(a.Para, flag, !eff);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public void ClearNumStyle()
    {
        if (CurrentBullet != "num") return;
        var (a, _) = SelOrdered();
        _doc.Clamp(ref a);
        PushUndo();
        foreach (char f in "bius") _doc.SetNumFlag(a.Para, f, null);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public Baseline CurrentBaseline =>
        _hasPending && !HasSelection ? _pending.Baseline : CurrentFormat.Baseline;

    public void ToggleSuper() => ToggleBaseline(Baseline.Super);
    public void ToggleSub() => ToggleBaseline(Baseline.Sub);

    private void ToggleBaseline(Baseline target)
    {
        var next = CurrentBaseline == target ? Baseline.Normal : target;
        ApplyValue(r => r.Baseline = next, f => f with { Baseline = next });
    }

    public TextAlign CurrentAlign
    {
        get { var (a, _) = SelOrdered(); _doc.Clamp(ref a); return _doc.Paragraphs[a.Para].Align; }
    }

    public ParaStyle CurrentTextType
    {
        get { var (a, _) = SelOrdered(); _doc.Clamp(ref a); return _doc.Paragraphs[a.Para].Style; }
    }

    public void SetAlignment(TextAlign align)
    {
        PushUndo();
        var (a, b) = SelOrdered();
        _doc.SetAlign(a, b, align);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public void SetTag(string? tag)
    {
        PushUndo();
        var (a, b) = SelOrdered();
        _doc.SetTag(a, b, tag);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public void SetTextType(ParaStyle style)
    {
        PushUndo();
        var (a, b) = SelOrdered();
        _doc.SetParaStyle(a, b, style);
        _typingBurst = false;
        RaiseSelectionChanged();
    }

    public string? CurrentLink => CurrentFormat.Link;

    public void ApplyLink(string? url)
    {
        string? u = string.IsNullOrWhiteSpace(url) ? null : url!.Trim();
        ApplyValue(r => r.Link = u, f => f with { Link = u });
    }

    public void InsertLink(string text, string url)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(url)) return;
        PushUndo();
        if (HasSelection) DeleteSelection();
        var fmt = _doc.FormatAt(_caret) with { Link = url.Trim(), Underline = true };
        _caret = _anchor = _doc.InsertText(_caret, text, fmt);
        AfterEdit();
    }

    public void InsertFootnote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        PushUndo();
        if (HasSelection) DeleteSelection();
        _caret = _anchor = _doc.InsertFootnote(_caret, text.Trim());
        AfterEdit();
    }

    public void ToggleBold() => ToggleFlag(r => r.Bold, (r, v) => r.Bold = v, f => f with { Bold = !f.Bold });
    public void ToggleItalic() => ToggleFlag(r => r.Italic, (r, v) => r.Italic = v, f => f with { Italic = !f.Italic });
    public void ToggleUnderline() => ToggleFlag(r => r.Underline, (r, v) => r.Underline = v, f => f with { Underline = !f.Underline });
    public void ToggleStrike() => ToggleFlag(r => r.Strike, (r, v) => r.Strike = v, f => f with { Strike = !f.Strike });

    public void ApplyHighlight(string? hex) => ApplyValue(r => r.Highlight = hex, f => f with { Highlight = hex });

    public void ApplyColor(string? hex) => ApplyValue(r => r.Color = hex, f => f with { Color = hex });

    public void ApplySize(double? size) => ApplyValue(r => r.Size = size, f => f with { Size = size });

    public void ApplyFont(string? family) => ApplyValue(r => r.Font = family, f => f with { Font = family });

    public void ToggleDefaultHighlight()
    {
        if (HasSelection)
        {
            var (a, b) = SelOrdered();
            ApplyHighlight(_doc.RangeAll(a, b, r => r.Highlight is not null) ? null : DefaultHighlightPref);
        }
        else
        {
            var cur = _hasPending ? _pending : _doc.FormatAt(_caret);
            ApplyHighlight(cur.Highlight is null ? DefaultHighlightPref : null);
        }
    }

    public void InsertDateTime()
    {
        string stamp;
        try { stamp = System.DateTime.Now.ToString(DateFormatPref); }
        catch (FormatException) { stamp = System.DateTime.Now.ToString("yyyy-MM-dd"); }
        InsertPlainText(stamp);
    }

    private void InsertPlainText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        PushUndo();
        if (HasSelection) DeleteSelection();
        _caret = _anchor = _doc.InsertText(_caret, text, _doc.FormatAt(_caret));
        AfterEdit();
    }

    private void ToggleFlag(Func<RichRun, bool> get, Action<RichRun, bool> set, Func<RunFormat, RunFormat> flipPending)
    {
        if (HasSelection)
        {
            PushUndo();
            var (a, b) = SelOrdered();
            bool all = _doc.RangeAll(a, b, get);
            _doc.ApplyFormat(a, b, r => set(r, !all));
            _typingBurst = false;
            InvalidateVisual();
        }
        else
        {
            _pending = flipPending(_hasPending ? _pending : _doc.FormatAt(_caret));
            _hasPending = true;
        }
        RaiseSelectionChanged();
    }

    private void ApplyValue(Action<RichRun> mutate, Func<RunFormat, RunFormat> setPending)
    {
        if (HasSelection)
        {
            PushUndo();
            var (a, b) = SelOrdered();
            _doc.ApplyFormat(a, b, mutate);
            _typingBurst = false;
            InvalidateVisual();
        }
        else
        {
            _pending = setPending(_hasPending ? _pending : _doc.FormatAt(_caret));
            _hasPending = true;
        }
        RaiseSelectionChanged();
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
        RaiseSelectionChanged();
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
        _desiredX = keepX;
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
        RaiseSelectionChanged();
    }

    private void ResetBlink()
    {
        _blinkMs = 0;
        _caretOpacity = 1;
    }

    private void AnimTick()
    {
        bool dirty = false;
        var target = CaretRect();
        if (!_caretSeeded) { _caretDisplay = target; _caretSeeded = true; dirty = true; }
        var d = _caretDisplay;
        bool gliding = Math.Abs(d.X - target.X) + Math.Abs(d.Y - target.Y) + Math.Abs(d.Height - target.Height) > 0.4;
        if (gliding)
        {
            const double k = 0.34;
            _caretDisplay = new Rect(AnimLerp(d.X, target.X, k), AnimLerp(d.Y, target.Y, k),
                                     AnimLerp(d.Width, target.Width, k), AnimLerp(d.Height, target.Height, k));
            dirty = true;
        }

        _blinkMs += 16;
        double op = gliding || !CaretBlinkPref ? 1.0 : BlinkOpacity(_blinkMs);
        if (Math.Abs(op - _caretOpacity) > 0.008) { _caretOpacity = op; dirty = true; }

        if (_checkAnim.Count > 0)
        {
            foreach (var key in _checkAnim.Keys.ToList())
            {
                var (p, on) = _checkAnim[key];
                p += 16.0 / 170;
                if (p >= 1) _checkAnim.Remove(key); else _checkAnim[key] = (p, on);
            }
            dirty = true;
        }

        if (dirty) InvalidateVisual();
    }

    private static double AnimLerp(double a, double b, double t) => a + (b - a) * t;
    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double BlinkOpacity(double ms)
    {
        double t = (ms % 1150) / 1150;
        if (t < 0.5) return 1;
        if (t < 0.66) return 1 - Smooth((t - 0.5) / 0.16) * 0.9;
        if (t < 0.82) return 0.1;
        return 0.1 + Smooth((t - 0.82) / 0.18) * 0.9;
    }

    private void BringCaretIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureLayouts(Bounds.Width);
            var r = CaretRect().Inflate(new Thickness(0, 12));

            if (this.FindAncestorOfType<ScrollViewer>() is { } sv)
            {
                var visible = new Rect(sv.Offset.X, sv.Offset.Y, sv.Viewport.Width, sv.Viewport.Height);
                if (visible.Contains(r)) return;
            }
            this.BringIntoView(r);
        }, DispatcherPriority.Background);
    }

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
            InsertPlainText(text);
        }
        catch { }
    }

    private bool _dragging;
    private Point _pressPoint;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _pressPoint = pt;
        var pos = PosFromPoint(pt);

        if (IsOverCheckbox(pt, out int checkPara))
        {
            PushUndo();
            _doc.ToggleChecked(checkPara);
            _checkAnim[checkPara] = (0, _doc.Paragraphs[checkPara].Checked);
            if (!_anim.IsEnabled) _anim.Start();
            e.Handled = true;
            return;
        }

        if (Services.Keymap.HasCommandStrict(e.KeyModifiers) && LinkAt(pos) is { } url)
        {
            OpenLink(url);
            e.Handled = true;
            return;
        }

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
        RaiseSelectionChanged();
        e.Handled = true;
    }

    private static readonly Cursor IbeamCursor = new(StandardCursorType.Ibeam);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private string? LinkAt(DocPos pos)
    {
        _doc.Clamp(ref pos);
        var para = _doc.Paragraphs[pos.Para];
        int acc = 0;
        foreach (var r in para.Runs)
        {
            int end = acc + r.Text.Length;
            if (pos.Off < end || (pos.Off == end && r == para.Runs[^1])) return r.Link;
            acc = end;
        }
        return null;
    }

    private static void OpenLink(string url)
    {
        var u = url.Trim();
        if (!u.Contains("://") && !u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u;
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u) { UseShellExecute = true });
        }
        catch {  }
    }

    private bool IsOverCheckbox(Point pt, out int paraIndex)
    {
        paraIndex = -1;
        var pos = PosFromPoint(pt);
        if (pos.Para >= _doc.Paragraphs.Count || _doc.Paragraphs[pos.Para].Bullet != "check") return false;
        if (pt.X > IndentOf(pos.Para)) return false;
        double top = ParagraphTop(pos.Para);
        double lineH = _layouts.Count > pos.Para && _layouts[pos.Para].TextLines.Count > 0
            ? _layouts[pos.Para].TextLines[0].Height : FontSize * 1.35;
        if (pt.Y < top || pt.Y > top + lineH) return false;
        paraIndex = pos.Para;
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        if (_dragging)
        {
            _caret = PosFromPoint(pt);
            InvalidateVisual();
            return;
        }

        Cursor = IsOverCheckbox(pt, out _) || LinkAt(PosFromPoint(pt)) is not null ? HandCursor : IbeamCursor;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) { _dragging = false; e.Pointer.Capture(null); }

        var pt = e.GetPosition(this);
        if (!HasSelection && !Services.Keymap.HasCommandStrict(e.KeyModifiers) &&
            Math.Abs(pt.X - _pressPoint.X) < 4 && Math.Abs(pt.Y - _pressPoint.Y) < 4 &&
            LinkAt(PosFromPoint(pt)) is { } url)
            OpenLink(url);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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
