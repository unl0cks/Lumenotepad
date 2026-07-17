using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Platform;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>The embeddable PDF viewer + annotator (M11): renders pages with native PDFium (never a
/// browser engine) and lets the user highlight regions, drop typed notes, write free text, and draw
/// arrows over them. Annotations live in a sidecar JSON next to the PDF — the source file is never
/// touched. Hosted both in <see cref="PdfViewerWindow"/> (PDF attachments) and inline in the page
/// canvas (PDF pages). Geometry is normalized to each page so it survives zoom and re-render.</summary>
public partial class PdfViewer : UserControl
{
    private enum Tool { Select, Highlight, Note, Text, Arrow }

    private const float BaseDpi = 110f;
    private static readonly double PxPerPoint = BaseDpi / 72.0;

    private string _pdfPath = "";
    private string _sidecarPath = "";
    private byte[] _pdfBytes = Array.Empty<byte>();
    private PdfAnnotationDoc _annos = new();

    private Tool _tool = Tool.Select;
    private string _color = "#66FFD54A";
    private double _zoom = 1.0;

    private sealed record PageView(int Index, double WPt, double HPt, Border Frame, Canvas Overlay, Image Img);
    private readonly List<PageView> _pages = new();
    private PdfAnnotation? _selected;
    private DispatcherTimer? _saveDebounce;
    private bool _doubleClickCreate;

    // Move/resize drag state (Select tool): which annotation, which handle (0=whole, 1=start, 2=end),
    // the pointer origin and the annotation's original geometry.
    private PdfAnnotation? _drag;
    private PdfAnnotation? _justAdded;      // set on create so its box pops in once
    private PdfAnnotation? _editing;        // which note/text is in edit mode (TextBox) vs display (TextBlock)
    private int _dragHandle;
    private Point _dragStartPt;
    private (double X, double Y, double W, double H, double X2, double Y2) _dragOrig;

    private static readonly (string Hex, string Name)[] Swatches =
    {
        ("#66FFD54A", "Yellow"), ("#6666E28A", "Green"), ("#66FF8FAB", "Pink"),
        ("#664DA6FF", "Blue"), ("#66C9A0FF", "Purple"),
    };

    public PdfViewer()
    {
        InitializeComponent();
        BuildSwatches();
        SelectTool.IsChecked = true;
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
        HighlightTool.Click += (_, _) => SetTool(Tool.Highlight);
        NoteTool.Click += (_, _) => SetTool(Tool.Note);
        TextTool.Click += (_, _) => SetTool(Tool.Text);
        ArrowTool.Click += (_, _) => SetTool(Tool.Arrow);
        ZoomIn.Click += (_, _) => SetZoom(_zoom * 1.2);
        ZoomOut.Click += (_, _) => SetZoom(_zoom / 1.2);
        FmtBold.Click += (_, _) => ApplyFmt(a => a.Bold = FmtBold.IsChecked == true);
        FmtItalic.Click += (_, _) => ApplyFmt(a => a.Italic = FmtItalic.IsChecked == true);
        FmtUnder.Click += (_, _) => ApplyFmt(a => a.Underline = FmtUnder.IsChecked == true);
        FmtStrike.Click += (_, _) => ApplyFmt(a => a.Strike = FmtStrike.IsChecked == true);
        FmtBullet.Click += (_, _) => ApplyFmt(a => a.Text = ToggleBullets(a.Text));
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    /// <summary>Load a PDF (and its sidecar annotations) and render it. Flushes any previous edits.</summary>
    public async void Load(string pdfPath, bool doubleClickCreate)
    {
        Flush();
        _pdfPath = pdfPath;
        _sidecarPath = PdfAnnotationDoc.SidecarPath(pdfPath);
        _doubleClickCreate = doubleClickCreate;
        _pages.Clear();
        PagesHost.Children.Clear();
        _annos = new PdfAnnotationDoc();
        _selected = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (PagesScroll is { } sv) SmoothScroll.Attach(sv);
        }, DispatcherPriority.Background);
        await LoadAsync();
    }

    /// <summary>Persist any pending annotation edits now (host calls this before the view goes away).</summary>
    public void Flush() => SaveNow();

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
        overlay.DoubleTapped += (_, e) => OnOverlayDoubleTapped(pv, e);
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
        TextTool.IsChecked = t == Tool.Text;
        ArrowTool.IsChecked = t == Tool.Arrow;
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
    private Border? _dragPreview;                 // highlight rubber-band
    private Avalonia.Controls.Shapes.Line? _arrowPreview;

    private void OnOverlayPressed(PageView pv, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(pv.Overlay).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(pv.Overlay);
        switch (_tool)
        {
            case Tool.Highlight:
                _dragStart = p;
                _dragPreview = new Border { Background = new SolidColorBrush(Color.Parse(_color)), IsHitTestVisible = false };
                Canvas.SetLeft(_dragPreview, p.X); Canvas.SetTop(_dragPreview, p.Y);
                pv.Overlay.Children.Add(_dragPreview);
                e.Pointer.Capture(pv.Overlay); e.Handled = true;
                break;
            case Tool.Arrow:
                _dragStart = p;
                _arrowPreview = new Avalonia.Controls.Shapes.Line
                {
                    Stroke = new SolidColorBrush(Color.Parse(SolidHex(_color))), StrokeThickness = 3,
                    StartPoint = p, EndPoint = p, IsHitTestVisible = false,
                };
                pv.Overlay.Children.Add(_arrowPreview);
                e.Pointer.Capture(pv.Overlay); e.Handled = true;
                break;
            case Tool.Note when !_doubleClickCreate:
                CreateTextAnno(pv, p, PdfAnnotation.Note); e.Handled = true; break;
            case Tool.Text when !_doubleClickCreate:
                CreateTextAnno(pv, p, PdfAnnotation.TextBox); e.Handled = true; break;
            case Tool.Select:
                Select(null);   // clicking bare page clears selection
                break;
        }
    }

    private void OnOverlayDoubleTapped(PageView pv, Avalonia.Input.TappedEventArgs e)
    {
        if (!_doubleClickCreate) return;                 // otherwise single-click already created it
        var p = e.GetPosition(pv.Overlay);
        if (_tool == Tool.Note) { CreateTextAnno(pv, p, PdfAnnotation.Note); e.Handled = true; }
        else if (_tool == Tool.Text) { CreateTextAnno(pv, p, PdfAnnotation.TextBox); e.Handled = true; }
    }

    private void OnOverlayMoved(PageView pv, PointerEventArgs e)
    {
        var p = e.GetPosition(pv.Overlay);
        if (_drag is { } a)                              // moving / resizing a selected annotation
        {
            double dx = (p.X - _dragStartPt.X) / pv.Overlay.Width;
            double dy = (p.Y - _dragStartPt.Y) / pv.Overlay.Height;
            ApplyDrag(a, _dragHandle, dx, dy);
            RedrawPage(pv);                              // capture is on the overlay, so this is safe
            return;
        }
        if (_dragPreview is not null)
        {
            double x = Math.Min(p.X, _dragStart.X), y = Math.Min(p.Y, _dragStart.Y);
            Canvas.SetLeft(_dragPreview, x); Canvas.SetTop(_dragPreview, y);
            _dragPreview.Width = Math.Abs(p.X - _dragStart.X);
            _dragPreview.Height = Math.Abs(p.Y - _dragStart.Y);
        }
        else if (_arrowPreview is not null)
        {
            _arrowPreview.EndPoint = p;
        }
    }

    private void OnOverlayReleased(PageView pv, PointerReleasedEventArgs e)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        if (_drag is not null)
        {
            _drag = null; e.Pointer.Capture(null); SaveNow();
            return;
        }
        if (_dragPreview is not null)
        {
            double x = Canvas.GetLeft(_dragPreview), y = Canvas.GetTop(_dragPreview);
            double pw = _dragPreview.Width, ph = _dragPreview.Height;
            pv.Overlay.Children.Remove(_dragPreview); _dragPreview = null;
            e.Pointer.Capture(null);
            if (pw > 5 && ph > 5 && w > 0 && h > 0)
            {
                _annos.Items.Add(new PdfAnnotation
                {
                    Page = pv.Index, Kind = PdfAnnotation.Highlight, Color = _color,
                    X = x / w, Y = y / h, W = pw / w, H = ph / h,
                });
                RedrawPage(pv); SaveNow();
            }
        }
        else if (_arrowPreview is not null)
        {
            var s = _arrowPreview.StartPoint; var en = _arrowPreview.EndPoint;
            pv.Overlay.Children.Remove(_arrowPreview); _arrowPreview = null;
            e.Pointer.Capture(null);
            if (w > 0 && h > 0 && Dist(s, en) > 8)
            {
                _annos.Items.Add(new PdfAnnotation
                {
                    Page = pv.Index, Kind = PdfAnnotation.Arrow, Color = SolidHex(_color),
                    X = s.X / w, Y = s.Y / h, X2 = en.X / w, Y2 = en.Y / h,
                });
                RedrawPage(pv); SaveNow();
            }
        }
    }

    private void CreateTextAnno(PageView pv, Point p, string kind)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        if (w <= 0 || h <= 0) return;
        var a = new PdfAnnotation
        {
            Page = pv.Index, Kind = kind, X = p.X / w, Y = p.Y / h,
            W = 190 / w, H = 58 / h, Text = "",
            Color = kind == PdfAnnotation.Note ? "#F2FFE9A8" : "#00000000",
        };
        _annos.Items.Add(a);
        _justAdded = a;                  // makes DrawTextAnno pop it in
        _editing = a;                    // fresh note/text opens ready to type
        Select(a);
        SaveNow();
        Dispatcher.UIThread.Post(() => FocusTextAnno(pv, a), DispatcherPriority.Background);
    }

    /// <summary>Move (handle 0) or drag an arrow endpoint (1 = start, 2 = end); geometry clamped to the page.</summary>
    private void ApplyDrag(PdfAnnotation a, int handle, double dx, double dy)
    {
        double C(double v) => Math.Clamp(v, 0, 1);
        if (a.Kind == PdfAnnotation.Arrow)
        {
            if (handle is 0 or 1) { a.X = C(_dragOrig.X + dx); a.Y = C(_dragOrig.Y + dy); }
            if (handle is 0 or 2) { a.X2 = C(_dragOrig.X2 + dx); a.Y2 = C(_dragOrig.Y2 + dy); }
        }
        else
        {
            a.X = C(_dragOrig.X + dx); a.Y = C(_dragOrig.Y + dy);
        }
    }

    private void StartDrag(PageView pv, PdfAnnotation a, int handle, PointerPressedEventArgs e)
    {
        Select(a);
        _drag = a; _dragHandle = handle;
        _dragStartPt = e.GetPosition(pv.Overlay);
        _dragOrig = (a.X, a.Y, a.W, a.H, a.X2, a.Y2);
        e.Pointer.Capture(pv.Overlay);
        e.Handled = true;
    }

    // ---- drawing annotations onto a page overlay ----
    private void RedrawPage(PageView pv)
    {
        pv.Overlay.Children.Clear();
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        if (w <= 0 || h <= 0) return;
        bool sel = _tool == Tool.Select;

        foreach (var a in _annos.Items)
        {
            if (a.Page != pv.Index) continue;
            switch (a.Kind)
            {
                case PdfAnnotation.Highlight: DrawRectAnno(pv, a, sel, fill: true); break;
                case PdfAnnotation.Note: DrawTextAnno(pv, a, sel, sticky: true); break;
                case PdfAnnotation.TextBox: DrawTextAnno(pv, a, sel, sticky: false); break;
                case PdfAnnotation.Arrow: DrawArrow(pv, a, sel); break;
            }
        }
    }

    private void DrawRectAnno(PageView pv, PdfAnnotation a, bool sel, bool fill)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        var rect = new Border
        {
            Background = fill ? new SolidColorBrush(Color.Parse(a.Color)) : Brushes.Transparent,
            IsHitTestVisible = sel, Cursor = new Cursor(StandardCursorType.SizeAll),
            BorderThickness = new Thickness(ReferenceEquals(a, _selected) ? 2 : 0),
            BorderBrush = this.FindResource("AccentBrush") as IBrush,
        };
        Canvas.SetLeft(rect, a.X * w); Canvas.SetTop(rect, a.Y * h);
        rect.Width = a.W * w; rect.Height = a.H * h;
        rect.PointerPressed += (_, e) => { if (sel && Left(e, pv)) StartDrag(pv, a, 0, e); };
        pv.Overlay.Children.Add(rect);
        if (ReferenceEquals(a, _selected) && sel) AddDeleteButton(pv, a, a.X * w + a.W * w, a.Y * h);
    }

    private void DrawTextAnno(PageView pv, PdfAnnotation a, bool sel, bool sticky)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        bool selected = ReferenceEquals(a, _selected);
        var fg = new SolidColorBrush(Color.Parse(sticky ? "#22160A" : "#141821"));
        var weight = a.Bold ? FontWeight.Bold : (sticky ? FontWeight.Normal : FontWeight.SemiBold);
        var style = a.Italic ? FontStyle.Italic : FontStyle.Normal;
        double fs = 12.5 * _zoom;

        // Edit mode uses a TextBox (bold/italic show live); otherwise a TextBlock, which is the only
        // control that renders underline/strikethrough. Clicking the box switches into edit mode.
        Control content;
        if (ReferenceEquals(a, _editing) && sel)
        {
            var tb = new TextBox
            {
                Text = a.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontSize = fs, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = fg, MinHeight = 0, Padding = new Thickness(9, 5),
                FontWeight = weight, FontStyle = style,
            };
            tb.TextChanged += (_, _) => { a.Text = tb.Text; SaveDebounced(); };
            tb.LostFocus += (_, _) => { if (ReferenceEquals(_editing, a)) { _editing = null; RedrawPage(pv); } };
            content = tb;
        }
        else
        {
            content = new TextBlock
            {
                Text = string.IsNullOrEmpty(a.Text) ? " " : a.Text, TextWrapping = TextWrapping.Wrap,
                FontSize = fs, Foreground = fg, FontWeight = weight, FontStyle = style,
                TextDecorations = TextDecorationsFor(a), Padding = new Thickness(9, 5),
            };
        }

        // A slim grip strip up top is the move handle; the text area below stays free for typing.
        var grip = new Border
        {
            Height = 13, CornerRadius = new CornerRadius(9, 9, 0, 0),
            Background = new SolidColorBrush(SolidColor(sticky ? "#40000000" : (selected ? "#26FFFFFF" : "#12FFFFFF"))),
            Cursor = new Cursor(StandardCursorType.SizeAll), IsHitTestVisible = sel,
        };
        DockPanel.SetDock(grip, Dock.Top);
        var stack = new DockPanel();
        stack.Children.Add(grip);
        stack.Children.Add(content);

        var box = new Border
        {
            Width = a.W * w, MinHeight = a.H * h,
            Background = sticky ? new SolidColorBrush(Color.Parse(a.Color)) : new SolidColorBrush(SolidColor("#F2FBFCFF")),
            CornerRadius = new CornerRadius(9), Child = stack, IsHitTestVisible = sel,
            BoxShadow = BoxShadows.Parse(selected ? "0 6 20 0 #66000000" : "0 3 12 0 #45000000"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected
                ? this.FindResource("AccentBrush") as IBrush
                : new SolidColorBrush(SolidColor(sticky ? "#33000000" : "#22000000")),
            Tag = a,
            Transitions = new Transitions
            {
                new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(140) },
            },
        };
        Canvas.SetLeft(box, a.X * w); Canvas.SetTop(box, a.Y * h);
        grip.PointerPressed += (_, e) => { if (sel && Left(e, pv)) StartDrag(pv, a, 0, e); };
        box.PointerPressed += (_, e) =>
        {
            if (sel && !ReferenceEquals(_editing, a))   // first click: select + enter edit mode
            {
                _editing = a;
                Select(a);
                Dispatcher.UIThread.Post(() => FocusTextAnno(pv, a), DispatcherPriority.Background);
            }
            e.Handled = true;                           // stop the overlay from clearing the selection
        };
        pv.Overlay.Children.Add(box);
        if (a == _justAdded) { _justAdded = null; Motion.ScaleIn(box, 0.9, Motion.Fast); }   // pop-in on create
        if (selected && sel) AddDeleteButton(pv, a, a.X * w + a.W * w, a.Y * h);
    }

    private static TextDecorationCollection? TextDecorationsFor(PdfAnnotation a)
    {
        if (!a.Underline && !a.Strike) return null;
        var d = new TextDecorationCollection();
        if (a.Underline) d.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (a.Strike) d.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        return d;
    }

    private void DrawArrow(PageView pv, PdfAnnotation a, bool sel)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        var s = new Point(a.X * w, a.Y * h);
        var en = new Point(a.X2 * w, a.Y2 * h);
        var brush = new SolidColorBrush(SolidColor(a.Color));

        // A fat transparent line under the visible one gives the thin shaft a grabbable hit area.
        var hit = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = s, EndPoint = en, Stroke = Brushes.Transparent, StrokeThickness = 12,
            IsHitTestVisible = sel, Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        hit.PointerPressed += (_, e) => { if (sel && Left(e, pv)) StartDrag(pv, a, 0, e); };
        var shaft = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = s, EndPoint = en, Stroke = brush, StrokeThickness = 3, IsHitTestVisible = false,
        };
        var head = new Avalonia.Controls.Shapes.Polygon { Fill = brush, Points = ArrowHead(s, en), IsHitTestVisible = false };
        pv.Overlay.Children.Add(hit);
        pv.Overlay.Children.Add(shaft);
        pv.Overlay.Children.Add(head);

        if (ReferenceEquals(a, _selected) && sel)
        {
            pv.Overlay.Children.Add(EndpointHandle(pv, a, s, 1));
            pv.Overlay.Children.Add(EndpointHandle(pv, a, en, 2));
            AddDeleteButton(pv, a, Math.Max(s.X, en.X), Math.Min(s.Y, en.Y));
        }
    }

    private Control EndpointHandle(PageView pv, PdfAnnotation a, Point at, int handle)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = this.FindResource("AccentBrush") as IBrush,
            Stroke = Brushes.White, StrokeThickness = 1.5, Cursor = new Cursor(StandardCursorType.Cross),
        };
        Canvas.SetLeft(dot, at.X - 6); Canvas.SetTop(dot, at.Y - 6);
        dot.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, handle, e); };
        return dot;
    }

    private static Avalonia.Collections.AvaloniaList<Point> ArrowHead(Point from, Point to)
    {
        double ang = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double len = 13, spread = 0.42;
        var p1 = new Point(to.X - len * Math.Cos(ang - spread), to.Y - len * Math.Sin(ang - spread));
        var p2 = new Point(to.X - len * Math.Cos(ang + spread), to.Y - len * Math.Sin(ang + spread));
        return new Avalonia.Collections.AvaloniaList<Point> { to, p1, p2 };
    }

    private void AddDeleteButton(PageView pv, PdfAnnotation a, double x, double y)
    {
        var btn = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.Parse("#E0E81123")),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10, Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(btn, x - 8); Canvas.SetTop(btn, y - 12);
        btn.PointerPressed += (_, e) => { Delete(a); e.Handled = true; };
        pv.Overlay.Children.Add(btn);
    }

    private void FocusTextAnno(PageView pv, PdfAnnotation a)
    {
        foreach (var child in pv.Overlay.Children)
            if (child is Border b && ReferenceEquals(b.Tag, a) && b.Child is DockPanel dp)
                foreach (var c in dp.Children)
                    if (c is TextBox tb) { tb.Focus(); return; }
    }

    private void Delete(PdfAnnotation a)
    {
        _annos.Items.Remove(a);
        if (ReferenceEquals(_selected, a)) _selected = null;
        foreach (var pv in _pages) RedrawPage(pv);
        SaveNow();
    }

    private void Select(PdfAnnotation? a)
    {
        _selected = a;
        if (!ReferenceEquals(a, _editing)) _editing = null;   // selecting elsewhere leaves edit mode
        UpdateFmtButtons();
        foreach (var pv in _pages) RedrawPage(pv);
    }

    // ---- whole-box text formatting for the selected note/text annotation ----
    private void ApplyFmt(Action<PdfAnnotation> set)
    {
        if (_selected is not { } a || (a.Kind != PdfAnnotation.Note && a.Kind != PdfAnnotation.TextBox)) return;
        set(a);
        foreach (var pv in _pages) RedrawPage(pv);
        UpdateFmtButtons();
        SaveNow();
    }

    private void UpdateFmtButtons()
    {
        bool texty = _selected is { Kind: PdfAnnotation.Note or PdfAnnotation.TextBox };
        FmtGroup.IsEnabled = texty;
        var a = _selected;
        FmtBold.IsChecked = texty && a!.Bold;
        FmtItalic.IsChecked = texty && a!.Italic;
        FmtUnder.IsChecked = texty && a!.Underline;
        FmtStrike.IsChecked = texty && a!.Strike;
        FmtBullet.IsChecked = texty && IsBulleted(a!.Text);
    }

    private static bool IsBulleted(string? text)
    {
        var lines = (text ?? "").Split('\n');
        bool any = false;
        foreach (var l in lines)
            if (l.Trim().Length > 0) { any = true; if (!l.TrimStart().StartsWith("• ")) return false; }
        return any;
    }

    /// <summary>Add "• " to every non-blank line, or strip it if all lines already have it.</summary>
    private static string ToggleBullets(string? text)
    {
        var lines = (text ?? "").Split('\n');
        bool bulleted = IsBulleted(text);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            lines[i] = bulleted
                ? lines[i].Replace("• ", "", StringComparison.Ordinal)
                : "• " + lines[i];
        }
        return string.Join('\n', lines);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_selected is { } a && (e.Key == Key.Delete || e.Key == Key.Back))
        {
            if (e.Source is TextBox) return;             // don't steal Backspace while typing
            Delete(a);
            e.Handled = true;
        }
    }

    // ---- small geometry/color helpers ----
    private static bool Left(PointerPressedEventArgs e, PageView pv) =>
        e.GetCurrentPoint(pv.Overlay).Properties.IsLeftButtonPressed;
    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    /// <summary>A fully-opaque version of a possibly-translucent hex (arrows/text read solid).</summary>
    private static Color SolidColor(string hex) => Color.Parse(hex);
    private static string SolidHex(string hex)
    {
        var c = Color.Parse(hex);
        return $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
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
        if (_sidecarPath.Length == 0) return;
        try { File.WriteAllText(_sidecarPath, _annos.ToJson()); }
        catch { /* read-only location → annotations just aren't persisted */ }
    }
}
