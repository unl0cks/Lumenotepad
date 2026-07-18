using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumenotepad.Editor;
using Lumenotepad.Platform;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>The embeddable PDF viewer + annotator (M11): renders pages with native PDFium (never a
/// browser engine) and lets the user highlight regions, drop notes, write free text, and draw arrows
/// over them. Notes and text boxes carry the SAME rich formatting as the note canvas — a full
/// <see cref="RichTextEditor"/> driven by the shared <see cref="FormatToolbar"/> (fonts, sizes,
/// colors, super/subscript, alignment, bullets, links). Annotations live in a sidecar JSON next to the
/// PDF — the source file is never touched — and the whole thing can be flattened into a NEW pdf with
/// every mark baked in. Hosted both in <see cref="PdfViewerWindow"/> and inline in the page canvas.
/// Geometry is normalized to each page so it survives zoom and re-render.</summary>
public partial class PdfViewer : UserControl
{
    private enum Tool { Select, Highlight, Note, Text, Arrow }

    private const float BaseDpi = 110f;
    private static readonly double PxPerPoint = BaseDpi / 72.0;
    private const float ExportDpi = 150f;              // page raster resolution in the flattened copy

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

    // Live rich content per note/text annotation: one RichDocument (source of truth, serialized into
    // the annotation's sidecar on change) and the editor control currently showing it (rebuilt on each
    // RedrawPage, so this maps to the LATEST instance — the format toolbar + focus target read it).
    private readonly Dictionary<PdfAnnotation, RichDocument> _docs = new();
    private readonly Dictionary<PdfAnnotation, RichTextEditor> _editors = new();
    private PdfAnnotation? _justAdded;                 // set on create so its box pops in once

    // Move/resize drag state (any tool): which annotation, which handle (0=whole, 1=start, 2=end), the
    // pointer origin, and the annotation's original geometry.
    private PdfAnnotation? _drag;
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
        FmtBar.SetCompact();                            // hide the canvas-only furniture
        FmtBar.SetPlacement(Dock.Top, pageScope: false);
        SelectTool.IsChecked = true;
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
        HighlightTool.Click += (_, _) => SetTool(Tool.Highlight);
        NoteTool.Click += (_, _) => SetTool(Tool.Note);
        TextTool.Click += (_, _) => SetTool(Tool.Text);
        ArrowTool.Click += (_, _) => SetTool(Tool.Arrow);
        ZoomIn.Click += (_, _) => SetZoom(_zoom * 1.2);
        ZoomOut.Click += (_, _) => SetZoom(_zoom / 1.2);
        ExportBtn.Click += (_, _) => _ = ExportFlattenedAsync();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    private IBrush AccentBrush => this.FindResource("AccentBrush") as IBrush ?? Brushes.DodgerBlue;

    // Bumped on every Load; the async pipeline checks it after each await so a superseded load can
    // never keep adding page frames. Without this, rapid page switches (or a double-fired selection
    // event) interleaved two/three loads: each cleared the host, then EACH appended its own set of
    // frames — the same page stacked multiple times, "1 page" in the status but three on screen, and
    // every annotation seemingly on all of them.
    private int _loadGen;

    /// <summary>Load a PDF (and its sidecar annotations) and render it. Flushes any previous edits.
    /// Re-entrancy-safe: a newer call supersedes an in-flight one, and loading the already-shown
    /// file is a no-op.</summary>
    public async void Load(string pdfPath, bool doubleClickCreate)
    {
        _doubleClickCreate = doubleClickCreate;
        if (pdfPath == _pdfPath && _pages.Count > 0) return;   // already showing this PDF
        Flush();
        int gen = ++_loadGen;
        _pdfPath = pdfPath;
        _sidecarPath = PdfAnnotationDoc.SidecarPath(pdfPath);
        _pages.Clear();
        PagesHost.Children.Clear();
        _annos = new PdfAnnotationDoc();
        _docs.Clear();
        _editors.Clear();
        _selected = null;
        HideFmtBar();
        Dispatcher.UIThread.Post(() =>
        {
            if (PagesScroll is { } sv) SmoothScroll.Attach(sv);
        }, DispatcherPriority.Background);
        await LoadAsync(gen);
    }

    /// <summary>Persist any pending annotation edits now (host calls this before the view goes away).</summary>
    public void Flush() => SaveNow();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SaveNow();      // window closing / view swapped out — never lose the last debounced edit
    }

    private async Task LoadAsync(int gen)
    {
        StatusLabel.Text = "Opening…";
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(_pdfPath); }
        catch { if (gen == _loadGen) StatusLabel.Text = "Couldn't read this file."; return; }
        if (gen != _loadGen) return;                     // superseded mid-await
        _pdfBytes = bytes;

        if (File.Exists(_sidecarPath))
        {
            PdfAnnotationDoc annos;
            try { annos = PdfAnnotationDoc.FromJson(await File.ReadAllTextAsync(_sidecarPath)); }
            catch { annos = new PdfAnnotationDoc(); }
            if (gen != _loadGen) return;
            _annos = annos;
        }

        int count = PdfRenderer.PageCount(bytes);
        var sizes = PdfRenderer.PageSizes(bytes);
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
            var bmp = await Task.Run(() => PdfRenderer.RenderPage(bytes, page, BaseDpi));
            if (gen != _loadGen) return;                 // switched PDFs while a page was rendering
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
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var pv = new PageView(index, wpt, hpt, frame, overlay, img);
        overlay.PointerPressed += (_, e) => OnOverlayPressed(pv, e);
        overlay.PointerMoved += (_, e) => OnOverlayMoved(pv, e);
        overlay.PointerReleased += (_, e) => OnOverlayReleased(pv, e);
        overlay.DoubleTapped += (_, e) => OnOverlayDoubleTapped(pv, e);
        PagesHost.Children.Add(frame);
        return pv;
    }

    /// <summary>"Rounded PDF corners" preference (on by default) — pushed by MainView's prefs apply.</summary>
    public static bool RoundedPagePref = true;

    private void LayoutPages()
    {
        double rad = RoundedPagePref ? 12 : 0;
        foreach (var pv in _pages)
        {
            double w = pv.WPt * PxPerPoint * _zoom;
            double h = pv.HPt * PxPerPoint * _zoom;
            pv.Frame.Width = w; pv.Frame.Height = h;
            pv.Overlay.Width = w; pv.Overlay.Height = h;
            pv.Frame.CornerRadius = new CornerRadius(rad);
            // Border only rounds its own background — the rendered page Image needs a matching clip.
            if (pv.Frame.Child is Control inner)
                inner.Clip = rad > 0 ? RoundedRect(w, h, rad) : null;
        }
    }

    /// <summary>Re-apply chrome preferences (page corner rounding) to an already-open PDF.</summary>
    public void RefreshChrome()
    {
        LayoutPages();
        foreach (var pv in _pages) RedrawPage(pv);
    }

    private static StreamGeometry RoundedRect(double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(r, 0), true);
        c.LineTo(new Point(w - r, 0));
        c.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(w, h - r));
        c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(r, h));
        c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(0, r));
        c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.EndFigure(true);
        return g;
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
        // Annotations stay grabbable in EVERY tool now, so switching tools keeps the current selection —
        // no more hopping back to "Select" just to nudge a mark.
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
                // Picking a color NEVER switches tools (owner request). It sets the color for the next
                // mark — and recolors whatever is currently selected, which is what the click usually means.
                if (_selected is { } cur)
                {
                    cur.Color = cur.Kind switch
                    {
                        PdfAnnotation.Arrow => SolidHex(hex),
                        PdfAnnotation.Highlight => hex,
                        _ => "#F2" + SolidHex(hex)[3..],           // note/text glass tint
                    };
                    foreach (var pv in _pages) RedrawPage(pv);
                    SaveNow();
                }
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
                    Stroke = new SolidColorBrush(Color.Parse(SolidHex(_color))), StrokeThickness = ArrowThickness,
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
                Select(null, focusEditor: false);   // clicking bare page clears selection
                break;
        }
    }

    private void OnOverlayDoubleTapped(PageView pv, TappedEventArgs e)
    {
        if (!_doubleClickCreate) return;                 // otherwise single-click already created it
        if (!ReferenceEquals(e.Source, pv.Overlay)) return;   // double-click ON a mark must not spawn another
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
        double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;
        var a = new PdfAnnotation
        {
            Page = pv.Index, Kind = kind, X = p.X / w, Y = p.Y / h,
            W = 200 / w0, H = 54 / h0, Text = "",
            Color = kind == PdfAnnotation.Note ? "#F2FFE9A8" : "#00000000",
        };
        _annos.Items.Add(a);
        _justAdded = a;                  // makes DrawTextAnno pop it in
        // One-shot: drop back to Select right away. With the tool left armed, EVERY later click
        // (deselecting, clicking another page…) silently spawned another box — boxes everywhere.
        SetTool(Tool.Select);
        Select(a, focusEditor: true);    // rebuilds, then focuses the fresh editor
        SaveNow();
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
        Select(a, focusEditor: false);
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

        foreach (var a in _annos.Items)
        {
            if (a.Page != pv.Index) continue;
            switch (a.Kind)
            {
                case PdfAnnotation.Highlight: DrawRectAnno(pv, a); break;
                case PdfAnnotation.Note: DrawTextAnno(pv, a, sticky: true); break;
                case PdfAnnotation.TextBox: DrawTextAnno(pv, a, sticky: false); break;
                case PdfAnnotation.Arrow: DrawArrow(pv, a); break;
            }
        }
    }

    private void DrawRectAnno(PageView pv, PdfAnnotation a)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        bool selected = ReferenceEquals(a, _selected);
        var rect = new Border
        {
            Background = new SolidColorBrush(Color.Parse(a.Color)),
            IsHitTestVisible = true, Cursor = new Cursor(StandardCursorType.SizeAll),
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(selected ? 2 : 0),
            BorderBrush = AccentBrush,
        };
        Canvas.SetLeft(rect, a.X * w); Canvas.SetTop(rect, a.Y * h);
        rect.Width = a.W * w; rect.Height = a.H * h;
        rect.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 0, e); };
        pv.Overlay.Children.Add(rect);
        if (selected) AddDeleteButton(pv, a.X * w + a.W * w, a.Y * h, a);
    }

    /// <summary>A note/text annotation. NOTES are Lumen frosted-glass cards: the backdrop samples the
    /// page's OWN pixels for the region the card covers, blurred, under a dark bluish smoke tint plus
    /// a veil of the note's color — with a real drop shadow and a hairline edge (the clip lives on an
    /// inner border precisely so the shadow survives). TEXT is bare: rich text written straight onto
    /// the page — dark ink, no card, no shadow — with a quiet outline + grip only while selected or
    /// hovered. Both embed the full canvas rich-text editor, laid out at page-native pixels and scaled
    /// by the current zoom so formatting scales together.</summary>
    private void DrawTextAnno(PageView pv, PdfAnnotation a, bool sticky)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;   // unzoomed page pixels
        bool selected = ReferenceEquals(a, _selected);

        var editor = BuildNoteEditor(a, onGlass: sticky);
        if (_editors.TryGetValue(a, out var stale)) stale.Document = new RichDocument();  // unbind the old instance from the shared doc
        _editors[a] = editor;

        // Grip strip: the Lumen pill bar, same furniture as canvas notes. On bare text everything is
        // invisible until the box is selected/hovered so the page stays clean.
        string pillHex = sticky ? (selected ? "#52FFFFFF" : "#30FFFFFF") : "#4D10151E";
        var gripBar = new Border
        {
            Width = 38, Height = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(pillHex)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsVisible = sticky || selected,
        };
        var grip = new Border
        {
            Height = 16, CornerRadius = new CornerRadius(10, 10, 0, 0),
            Background = sticky ? new SolidColorBrush(Color.Parse("#12FFFFFF")) : Brushes.Transparent,
            Child = gripBar,
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        DockPanel.SetDock(grip, Dock.Top);
        var content = new DockPanel();
        content.Children.Add(grip);
        content.Children.Add(editor);

        // ✕ in the top-right corner (the canvas note-box pattern): quiet until hovered, then red.
        string closeHex = sticky ? "#8CFFFFFF" : "#8C10151E";
        var closeGlyph = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L7,7 M7,0 L0,7"),
            Stroke = new SolidColorBrush(Color.Parse(closeHex)), StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new Border
        {
            Width = 19, Height = 19, CornerRadius = new CornerRadius(0, 10, 0, 7),
            Background = Brushes.Transparent, Child = closeGlyph, IsVisible = selected,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        close.PointerEntered += (_, _) => { close.Background = new SolidColorBrush(Color.Parse("#66E81123")); closeGlyph.Stroke = Brushes.White; };
        close.PointerExited += (_, _) => { close.Background = Brushes.Transparent; closeGlyph.Stroke = new SolidColorBrush(Color.Parse(closeHex)); };
        close.PointerPressed += (_, e) => e.Handled = true;      // don't fall through to select/drag
        close.PointerReleased += (_, e) => { Delete(a); e.Handled = true; };

        ImageBrush? backdropBrush = null;
        Border box;
        if (sticky)
        {
            // frosted backdrop: the page region under the card, blurred (falls back to plain glass
            // while the page bitmap is still rendering)
            backdropBrush = pv.Img.Source is Bitmap bmp ? new ImageBrush(bmp) { Stretch = Stretch.Fill } : null;
            var backdrop = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = (IBrush?)backdropBrush ?? new SolidColorBrush(Color.Parse("#301A2030")),
                Effect = new BlurEffect { Radius = 16 },
                IsHitTestVisible = false,
            };
            var smoke = new Border      // the dark BLUISH Lumen glass the white text sits on
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#D4131A29")),
                IsHitTestVisible = false,
            };
            var layers = new Panel();
            layers.Children.Add(backdrop);
            layers.Children.Add(smoke);
            layers.Children.Add(new Border      // a soft veil of the note's own color over the glass
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#30" + SolidHex(a.Color)[3..])),
                IsHitTestVisible = false,
            });
            layers.Children.Add(content);
            layers.Children.Add(close);
            // The clip lives on an INNER border so the outer border's drop shadow isn't clipped away.
            var clip = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = layers };
            box = new Border
            {
                Width = a.W * w0, MinHeight = a.H * h0,
                CornerRadius = new CornerRadius(10), Child = clip,
                BoxShadow = BoxShadows.Parse(selected ? "0 9 28 0 #80000000" : "0 4 16 0 #59000000"),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                BorderBrush = selected ? AccentBrush : new SolidColorBrush(Color.Parse("#33FFFFFF")),
                Transitions = new Transitions
                {
                    new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(140) },
                },
            };
        }
        else
        {
            // Bare text: ink straight on the page — no card, no shadow, no chrome. A slim accent
            // outline while selected (faint on hover) keeps it findable without dressing it up.
            var layers = new Panel();
            layers.Children.Add(content);
            layers.Children.Add(close);
            box = new Border
            {
                Width = a.W * w0, MinHeight = a.H * h0,
                CornerRadius = new CornerRadius(8), Child = layers,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = selected ? AccentBrush : Brushes.Transparent,
            };
        }
        box.Tag = a;
        box.RenderTransform = new ScaleTransform(_zoom, _zoom);
        box.RenderTransformOrigin = RelativePoint.TopLeft;
        Canvas.SetLeft(box, a.X * w); Canvas.SetTop(box, a.Y * h);
        grip.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 0, e); };
        box.PointerEntered += (_, _) =>
        {
            close.IsVisible = true;
            gripBar.IsVisible = true;
            if (!sticky && !ReferenceEquals(_selected, a))
                box.BorderBrush = new SolidColorBrush(Color.Parse("#3310151E"));
        };
        box.PointerExited += (_, _) =>
        {
            bool sel = ReferenceEquals(_selected, a);
            close.IsVisible = sel;
            gripBar.IsVisible = sticky || sel;
            if (!sticky && !sel) box.BorderBrush = Brushes.Transparent;
        };
        box.PointerPressed += (_, e) =>
        {
            // Click anywhere on the card selects it — and stops the press falling through to the
            // overlay (where an armed Note/Text tool would spawn ANOTHER box on top of this one).
            if (!ReferenceEquals(_selected, a)) Select(a, focusEditor: false);
            e.Handled = true;
        };

        // The frost must sample exactly the page region under the card; the card's height depends on
        // its content, so sync the brush's source rect after each layout pass.
        if (backdropBrush is not null)
        {
            var bb = backdropBrush;
            double lastH = -1;
            box.LayoutUpdated += (_, _) =>
            {
                double bh = box.Bounds.Height;
                if (Math.Abs(bh - lastH) < 0.5 || bh <= 0 || h0 <= 0) return;
                lastH = bh;
                bb.SourceRect = new RelativeRect(
                    a.X, a.Y, a.W, Math.Min(Math.Max(0.001, 1 - a.Y), bh / h0), RelativeUnit.Relative);
            };
        }

        pv.Overlay.Children.Add(box);
        if (a == _justAdded) { _justAdded = null; Motion.ScaleIn(box, 0.9, Motion.Fast); }   // pop-in on create
    }

    private void DrawArrow(PageView pv, PdfAnnotation a)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        bool selected = ReferenceEquals(a, _selected);
        var s = new Point(a.X * w, a.Y * h);
        var en = new Point(a.X2 * w, a.Y2 * h);
        var brush = new SolidColorBrush(SolidColor(a.Color));
        double thick = ArrowThickness;

        // A fat transparent line under the visible one gives the thin shaft a grabbable hit area.
        var hit = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = s, EndPoint = en, Stroke = Brushes.Transparent, StrokeThickness = Math.Max(14, thick * 4),
            IsHitTestVisible = true, Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        hit.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 0, e); };
        var shaft = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = s, EndPoint = en, Stroke = brush, StrokeThickness = thick,
            StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
        };
        var head = new Avalonia.Controls.Shapes.Polygon { Fill = brush, Points = ArrowHead(s, en, ArrowHeadLen), IsHitTestVisible = false };
        pv.Overlay.Children.Add(hit);
        pv.Overlay.Children.Add(shaft);
        pv.Overlay.Children.Add(head);

        if (selected)
        {
            pv.Overlay.Children.Add(EndpointHandle(pv, a, s, 1));
            pv.Overlay.Children.Add(EndpointHandle(pv, a, en, 2));
            AddDeleteButton(pv, Math.Max(s.X, en.X), Math.Min(s.Y, en.Y), a);
        }
    }

    /// <summary>Arrow shaft thickness + arrowhead length track the zoom so arrows stay proportional to
    /// the page instead of ballooning (or vanishing) as you zoom.</summary>
    private double ArrowThickness => Math.Clamp(3 * _zoom, 2.0, 10.0);
    private double ArrowHeadLen => Math.Clamp(13 * _zoom, 9.0, 40.0);

    private Control EndpointHandle(PageView pv, PdfAnnotation a, Point at, int handle)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = AccentBrush,
            Stroke = Brushes.White, StrokeThickness = 1.5, Cursor = new Cursor(StandardCursorType.Cross),
        };
        Canvas.SetLeft(dot, at.X - 6); Canvas.SetTop(dot, at.Y - 6);
        dot.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, handle, e); };
        return dot;
    }

    private static Avalonia.Collections.AvaloniaList<Point> ArrowHead(Point from, Point to, double len)
    {
        double ang = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double spread = 0.42;
        var p1 = new Point(to.X - len * Math.Cos(ang - spread), to.Y - len * Math.Sin(ang - spread));
        var p2 = new Point(to.X - len * Math.Cos(ang + spread), to.Y - len * Math.Sin(ang + spread));
        return new Avalonia.Collections.AvaloniaList<Point> { to, p1, p2 };
    }

    /// <summary>Delete chip for highlights and arrows (notes carry their ✕ inside the card instead):
    /// a small rounded-square of dark Lumen glass — the app's icon-button shape — with a quiet drawn ✕
    /// that turns red only on hover, a hairline edge, vignette, and drop shadow.</summary>
    private void AddDeleteButton(PageView pv, double x, double y, PdfAnnotation a)
    {
        const double d = 22;
        var restBg = new SolidColorBrush(Color.Parse("#CC141A26"));
        var restFg = new SolidColorBrush(Color.Parse("#A6FFFFFF"));
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L7,7 M7,0 L0,7"),
            Stroke = restFg, StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var btn = new Border
        {
            Width = d, Height = d, CornerRadius = new CornerRadius(7),
            Background = restBg,
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #66000000, inset 0 0 8 1 #40000000"),
            Cursor = new Cursor(StandardCursorType.Hand), Child = glyph,
        };
        btn.PointerEntered += (_, _) => { btn.Background = new SolidColorBrush(Color.Parse("#E6E5484D")); glyph.Stroke = Brushes.White; };
        btn.PointerExited += (_, _) => { btn.Background = restBg; glyph.Stroke = restFg; };
        ToolTip.SetTip(btn, "Delete");
        Canvas.SetLeft(btn, x - d / 2); Canvas.SetTop(btn, y - d / 2);
        btn.PointerPressed += (_, e) => { Delete(a); e.Handled = true; };
        pv.Overlay.Children.Add(btn);
    }

    // ---- rich note editors ----

    /// <summary>Build (and wire) the rich editor for a note/text annotation. Focus selects the
    /// annotation and points the format toolbar at this editor.</summary>
    private RichTextEditor BuildNoteEditor(PdfAnnotation a, bool onGlass)
    {
        var editor = NewNoteEditor(DocFor(a), onGlass);
        editor.GotFocus += (_, _) =>
        {
            if (!ReferenceEquals(_selected, a)) Select(a, focusEditor: true);
            else ShowFmtBar(editor);
        };
        return editor;
    }

    private RichTextEditor NewNoteEditor(RichDocument doc, bool onGlass) => new()
    {
        Document = doc,
        Margin = new Thickness(10, 4, 10, 8),
        // White text on a note's dark glass; dark ink when written straight onto the page.
        Foreground = new SolidColorBrush(Color.Parse(onGlass ? "#F0FFFFFF" : "#E610151E")),
        CaretBrush = AccentBrush,
        LinkBrush = AccentBrush,
        SelectionBrush = new SolidColorBrush(Color.Parse("#554DA6FF")),
        FontFamily = Services.AppFonts.Family(RichTextEditor.EditorFontPref),
        FontSize = 13,
    };

    /// <summary>The live rich document for a note/text annotation. Migrates a legacy plain-text +
    /// whole-box-flags annotation into a real document on first touch, and keeps the sidecar in sync on
    /// every edit (plain <see cref="PdfAnnotation.Text"/> mirror kept for exports/back-compat).</summary>
    private RichDocument DocFor(PdfAnnotation a)
    {
        if (_docs.TryGetValue(a, out var d)) return d;
        var doc = !string.IsNullOrEmpty(a.Rich) ? RichDocJson.FromJson(a.Rich) : LegacyDoc(a);
        _docs[a] = doc;
        doc.Changed += () =>
        {
            a.Rich = RichDocJson.ToJson(doc);
            a.Text = doc.GetText();
            SaveDebounced();
        };
        return doc;
    }

    private static RichDocument LegacyDoc(PdfAnnotation a)
    {
        var doc = new RichDocument();
        var text = a.Text ?? "";
        if (text.Length == 0) return doc;                     // fresh, single empty paragraph
        doc.Paragraphs.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var p = new Paragraph();
            if (line.Length > 0)
                p.Runs.Add(new RichRun { Text = line, Bold = a.Bold, Italic = a.Italic, Underline = a.Underline, Strike = a.Strike });
            doc.Paragraphs.Add(p);
        }
        if (doc.Paragraphs.Count == 0) doc.Paragraphs.Add(new Paragraph());
        return doc;
    }

    // The strip stays in the layout permanently (no show/hide jumping) — it just dims and disables
    // while nothing text-like is selected.
    private void ShowFmtBar(RichTextEditor editor) { FmtBar.Target = editor; FmtBar.IsEnabled = true; FmtBar.Opacity = 1; }
    private void HideFmtBar() { FmtBar.Target = null; FmtBar.IsEnabled = false; FmtBar.Opacity = 0.45; }

    private void Delete(PdfAnnotation a)
    {
        _annos.Items.Remove(a);
        _docs.Remove(a);
        _editors.Remove(a);
        if (ReferenceEquals(_selected, a)) { _selected = null; HideFmtBar(); }
        foreach (var pv in _pages) RedrawPage(pv);
        SaveNow();
    }

    private void Select(PdfAnnotation? a, bool focusEditor)
    {
        _selected = a;
        foreach (var pv in _pages) RedrawPage(pv);
        if (a is { Kind: PdfAnnotation.Note or PdfAnnotation.TextBox } && _editors.TryGetValue(a, out var ed))
        {
            ShowFmtBar(ed);
            if (focusEditor) Dispatcher.UIThread.Post(() => ed.Focus(), DispatcherPriority.Background);
        }
        else HideFmtBar();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_selected is { } a && (e.Key == Key.Delete || e.Key == Key.Back))
        {
            if (e.Source is TextBox or RichTextEditor) return;   // don't steal keys while typing in a note
            Delete(a);
            e.Handled = true;
        }
    }

    // ---- flatten / export: a NEW pdf with every mark baked in ----

    /// <summary>Save a flattened copy: each page is rasterized (native PDFium) and the highlights,
    /// arrows, notes, and text are drawn on top into a fresh SkiaSharp PDF. The marks become part of
    /// the page image, so the copy opens anywhere — nothing needs Lumenotepad to read it.</summary>
    private async Task ExportFlattenedAsync()
    {
        if (_pages.Count == 0 || _pdfBytes.Length == 0) return;
        SaveNow();
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var stem = Path.GetFileNameWithoutExtension(_pdfPath);
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save an annotated copy",
            SuggestedFileName = (string.IsNullOrWhiteSpace(stem) ? "document" : stem) + " (annotated)",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        StatusLabel.Text = "Saving a copy…";
        try
        {
            var bytes = BuildFlattenedPdf();
            await File.WriteAllBytesAsync(path, bytes);
            StatusLabel.Text = "Saved a copy.";
        }
        catch
        {
            StatusLabel.Text = "Couldn't save the copy.";
        }
    }

    private byte[] BuildFlattenedPdf()
    {
        using var ms = new MemoryStream();
        using (var pdf = SkiaSharp.SKDocument.CreatePdf(ms))
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                var pv = _pages[i];
                float wpt = (float)pv.WPt, hpt = (float)pv.HPt;
                var canvas = pdf.BeginPage(wpt, hpt);
                using (var page = PdfRenderer.RenderPageSk(_pdfBytes, i, ExportDpi))
                {
                    if (page is not null)
                        canvas.DrawBitmap(page, new SkiaSharp.SKRect(0, 0, wpt, hpt));

                    foreach (var a in _annos.Items)
                    {
                        if (a.Page != i) continue;
                        switch (a.Kind)
                        {
                            case PdfAnnotation.Highlight: FlattenHighlight(canvas, a, wpt, hpt); break;
                            case PdfAnnotation.Arrow: FlattenArrow(canvas, a, wpt, hpt); break;
                            case PdfAnnotation.Note: FlattenNote(canvas, a, wpt, hpt, sticky: true, page); break;
                            case PdfAnnotation.TextBox: FlattenNote(canvas, a, wpt, hpt, sticky: false, page); break;
                        }
                    }
                }
                pdf.EndPage();
            }
        }
        return ms.ToArray();
    }

    private static void FlattenHighlight(SkiaSharp.SKCanvas c, PdfAnnotation a, float wpt, float hpt)
    {
        using var p = new SkiaSharp.SKPaint { Color = SkColor(a.Color), IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
        var r = new SkiaSharp.SKRect((float)a.X * wpt, (float)a.Y * hpt, (float)(a.X + a.W) * wpt, (float)(a.Y + a.H) * hpt);
        c.DrawRoundRect(r, 2, 2, p);
    }

    private static void FlattenArrow(SkiaSharp.SKCanvas c, PdfAnnotation a, float wpt, float hpt)
    {
        var s = new SkiaSharp.SKPoint((float)a.X * wpt, (float)a.Y * hpt);
        var e = new SkiaSharp.SKPoint((float)a.X2 * wpt, (float)a.Y2 * hpt);
        var col = SkColor(SolidHex(a.Color));
        using var stroke = new SkiaSharp.SKPaint
        {
            Color = col, IsAntialias = true, StrokeWidth = 2.2f,
            StrokeCap = SkiaSharp.SKStrokeCap.Round, Style = SkiaSharp.SKPaintStyle.Stroke,
        };
        c.DrawLine(s, e, stroke);
        double ang = Math.Atan2(e.Y - s.Y, e.X - s.X);
        const double len = 10, spread = 0.42;
        var p1 = new SkiaSharp.SKPoint((float)(e.X - len * Math.Cos(ang - spread)), (float)(e.Y - len * Math.Sin(ang - spread)));
        var p2 = new SkiaSharp.SKPoint((float)(e.X - len * Math.Cos(ang + spread)), (float)(e.Y - len * Math.Sin(ang + spread)));
        using var fill = new SkiaSharp.SKPaint { Color = col, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
        using var path = new SkiaSharp.SKPath();
        path.MoveTo(e); path.LineTo(p1); path.LineTo(p2); path.Close();
        c.DrawPath(path, fill);
    }

    private void FlattenNote(SkiaSharp.SKCanvas c, PdfAnnotation a, float wpt, float hpt, bool sticky,
                             SkiaSharp.SKBitmap? pageBmp)
    {
        double widthPts = a.W * wpt;
        if (widthPts < 4) return;
        var png = RenderNoteImage(a, sticky, out double pxW, out double pxH);
        if (png is null || pxW < 1 || pxH < 1) return;
        using var data = SkiaSharp.SKData.CreateCopy(png);
        using var img = SkiaSharp.SKImage.FromEncodedData(data);
        if (img is null) return;
        float heightPts = (float)(pxH * (widthPts / pxW));
        float left = (float)a.X * wpt, top = (float)a.Y * hpt;
        var rect = new SkiaSharp.SKRect(left, top, left + (float)widthPts, top + heightPts);

        // Bake the frosted-glass backdrop (notes only — bare text has no card): re-draw the page
        // region under the card blurred, clipped to its rounded rect, then lay the card image over it.
        if (sticky && pageBmp is not null)
        {
            c.Save();
            using var rr = new SkiaSharp.SKRoundRect(rect, 6.5f, 6.5f);
            c.ClipRoundRect(rr, antialias: true);
            using var blur = new SkiaSharp.SKPaint { ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(5, 5) };
            c.DrawBitmap(pageBmp, new SkiaSharp.SKRect(0, 0, wpt, hpt), blur);
            c.Restore();
        }
        c.DrawImage(img, rect);
    }

    /// <summary>Render a note/text card to a PNG (its exact on-screen glass look, minus the live
    /// frost — the flatten path blurs the page beneath instead) at page-native pixels, so the
    /// flattened copy carries the same fonts, colors, and layout. The PNG keeps its alpha: the glass
    /// tints stay translucent so the blurred page shows through in the export. Independent doc —
    /// never touches the live editor. Returns null if the page went away.</summary>
    private byte[]? RenderNoteImage(PdfAnnotation a, bool sticky, out double pxW, out double pxH)
    {
        pxW = pxH = 0;
        var page = _pages.FirstOrDefault(p => p.Index == a.Page);
        if (page is null) return null;
        double w0 = page.WPt * PxPerPoint;
        pxW = Math.Max(8, a.W * w0);
        var doc = !string.IsNullOrEmpty(a.Rich) ? RichDocJson.FromJson(a.Rich) : LegacyDoc(a);
        var editor = NewNoteEditor(doc, onGlass: sticky);

        // Same 16px grip band as on screen (transparent for bare text) so the text lands identically.
        var grip = new Border
        {
            Height = 16, CornerRadius = new CornerRadius(10, 10, 0, 0),
            Background = sticky ? new SolidColorBrush(Color.Parse("#12FFFFFF")) : Brushes.Transparent,
            Child = sticky
                ? new Border
                {
                    Width = 38, Height = 4, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.Parse("#30FFFFFF")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                }
                : null,
        };
        DockPanel.SetDock(grip, Dock.Top);
        var content = new DockPanel();
        content.Children.Add(grip);
        content.Children.Add(editor);

        Border box;
        if (sticky)
        {
            var layers = new Panel();
            layers.Children.Add(new Border   // dark bluish smoke (translucent — the blurred page shows through)
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#D4131A29")),
            });
            layers.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#30" + SolidHex(a.Color)[3..])),
            });
            layers.Children.Add(content);
            box = new Border
            {
                Width = pxW,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
                Child = layers,
            };
        }
        else
        {
            // Bare text: just the ink, fully transparent around it — exactly like on screen.
            box = new Border { Width = pxW, Child = content };
        }
        box.Measure(new Size(pxW, double.PositiveInfinity));
        pxH = Math.Max(8, Math.Ceiling(box.DesiredSize.Height));
        box.Arrange(new Rect(0, 0, pxW, pxH));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(pxW), (int)pxH), new Vector(96, 96));
        rtb.Render(box);
        using var mm = new MemoryStream();
        rtb.Save(mm);
        return mm.ToArray();
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

    private static SkiaSharp.SKColor SkColor(string hex)
    {
        var c = Color.Parse(hex);
        return new SkiaSharp.SKColor(c.R, c.G, c.B, c.A);
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
