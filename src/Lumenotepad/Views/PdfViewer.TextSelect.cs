using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

public partial class PdfViewer
{
    public static bool HighlightByTextPref = true;
    public static event Action<bool>? HighlightModeChanged;

    private IReadOnlyList<PdfPageText>? _pageText;
    private bool _textReady;
    private string _pageStatus = "";

    private int _selPage = -1, _selAnchor, _selFocus;
    private bool _selDragging;
    private PdfAnnotation? _pressedHighlight;
    private Point _selPressPx;

    private int _pendPage = -1, _pendAnchor, _pendFocus;
    private bool _pendDragging;
    private Point _pendPressPx;

    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);

    private IReadOnlyList<PdfChar>? CharsFor(int page) =>
        _pageText is { } t && page >= 0 && page < t.Count ? t[page].Chars : null;

    private static Point Unit(PageView pv, Point px) =>
        new(px.X / Math.Max(1, pv.Overlay.Width), px.Y / Math.Max(1, pv.Overlay.Height));

    private PageView? PageAt(int index) => index >= 0 && index < _pages.Count ? _pages[index] : null;

    private void ResetText()
    {
        _pageText = null;
        _textReady = false;
        _selPage = -1; _selDragging = false; _pressedHighlight = null;
        _pendPage = -1; _pendDragging = false;
    }

    private void SetPageText(IReadOnlyList<PdfPageText> text)
    {
        _pageText = text;
        _textReady = true;
    }

    private void Flash(string message)
    {
        StatusLabel.Text = message;
        DispatcherTimer.RunOnce(() =>
        {
            if (StatusLabel.Text == message) StatusLabel.Text = _pageStatus;
        }, TimeSpan.FromSeconds(3));
    }

    private bool HasTextSelection => _selPage >= 0 && _selAnchor != _selFocus;

    private void SetTextSelection(int page, int anchor, int focus)
    {
        _selPage = page; _selAnchor = anchor; _selFocus = focus;
        RedrawTextLayers();
    }

    private void ClearTextSelection()
    {
        if (_selPage < 0 && !_selDragging) return;
        _selPage = -1; _selDragging = false; _pressedHighlight = null;
        RedrawTextLayers();
    }

    private void RedrawTextLayers()
    {
        foreach (var p in _pages) { p.Selection.Children.Clear(); p.Bar.Children.Clear(); }
        if (PageAt(_pendPage) is { } pp && CharsFor(_pendPage) is { } pc)
        {
            if (_pendFocus != _pendAnchor)
                DrawTint(pp, PdfTextSelection.Rects(pc, Math.Min(_pendAnchor, _pendFocus), Math.Max(_pendAnchor, _pendFocus)),
                    new SolidColorBrush(Color.Parse(HighlightHex)));
            if (PdfTextSelection.CaretBox(pc, _pendAnchor) is { } mark)
            {
                var caret = new Border
                {
                    Width = 2, Height = mark.Height * pp.Overlay.Height, Background = AccentBrush,
                    CornerRadius = new CornerRadius(1), IsHitTestVisible = false,
                };
                Canvas.SetLeft(caret, mark.X * pp.Overlay.Width - 1); Canvas.SetTop(caret, mark.Y * pp.Overlay.Height);
                pp.Selection.Children.Add(caret);
            }
        }
        if (!HasTextSelection || PageAt(_selPage) is not { } pv || CharsFor(_selPage) is not { } chars) return;
        var rects = PdfTextSelection.Rects(chars, Math.Min(_selAnchor, _selFocus), Math.Max(_selAnchor, _selFocus));
        DrawTint(pv, rects, new SolidColorBrush(Color.Parse(ThemeManager.Current.FieldSelection)));
        if (!_selDragging && rects.Count > 0) ShowSelectionBar(pv, rects);
    }

    private static void DrawTint(PageView pv, IReadOnlyList<Rect> rects, IBrush brush)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        foreach (var r in rects)
        {
            var b = new Border { Background = brush, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
            Canvas.SetLeft(b, r.X * w); Canvas.SetTop(b, r.Y * h);
            b.Width = r.Width * w; b.Height = r.Height * h;
            pv.Selection.Children.Add(b);
        }
    }

    private void ShowSelectionBar(PageView pv, IReadOnlyList<Rect> rects)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        var hl = new Button { Content = BarLabel("Highlight", SolidHex(_color)) };
        hl.Classes.Add("pill");
        var copy = new Button { Content = "Copy" };
        copy.Classes.Add("pill");
        ToolTip.SetTip(hl, "Highlight the selected text");
        ToolTip.SetTip(copy, "Copy the selected text");
        hl.Click += (_, _) => HighlightSelection();
        copy.Click += (_, _) => _ = CopySelectionAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(hl);
        row.Children.Add(copy);
        var bar = new Border
        {
            Background = this.FindResource("MenuBackgroundBrush") as IBrush ?? Brushes.DimGray,
            BorderBrush = this.FindResource("MenuBorderBrush") as IBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(5),
            BoxShadow = BoxShadows.Parse("0 6 18 0 #55000000"), Child = row,
        };
        pv.Bar.Children.Add(bar);
        bar.Measure(Size.Infinity);
        var size = bar.DesiredSize;
        var first = rects[0];
        var last = rects[^1];
        double x = Math.Clamp(first.X * w, 4, Math.Max(4, w - size.Width - 4));
        double y = first.Y * h - size.Height - 8;
        if (y < 4) y = Math.Min(h - size.Height - 4, (last.Y + last.Height) * h + 8);
        Canvas.SetLeft(bar, x); Canvas.SetTop(bar, y);
    }

    private static Control BarLabel(string text, string hex)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse(hex)), VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void HighlightSelection()
    {
        if (!HasTextSelection) return;
        int page = _selPage, a = _selAnchor, b = _selFocus;
        ClearTextSelection();
        AddTextHighlight(page, a, b);
    }

    private void AddTextHighlight(int page, int a, int b)
    {
        if (PageAt(page) is not { } pv || CharsFor(page) is not { } chars) return;
        var rects = PdfTextSelection.Rects(chars, Math.Min(a, b), Math.Max(a, b));
        if (rects.Count == 0) return;
        PushUndo();
        _annos.Items.Add(PdfAnnotation.TextHighlight(page, rects, HighlightHex));
        RedrawPage(pv);
        SaveNow();
    }

    private async Task CopySelectionAsync()
    {
        if (!HasTextSelection || CharsFor(_selPage) is not { } chars) return;
        string text = PdfTextSelection.TextOf(chars, Math.Min(_selAnchor, _selFocus), Math.Max(_selAnchor, _selFocus));
        if (text.Length == 0) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                await cb.SetTextAsync(text);
                Flash("Copied.");
            }
        }
        catch { }
    }

    private void UpdateTextCursor(PageView pv, Point px)
    {
        bool textTool = _tool == Tool.Select || (_tool == Tool.Highlight && HighlightByTextPref);
        var chars = textTool ? CharsFor(pv.Index) : null;
        bool over = chars is not null && PdfTextSelection.CharAt(chars, Unit(pv, px)) >= 0;
        var want = over ? TextCursor : null;
        if (!ReferenceEquals(pv.Overlay.Cursor, want)) pv.Overlay.Cursor = want;
    }

    private bool TrySelectPress(PageView pv, PointerPressedEventArgs e)
    {
        var chars = CharsFor(pv.Index);
        if (chars is null)
        {
            if (!_textReady) Flash("Reading the page text…");
            return false;
        }
        var px = e.GetPosition(pv.Overlay);
        var u = Unit(pv, px);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selPage == pv.Index)
        {
            int c = PdfTextSelection.CaretAt(chars, u);
            if (c < 0) return false;
            _selFocus = c;
            RedrawTextLayers();
            Focus();
            e.Handled = true;
            return true;
        }
        int hit = PdfTextSelection.CharAt(chars, u);
        if (hit < 0) return false;
        BeginTextPress(pv, chars, hit, u, px, e);
        return true;
    }

    private void BeginTextPress(PageView pv, IReadOnlyList<PdfChar> chars, int hit, Point u, Point px, PointerPressedEventArgs e)
    {
        if (_selected is not null) Select(null, focusEditor: false);
        if (e.ClickCount == 2)
        {
            var (a, b) = PdfTextSelection.WordAt(chars, hit);
            _selDragging = false;
            SetTextSelection(pv.Index, a, b);
        }
        else if (e.ClickCount >= 3)
        {
            var (a, b) = PdfTextSelection.LineAt(chars, hit);
            _selDragging = false;
            SetTextSelection(pv.Index, a, b);
        }
        else
        {
            int c = PdfTextSelection.CaretAt(chars, u);
            _selDragging = true;
            _selPressPx = px;
            SetTextSelection(pv.Index, c, c);
            e.Pointer.Capture(pv.Overlay);
        }
        Focus();
        e.Handled = true;
    }

    private bool TextMove(PageView pv, Point px)
    {
        UpdateTextCursor(pv, px);
        if (_selDragging && _selPage == pv.Index && CharsFor(pv.Index) is { } chars)
        {
            int c = PdfTextSelection.CaretAt(chars, Unit(pv, px));
            if (c >= 0 && c != _selFocus) { _selFocus = c; RedrawTextLayers(); }
            return true;
        }
        if (_pendPage == pv.Index && CharsFor(pv.Index) is { } pc)
        {
            int c = PdfTextSelection.CaretAt(pc, Unit(pv, px));
            if (c >= 0 && c != _pendFocus) { _pendFocus = c; RedrawTextLayers(); }
            return _pendDragging;
        }
        return false;
    }

    private bool TextRelease(PageView pv, PointerReleasedEventArgs e)
    {
        if (_selDragging)
        {
            _selDragging = false;
            e.Pointer.Capture(null);
            var pressed = _pressedHighlight;
            _pressedHighlight = null;
            if (Dist(e.GetPosition(pv.Overlay), _selPressPx) < 4 || _selAnchor == _selFocus)
            {
                ClearTextSelection();
                if (pressed is not null && _annos.Items.Contains(pressed)) Select(pressed, focusEditor: false);
            }
            else RedrawTextLayers();
            return true;
        }
        if (PendingRelease(pv, e)) return true;
        return false;
    }

    private void OnTextHighlightPressed(PageView pv, PdfAnnotation a, PointerPressedEventArgs e)
    {
        if (!Left(e, pv)) return;
        if (_tool == Tool.Highlight && HighlightByTextPref && TryHighlightTextPress(pv, e)) return;
        if (_tool == Tool.Select && CharsFor(pv.Index) is { } chars)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selPage == pv.Index && TrySelectPress(pv, e)) return;
            var px = e.GetPosition(pv.Overlay);
            var u = Unit(pv, px);
            int hit = PdfTextSelection.CharAt(chars, u);
            if (hit >= 0)
            {
                if (e.ClickCount == 1) _pressedHighlight = a;
                BeginTextPress(pv, chars, hit, u, px, e);
                return;
            }
        }
        ClearTextSelection();
        Select(a, focusEditor: false);
        e.Handled = true;
    }

    private void CancelPending()
    {
        if (_pendPage < 0 && !_pendDragging) return;
        _pendPage = -1; _pendDragging = false;
        RedrawTextLayers();
    }

    private bool TryHighlightTextPress(PageView pv, PointerPressedEventArgs e)
    {
        var chars = CharsFor(pv.Index);
        if (chars is null)
        {
            if (_textReady) return false;
            Flash("Reading the page text…");
            e.Handled = true;
            return true;
        }
        var px = e.GetPosition(pv.Overlay);
        var u = Unit(pv, px);
        if (_pendPage >= 0 && _pendPage != pv.Index) CancelPending();
        if (_pendPage == pv.Index)
        {
            int word = e.ClickCount == 2 ? PdfTextSelection.CharAt(chars, u) : -1;
            if (word >= 0)
            {
                var (wa, wb) = PdfTextSelection.WordAt(chars, word);
                CancelPending();
                AddTextHighlight(pv.Index, wa, wb);
                e.Handled = true;
                return true;
            }
            int end = PdfTextSelection.CaretAt(chars, u);
            int start = _pendAnchor;
            CancelPending();
            if (end < 0) return false;
            if (end != start) AddTextHighlight(pv.Index, start, end);
            e.Handled = true;
            return true;
        }
        if (PdfTextSelection.CharAt(chars, u) < 0)
        {
            if (!chars.Any(c => c.HasBox))
                Flash("This page is a picture, so there's no text to select. Drag to highlight an area.");
            return false;
        }
        ClearTextSelection();
        if (_selected is not null) Select(null, focusEditor: false);
        int caret = PdfTextSelection.CaretAt(chars, u);
        _pendPage = pv.Index; _pendAnchor = caret; _pendFocus = caret;
        _pendDragging = true; _pendPressPx = px;
        e.Pointer.Capture(pv.Overlay);
        RedrawTextLayers();
        Focus();
        e.Handled = true;
        return true;
    }

    private bool PendingRelease(PageView pv, PointerReleasedEventArgs e)
    {
        if (!_pendDragging) return false;
        _pendDragging = false;
        e.Pointer.Capture(null);
        if (_pendPage >= 0 && _pendFocus != _pendAnchor && Dist(e.GetPosition(pv.Overlay), _pendPressPx) >= 6)
        {
            int page = _pendPage, a = _pendAnchor, b = _pendFocus;
            CancelPending();
            AddTextHighlight(page, a, b);
        }
        else Flash("Now click where the highlight should end.");
        return true;
    }

    private void UpdateHighlightTip() => ToolTip.SetTip(HighlightTool, HighlightByTextPref
        ? "Click where the text starts, then where it ends. Double-click a word to highlight just that word."
        : "Drag over the page to highlight an area");

    private void SetHighlightMode(bool byText)
    {
        CancelPending();
        if (HighlightByTextPref != byText)
        {
            HighlightByTextPref = byText;
            HighlightModeChanged?.Invoke(byText);
        }
        UpdateHighlightTip();
        if (_tool != Tool.Highlight) SetTool(Tool.Highlight);
    }

    private void ShowHighlightOptions()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(6), Width = 250 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        panel.Children.Add(ModeButton(true, "Text", "Click where the text starts, then where it ends."));
        panel.Children.Add(ModeButton(false, "Area", "Drag a box over any part of the page."));
        MenuFx.AttachFlyout(flyout);
        flyout.ShowAt(HighlightOptsBtn);

        Button ModeButton(bool byText, string title, string hint)
        {
            bool on = HighlightByTextPref == byText;
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7), BorderThickness = new Thickness(on ? 2 : 1),
                BorderBrush = (on ? this.FindResource("AccentBrush") : this.FindResource("FrameBorderBrush")) as IBrush,
                Content = content,
            };
            b.Click += (_, _) => { SetHighlightMode(byText); flyout.Hide(); };
            return b;
        }
    }
}
