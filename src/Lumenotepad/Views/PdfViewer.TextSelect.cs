using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

public partial class PdfViewer
{
    private IReadOnlyList<PdfPageText>? _pageText;
    private bool _textReady;
    private string _pageStatus = "";

    private IReadOnlyList<PdfChar>? CharsFor(int page) =>
        _pageText is { } t && page >= 0 && page < t.Count ? t[page].Chars : null;

    private static Point Unit(PageView pv, Point px) =>
        new(px.X / Math.Max(1, pv.Overlay.Width), px.Y / Math.Max(1, pv.Overlay.Height));

    private PageView? PageAt(int index) => index >= 0 && index < _pages.Count ? _pages[index] : null;

    private void ResetText()
    {
        _pageText = null;
        _textReady = false;
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

    private void OnTextHighlightPressed(PageView pv, PdfAnnotation a, PointerPressedEventArgs e)
    {
        if (!Left(e, pv)) return;
        Select(a, focusEditor: false);
        e.Handled = true;
    }
}
