using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Platform;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>The in-app PDF viewer + annotator (M11): renders pages with native PDFium (never a
/// browser engine) and lets the user highlight regions and drop typed notes over them. Annotations
/// live in a sidecar JSON next to the PDF — the source file is never touched. Geometry is normalized
/// to each page so it survives zoom and re-render.</summary>
public partial class PdfViewerWindow : Window
{
    private enum Tool { Select, Highlight, Note }

    private const float BaseDpi = 110f;
    private static readonly double PxPerPoint = BaseDpi / 72.0;

    private readonly string _pdfPath;
    private readonly string _sidecarPath;
    private byte[] _pdfBytes = Array.Empty<byte>();
    private PdfAnnotationDoc _annos = new();

    private Tool _tool = Tool.Select;
    private string _color = "#66FFD54A";
    private double _zoom = 1.0;

    private sealed record PageView(int Index, double WPt, double HPt, Border Frame, Canvas Overlay, Image Img);
    private readonly List<PageView> _pages = new();
    private PdfAnnotation? _selected;
    private DispatcherTimer? _saveDebounce;

    private static readonly (string Hex, string Name)[] Swatches =
    {
        ("#66FFD54A", "Yellow"), ("#6666E28A", "Green"), ("#66FF8FAB", "Pink"),
        ("#664DA6FF", "Blue"), ("#66C9A0FF", "Purple"),
    };

    public PdfViewerWindow(string pdfPath)
    {
        InitializeComponent();
        _pdfPath = pdfPath;
        _sidecarPath = PdfAnnotationDoc.SidecarPath(pdfPath);
        PdfTitle.Text = Path.GetFileName(pdfPath);

        BuildSwatches();
        SelectTool.IsChecked = true;
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
        HighlightTool.Click += (_, _) => SetTool(Tool.Highlight);
        NoteTool.Click += (_, _) => SetTool(Tool.Note);
        ZoomIn.Click += (_, _) => SetZoom(_zoom * 1.2);
        ZoomOut.Click += (_, _) => SetZoom(_zoom / 1.2);
        CloseBtn.Click += (_, _) => Close();
        KeyDown += OnKey;
        PdfTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        Opened += async (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            Services.ThemeManager.ApplyChildChrome(this);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);
            Dispatcher.UIThread.Post(() =>
            {
                if (PagesScroll is { } sv) SmoothScroll.Attach(sv);
            }, DispatcherPriority.Background);
            await LoadAsync();
        };
        bool closing = false;
        Closing += (_, e) =>
        {
            SaveNow();                       // flush any pending edits before it goes
            if (closing) return;
            e.Cancel = true; closing = true;
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
            else Close();
        };
    }

    private async Task LoadAsync()
    {
        StatusLabel.Text = "Opening…";
        try { _pdfBytes = await File.ReadAllBytesAsync(_pdfPath); }
        catch { StatusLabel.Text = "Couldn't read this file."; return; }
        if (File.Exists(_sidecarPath))
        {
            try { _annos = PdfAnnotationDoc.FromJson(await File.ReadAllTextAsync(_sidecarPath)); }
            catch { _annos = new PdfAnnotationDoc(); }
        }

        int count = PdfRenderer.PageCount(_pdfBytes);
        var sizes = PdfRenderer.PageSizes(_pdfBytes);
        if (count == 0 || sizes.Count == 0)
        {
            StatusLabel.Text = "This file isn't a readable PDF.";
            return;
        }
        StatusLabel.Text = count == 1 ? "1 page" : $"{count} pages";

        // Build every page frame first (so scrolling/layout is immediate), then fill in the bitmaps.
        for (int i = 0; i < count; i++)
        {
            var (wpt, hpt) = sizes[i];
            _pages.Add(BuildPageFrame(i, wpt, hpt));
        }
        LayoutPages();

        for (int i = 0; i < count; i++)
        {
            int page = i;
            var bmp = await Task.Run(() => PdfRenderer.RenderPage(_pdfBytes, page, BaseDpi));
            if (bmp is not null) _pages[page].Img.Source = bmp;
            RedrawPage(_pages[page]);
        }
    }

    private PageView BuildPageFrame(int index, double wpt, double hpt)
    {
        var img = new Image { Stretch = Stretch.Fill };
        var overlay = new Canvas { Background = Brushes.Transparent };
        var panel = new Panel();
        panel.Children.Add(img);
        panel.Children.Add(overlay);
        var frame = new Border
        {
            Background = Brushes.White, Child = panel,
            BoxShadow = BoxShadows.Parse("0 4 18 0 #55000000"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var pv = new PageView(index, wpt, hpt, frame, overlay, img);
        overlay.PointerPressed += (_, e) => OnOverlayPressed(pv, e);
        overlay.PointerMoved += (_, e) => OnOverlayMoved(pv, e);
        overlay.PointerReleased += (_, e) => OnOverlayReleased(pv, e);
        PagesHost.Children.Add(frame);
        return pv;
    }

    private void LayoutPages()
    {
        foreach (var pv in _pages)
        {
            double w = pv.WPt * PxPerPoint * _zoom;
            double h = pv.HPt * PxPerPoint * _zoom;
            pv.Frame.Width = w; pv.Frame.Height = h;
            pv.Overlay.Width = w; pv.Overlay.Height = h;
        }
    }

    // ---- tools ----
    private void SetTool(Tool t)
    {
        _tool = t;
        SelectTool.IsChecked = t == Tool.Select;
        HighlightTool.IsChecked = t == Tool.Highlight;
        NoteTool.IsChecked = t == Tool.Note;
        if (t != Tool.Select) Select(null);
        foreach (var pv in _pages) RedrawPage(pv);   // toggles annotation hit-testing
    }

    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.4, 3.0);
        ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
        LayoutPages();
        foreach (var pv in _pages) RedrawPage(pv);
    }

    private void BuildSwatches()
    {
        foreach (var (hex, name) in Swatches)
        {
            var sw = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(hex == _color ? 2 : 1),
                BorderBrush = hex == _color
                    ? this.FindResource("AccentBrush") as IBrush
                    : this.FindResource("FrameBorderBrush") as IBrush,
                Cursor = new Cursor(StandardCursorType.Hand), Tag = hex,
            };
            ToolTip.SetTip(sw, name);
            sw.PointerPressed += (_, _) =>
            {
                _color = hex;
                foreach (var other in ColorSwatches.Children)
                    if (other is Border b)
                    {
                        bool on = Equals(b.Tag, _color);
                        b.BorderThickness = new Thickness(on ? 2 : 1);
                        b.BorderBrush = (on ? this.FindResource("AccentBrush") : this.FindResource("FrameBorderBrush")) as IBrush;
                    }
                if (HighlightTool.IsChecked != true) SetTool(Tool.Highlight);
            };
            ColorSwatches.Children.Add(sw);
        }
    }

    // ---- overlay pointer interaction ----
    private Point _dragStart;
    private Border? _dragPreview;

    private void OnOverlayPressed(PageView pv, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(pv.Overlay).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(pv.Overlay);
        if (_tool == Tool.Highlight)
        {
            _dragStart = p;
            _dragPreview = new Border
            {
                Background = new SolidColorBrush(Color.Parse(_color)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_dragPreview, p.X);
            Canvas.SetTop(_dragPreview, p.Y);
            pv.Overlay.Children.Add(_dragPreview);
            e.Pointer.Capture(pv.Overlay);
            e.Handled = true;
        }
        else if (_tool == Tool.Note)
        {
            var a = new PdfAnnotation
            {
                Page = pv.Index, Kind = PdfAnnotation.Note,
                X = p.X / pv.Overlay.Bounds.Width, Y = p.Y / pv.Overlay.Bounds.Height,
                Color = "#F2FFE9A8", Text = "",
            };
            _annos.Items.Add(a);
            RedrawPage(pv);
            SaveNow();
            e.Handled = true;
            // Focus the fresh note's editor.
            Dispatcher.UIThread.Post(() => FocusNote(pv, a), DispatcherPriority.Background);
        }
        else if (_tool == Tool.Select)
        {
            Select(null);   // clicking bare page clears selection
        }
    }

    private void OnOverlayMoved(PageView pv, PointerEventArgs e)
    {
        if (_tool != Tool.Highlight || _dragPreview is null) return;
        var p = e.GetPosition(pv.Overlay);
        double x = Math.Min(p.X, _dragStart.X), y = Math.Min(p.Y, _dragStart.Y);
        Canvas.SetLeft(_dragPreview, x);
        Canvas.SetTop(_dragPreview, y);
        _dragPreview.Width = Math.Abs(p.X - _dragStart.X);
        _dragPreview.Height = Math.Abs(p.Y - _dragStart.Y);
    }

    private void OnOverlayReleased(PageView pv, PointerReleasedEventArgs e)
    {
        if (_tool != Tool.Highlight || _dragPreview is null) return;
        double w = pv.Overlay.Bounds.Width, h = pv.Overlay.Bounds.Height;
        double x = Canvas.GetLeft(_dragPreview), y = Canvas.GetTop(_dragPreview);
        double pw = _dragPreview.Width, ph = _dragPreview.Height;
        pv.Overlay.Children.Remove(_dragPreview);
        _dragPreview = null;
        e.Pointer.Capture(null);
        if (pw > 5 && ph > 5 && w > 0 && h > 0)     // ignore stray taps
        {
            _annos.Items.Add(new PdfAnnotation
            {
                Page = pv.Index, Kind = PdfAnnotation.Highlight, Color = _color,
                X = x / w, Y = y / h, W = pw / w, H = ph / h,
            });
            RedrawPage(pv);
            SaveNow();
        }
    }

    // ---- drawing annotations onto a page overlay ----
    private void RedrawPage(PageView pv)
    {
        pv.Overlay.Children.Clear();
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        if (w <= 0 || h <= 0) return;
        bool selectable = _tool == Tool.Select;

        foreach (var a in _annos.Items)
        {
            if (a.Page != pv.Index) continue;
            if (a.Kind == PdfAnnotation.Highlight)
            {
                var rect = new Border
                {
                    Background = new SolidColorBrush(Color.Parse(a.Color)),
                    IsHitTestVisible = selectable, Cursor = new Cursor(StandardCursorType.Hand),
                    BorderThickness = new Thickness(ReferenceEquals(a, _selected) ? 2 : 0),
                    BorderBrush = this.FindResource("AccentBrush") as IBrush,
                };
                Canvas.SetLeft(rect, a.X * w);
                Canvas.SetTop(rect, a.Y * h);
                rect.Width = a.W * w; rect.Height = a.H * h;
                rect.PointerPressed += (_, e) => { Select(a); e.Handled = true; };
                pv.Overlay.Children.Add(rect);
            }
            else // note
            {
                var box = BuildNote(pv, a, selectable);
                pv.Overlay.Children.Add(box);
            }
        }
    }

    private Control BuildNote(PageView pv, PdfAnnotation a, bool selectable)
    {
        var tb = new TextBox
        {
            Text = a.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#22160A")), MinHeight = 0,
            IsHitTestVisible = selectable,
        };
        tb.TextChanged += (_, _) => { a.Text = tb.Text; SaveDebounced(); };
        var note = new Border
        {
            Width = 180 * _zoom, Padding = new Thickness(8, 6),
            Background = new SolidColorBrush(Color.Parse(a.Color)),
            CornerRadius = new CornerRadius(6), Child = tb,
            IsHitTestVisible = selectable, Cursor = new Cursor(StandardCursorType.Hand),
            BoxShadow = BoxShadows.Parse("0 2 8 0 #40000000"),
            BorderThickness = new Thickness(ReferenceEquals(a, _selected) ? 2 : 0),
            BorderBrush = this.FindResource("AccentBrush") as IBrush,
        };
        Canvas.SetLeft(note, a.X * pv.Overlay.Width);
        Canvas.SetTop(note, a.Y * pv.Overlay.Height);
        note.PointerPressed += (_, e) => { Select(a); };   // don't Handle: let the TextBox focus too
        note.Tag = a;
        return note;
    }

    private void FocusNote(PageView pv, PdfAnnotation a)
    {
        foreach (var child in pv.Overlay.Children)
            if (child is Border b && ReferenceEquals(b.Tag, a) && b.Child is TextBox tb) { tb.Focus(); return; }
    }

    private void Select(PdfAnnotation? a)
    {
        _selected = a;
        foreach (var pv in _pages) RedrawPage(pv);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); return; }
        if (_selected is { } a && (e.Key == Key.Delete || e.Key == Key.Back))
        {
            // Don't steal Backspace while typing inside a note.
            if (e.Source is TextBox) return;
            _annos.Items.Remove(a);
            _selected = null;
            foreach (var pv in _pages) RedrawPage(pv);
            SaveNow();
            e.Handled = true;
        }
    }

    // ---- persistence (sidecar next to the PDF) ----
    private void SaveDebounced()
    {
        _saveDebounce?.Stop();
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce?.Stop(); SaveNow(); };
        _saveDebounce.Start();
    }

    private void SaveNow()
    {
        _saveDebounce?.Stop();
        try { File.WriteAllText(_sidecarPath, _annos.ToJson()); }
        catch { /* read-only location → annotations just aren't persisted */ }
    }
}
