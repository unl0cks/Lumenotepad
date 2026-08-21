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
using Shapes = Avalonia.Controls.Shapes;

namespace Lumenotepad.Views;

public partial class PdfViewer : UserControl
{
    private enum Tool { Select, Highlight, Note, Text, Arrow }

    private const float BaseDpi = 110f;
    private static readonly double PxPerPoint = BaseDpi / 72.0;
    private const float ExportDpi = 150f;

    private string _pdfPath = "";
    private string _sidecarPath = "";
    private byte[] _pdfBytes = Array.Empty<byte>();
    private PdfAnnotationDoc _annos = new();

    private Tool _tool = Tool.Select;
    private string _color = "#FFF5E3A3";
    private double _zoom = 1.0;

    private sealed record PageView(int Index, double WPt, double HPt, Border Frame, Canvas Overlay, Image Img);
    private readonly List<PageView> _pages = new();
    private PdfAnnotation? _selected;
    private DispatcherTimer? _saveDebounce;

    private readonly Dictionary<PdfAnnotation, RichDocument> _docs = new();
    private readonly Dictionary<PdfAnnotation, RichTextEditor> _editors = new();
    private PdfAnnotation? _justAdded;

    private PdfAnnotation? _drag;
    private int _dragHandle;
    private Point _dragStartPt;
    private (double X, double Y, double W, double H, double X2, double Y2) _dragOrig;
    private (double C1x, double C1y, double C2x, double C2y, double C3x, double C3y) _dragOrigC;

    public static bool SnapToGrid;
    private const double GridStep = 14;

    private bool _arrowCurved;
    private double _arrowHeadScale = 1.0;
    private string _arrowHeadStyle = "triangle";

    public PdfViewer()
    {
        InitializeComponent();
        Focusable = true;
        BuildSwatches();
        FmtBar.SetCompact();
        FmtBar.SetPlacement(Dock.Top, pageScope: false);
        HighlightTool.Click += (_, _) => SetTool(Tool.Highlight);
        NoteTool.Click += (_, _) => SetTool(Tool.Note);
        TextTool.Click += (_, _) => SetTool(Tool.Text);
        ArrowTool.Click += (_, _) => SetTool(Tool.Arrow);
        ZoomIn.Click += (_, _) => SetZoom(_zoom * 1.2);
        ZoomOut.Click += (_, _) => SetZoom(_zoom / 1.2);
        ExportBtn.Click += (_, _) => _ = ExportFlattenedAsync();
        SnapBtn.IsChecked = SnapToGrid;
        SnapBtn.Click += (_, _) => SnapToGrid = SnapBtn.IsChecked == true;
        ArrowOptsBtn.Click += (_, _) => ShowArrowOptions();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    private IBrush AccentBrush => this.FindResource("AccentBrush") as IBrush ?? Brushes.DodgerBlue;

    private IBrush NoteFocusBrush => new SolidColorBrush(Color.Parse(
        Services.ThemePalettes.Alpha(Services.ThemeManager.Current.Accent, 0xB3)));

    private int _loadGen;

    private bool _loaded;

    public async void Load(string pdfPath, bool doubleClickCreate)
    {
        _ = doubleClickCreate;
        if (pdfPath == _pdfPath) return;
        Flush();
        int gen = ++_loadGen;
        _loaded = false;
        _pdfPath = pdfPath;
        _sidecarPath = PdfAnnotationDoc.SidecarPath(pdfPath);
        _pages.Clear();
        PagesHost.Children.Clear();
        _annos = new PdfAnnotationDoc();
        _docs.Clear();
        _editors.Clear();
        _selected = null;
        _undoStack.Clear();
        _redoStack.Clear();
        HideFmtBar();
        Dispatcher.UIThread.Post(() =>
        {
            if (PagesScroll is { } sv) SmoothScroll.Attach(sv);
        }, DispatcherPriority.Background);
        await LoadAsync(gen);
    }

    public void Flush() => SaveNow();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PdfAnnotationHub.Changed += OnHubChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        PdfAnnotationHub.Changed -= OnHubChanged;
        SaveNow();
    }

    private void OnHubChanged(string path, object? sender)
    {
        if (ReferenceEquals(sender, this) || !_loaded) return;
        if (!string.Equals(path, _pdfPath, StringComparison.OrdinalIgnoreCase)) return;
        _docs.Clear();
        _editors.Clear();
        if (_selected is { } sel && !_annos.Items.Contains(sel)) { _selected = null; HideFmtBar(); }
        foreach (var pv in _pages) RedrawPage(pv);
    }

    private async Task LoadAsync(int gen)
    {
        StatusLabel.Text = "Opening…";
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(_pdfPath); }
        catch { if (gen == _loadGen) StatusLabel.Text = "Couldn't read this file."; return; }
        if (gen != _loadGen) return;
        _pdfBytes = bytes;

        string? sidecarJson = null;
        if (File.Exists(_sidecarPath))
        {
            try { sidecarJson = await File.ReadAllTextAsync(_sidecarPath); }
            catch {  }
            if (gen != _loadGen) return;
        }

        _annos = PdfAnnotationHub.Get(_pdfPath, sidecarJson);

        int count = PdfRenderer.PageCount(bytes);
        var sizes = PdfRenderer.PageSizes(bytes);
        if (count == 0 || sizes.Count == 0)
        {
            StatusLabel.Text = "This file isn't a readable PDF.";
            return;
        }
        StatusLabel.Text = count == 1 ? "1 page" : $"{count} pages";
        _loaded = true;

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
            if (gen != _loadGen) return;
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

            BorderBrush = new SolidColorBrush(Color.Parse("#33000000")), BorderThickness = new Thickness(1),
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

            if (pv.Frame.Child is Control inner)
                inner.Clip = rad > 0 ? RoundedRect(w, h, rad) : null;
        }
    }

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

    private void SetTool(Tool t)
    {
        _tool = _tool == t ? Tool.Select : t;
        HighlightTool.IsChecked = _tool == Tool.Highlight;
        NoteTool.IsChecked = _tool == Tool.Note;
        TextTool.IsChecked = _tool == Tool.Text;
        ArrowTool.IsChecked = _tool == Tool.Arrow;
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
        void AddFamily(string family, (string Name, string Hex)[] shades, IBrush swatch)
        {
            var btn = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                Background = swatch,
                BorderThickness = new Thickness(1),
                BorderBrush = this.FindResource("FrameBorderBrush") as IBrush,
                Cursor = new Cursor(StandardCursorType.Hand), Tag = family,
            };
            ToolTip.SetTip(btn, $"{family}: pick a shade");

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2) };
            var flyout = new Flyout { Content = row, Placement = PlacementMode.Bottom };
            MenuFx.AttachFlyout(flyout);
            foreach (var (name, hex) in shades)
            {
                var chip = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                ToolTip.SetTip(chip, name);
                string pick = "#FF" + Rgb(hex);
                chip.PointerPressed += (_, _) => { PickColor(pick); flyout.Hide(); };
                row.Children.Add(chip);
            }
            btn.PointerPressed += (_, _) => flyout.ShowAt(btn);
            ColorSwatches.Children.Add(btn);
        }

        foreach (var (family, shades) in ViewModels.MainViewModel.NotebookPalette)
            AddFamily(family, shades, new SolidColorBrush(Color.Parse(shades[2].Hex)));
        AddFamily("Neutral", ViewModels.MainViewModel.GrayscaleShades, NeutralSwatch());
        RefreshSwatchRings();
    }

    private static IBrush NeutralSwatch() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.Parse("#111418"), 1) },
    };

    private static readonly (string Key, string Glyph, string Name)[] HeadStyles =
    {
        ("triangle", "➤", "Filled"), ("open", "❯", "Open"), ("diamond", "◆", "Diamond"),
        ("circle", "●", "Dot"), ("none", "—", "None"),
    };

    private void ShowArrowOptions()
    {
        var arr = _selected is { Kind: PdfAnnotation.Arrow } a ? a : null;
        bool curved = arr?.Curved ?? _arrowCurved;
        string style = arr is { HeadStyle.Length: > 0 } ? arr.HeadStyle! : _arrowHeadStyle;
        double scale = arr is { HeadScale: > 0 } ? arr.HeadScale : _arrowHeadScale;

        var panel = new StackPanel { Spacing = 9, Margin = new Thickness(6), Width = 214 };

        var curvedBtn = new Button
        {
            Content = curved ? "Curved: On" : "Curved: Off",
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        curvedBtn.Click += (_, _) =>
        {
            curved = !curved;
            curvedBtn.Content = curved ? "Curved: On" : "Curved: Off";
            ApplyArrow(x => { x.Curved = curved; if (curved && x.C1x == 0 && x.C2x == 0 && x.C3x == 0) InitCurve(x); },
                       () => _arrowCurved = curved);
        };
        panel.Children.Add(curvedBtn);

        panel.Children.Add(new TextBlock { Text = "Arrowhead", FontSize = 11.5, Opacity = 0.75 });
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (var (key, glyph, name) in HeadStyles)
        {
            var b = new Button
            {
                Width = 36, Height = 32, FontSize = 15, Content = glyph,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(key == style ? 2 : 1),
                BorderBrush = (key == style ? this.FindResource("AccentBrush") : this.FindResource("FrameBorderBrush")) as IBrush,
            };
            ToolTip.SetTip(b, name);
            string k = key;
            b.Click += (_, _) => { style = k; ApplyArrow(x => x.HeadStyle = k, () => _arrowHeadStyle = k); ShowArrowOptions(); };
            styleRow.Children.Add(b);
        }
        panel.Children.Add(styleRow);

        panel.Children.Add(new TextBlock { Text = "Head size", FontSize = 11.5, Opacity = 0.75 });
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var val = new TextBlock { Text = $"{scale:0.##}×", Width = 52, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var minus = new Button { Content = "−", Width = 34, Height = 30 };
        var plus = new Button { Content = "+", Width = 34, Height = 30 };
        void Nudge(double d)
        {
            scale = Math.Clamp(Math.Round((scale + d) / 0.25) * 0.25, 0.5, 3.0);
            val.Text = $"{scale:0.##}×";
            ApplyArrow(x => x.HeadScale = scale, () => _arrowHeadScale = scale);
        }
        minus.Click += (_, _) => Nudge(-0.25);
        plus.Click += (_, _) => Nudge(+0.25);
        sizeRow.Children.Add(minus); sizeRow.Children.Add(val); sizeRow.Children.Add(plus);
        panel.Children.Add(sizeRow);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        MenuFx.AttachFlyout(flyout);
        flyout.ShowAt(ArrowOptsBtn);
    }

    private void ApplyArrow(Action<PdfAnnotation> mutate, Action setDefault)
    {
        setDefault();
        if (_selected is { Kind: PdfAnnotation.Arrow } arr)
        {
            PushUndo();
            mutate(arr);
            foreach (var pv in _pages) RedrawPage(pv);
            SaveNow();
        }
    }

    private void RefreshSwatchRings()
    {
        var accent = this.FindResource("AccentBrush") as IBrush;
        var frame = this.FindResource("FrameBorderBrush") as IBrush;
        int fi = 0;
        foreach (var (_, shades) in ViewModels.MainViewModel.NotebookPalette)
        {
            bool active = shades.Any(s => string.Equals(Rgb(s.Hex), Rgb(_color), StringComparison.OrdinalIgnoreCase));
            if (fi < ColorSwatches.Children.Count && ColorSwatches.Children[fi] is Border b)
            {
                b.BorderThickness = new Thickness(active ? 2.5 : 1);
                b.BorderBrush = active ? accent : frame;
            }
            fi++;
        }
    }

    private void PickColor(string solidHex)
    {
        _color = solidHex;
        RefreshSwatchRings();
        if (_selected is { } cur)
        {
            PushUndo();
            cur.Color = cur.Kind switch
            {
                PdfAnnotation.Arrow => SolidHex(solidHex),
                PdfAnnotation.Highlight => "#66" + Rgb(solidHex),
                _ => solidHex,
            };
            foreach (var pv in _pages) RedrawPage(pv);
            SaveNow();
        }
    }

    private static string Rgb(string hex) { var c = Color.Parse(hex); return $"{c.R:X2}{c.G:X2}{c.B:X2}"; }

    private string HighlightHex => "#66" + Rgb(_color);

    private static bool IsLight(string rgb)
    {
        var c = Color.Parse(rgb.StartsWith('#') ? rgb : "#FF" + rgb);
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150;
    }

    private static bool DarkNoteInk(string noteRgb, bool glassTheme)
    {
        var c = Color.Parse("#FF" + noteRgb);
        double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return lum > (glassTheme ? 183 : 150);
    }

    private Point _dragStart;
    private Border? _dragPreview;
    private Avalonia.Controls.Shapes.Line? _arrowPreview;

    private void OnOverlayPressed(PageView pv, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(pv.Overlay).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(pv.Overlay);
        switch (_tool)
        {
            case Tool.Highlight:
                _dragStart = p;
                _dragPreview = new Border { Background = new SolidColorBrush(Color.Parse(HighlightHex)), IsHitTestVisible = false, CornerRadius = new CornerRadius(5) };
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
            default:
                Select(null, focusEditor: false);
                break;
        }
    }

    private void OnOverlayDoubleTapped(PageView pv, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, pv.Overlay)) return;
        var p = e.GetPosition(pv.Overlay);
        if (_tool == Tool.Note) { CreateTextAnno(pv, p, PdfAnnotation.Note); e.Handled = true; }
        else if (_tool == Tool.Text) { CreateTextAnno(pv, p, PdfAnnotation.TextBox); e.Handled = true; }
    }

    private void OnOverlayMoved(PageView pv, PointerEventArgs e)
    {
        var p = e.GetPosition(pv.Overlay);
        if (_drag is { } a)
        {
            double dx = (p.X - _dragStartPt.X) / pv.Overlay.Width;
            double dy = (p.Y - _dragStartPt.Y) / pv.Overlay.Height;
            ApplyDrag(a, _dragHandle, dx, dy);
            RedrawPage(pv);
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
            _drag = null; e.Pointer.Capture(null);
            if (_dragUndoSnap is { } snap && snap != _annos.ToJson())
            {
                _undoStack.Push(snap);
                _redoStack.Clear();
            }
            _dragUndoSnap = null;
            SaveNow();
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
                double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;
                PushUndo();
                _annos.Items.Add(new PdfAnnotation
                {
                    Page = pv.Index, Kind = PdfAnnotation.Highlight, Color = HighlightHex,
                    X = SnapNorm(x / w, w0), Y = SnapNorm(y / h, h0),
                    W = SnapNorm(pw / w, w0), H = SnapNorm(ph / h, h0),
                });
                RedrawPage(pv); SaveNow();
            }
            else Select(null, focusEditor: false);
        }
        else if (_arrowPreview is not null)
        {
            var s = _arrowPreview.StartPoint; var en = _arrowPreview.EndPoint;
            pv.Overlay.Children.Remove(_arrowPreview); _arrowPreview = null;
            e.Pointer.Capture(null);
            if (w > 0 && h > 0 && Dist(s, en) > 8)
            {
                double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;
                PushUndo();
                var arrow = new PdfAnnotation
                {
                    Page = pv.Index, Kind = PdfAnnotation.Arrow, Color = SolidHex(_color),
                    X = SnapNorm(s.X / w, w0), Y = SnapNorm(s.Y / h, h0),
                    X2 = SnapNorm(en.X / w, w0), Y2 = SnapNorm(en.Y / h, h0),
                    Curved = _arrowCurved, HeadScale = _arrowHeadScale, HeadStyle = _arrowHeadStyle,
                };
                if (arrow.Curved) InitCurve(arrow);
                _annos.Items.Add(arrow);
                RedrawPage(pv); SaveNow();
            }
            else Select(null, focusEditor: false);
        }
    }

    private void CreateTextAnno(PageView pv, Point p, string kind)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        if (w <= 0 || h <= 0) return;
        double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;
        var a = new PdfAnnotation
        {
            Page = pv.Index, Kind = kind, X = SnapNorm(p.X / w, w0), Y = SnapNorm(p.Y / h, h0),
            W = 200 / w0, H = 54 / h0, Text = "",
            Color = kind == PdfAnnotation.Note ? _color : "#00000000",
        };
        PushUndo();
        _annos.Items.Add(a);
        _justAdded = a;

        Select(a, focusEditor: true);
        SaveNow();
    }

    private const double MinW = 0.03, MinH = 0.012;

    private static void InitCurve(PdfAnnotation a)
    {
        static double L(double x1, double x2, double t) => x1 + (x2 - x1) * t;
        a.C1x = L(a.X, a.X2, 0.25); a.C1y = L(a.Y, a.Y2, 0.25);
        a.C2x = L(a.X, a.X2, 0.50); a.C2y = L(a.Y, a.Y2, 0.50);
        a.C3x = L(a.X, a.X2, 0.75); a.C3y = L(a.Y, a.Y2, 0.75);
    }

    private static double SnapNorm(double norm, double pageDimPx) =>
        !SnapToGrid || pageDimPx <= 0 ? norm : Math.Round(norm * pageDimPx / GridStep) * GridStep / pageDimPx;

    private void ApplyDrag(PdfAnnotation a, int handle, double dx, double dy)
    {
        var page = _pages.FirstOrDefault(p => p.Index == a.Page);
        double w0 = page is null ? 0 : page.WPt * PxPerPoint, h0 = page is null ? 0 : page.HPt * PxPerPoint;
        double CX(double v) => SnapNorm(Math.Clamp(v, 0, 1), w0);
        double CY(double v) => SnapNorm(Math.Clamp(v, 0, 1), h0);
        if (a.Kind == PdfAnnotation.Arrow)
        {

            if (handle == 0)
            {
                a.X = CX(_dragOrig.X + dx); a.Y = CY(_dragOrig.Y + dy);
                a.X2 = CX(_dragOrig.X2 + dx); a.Y2 = CY(_dragOrig.Y2 + dy);
                if (a.Curved)
                {
                    a.C1x = CX(_dragOrigC.C1x + dx); a.C1y = CY(_dragOrigC.C1y + dy);
                    a.C2x = CX(_dragOrigC.C2x + dx); a.C2y = CY(_dragOrigC.C2y + dy);
                    a.C3x = CX(_dragOrigC.C3x + dx); a.C3y = CY(_dragOrigC.C3y + dy);
                }
                return;
            }
            if (handle == 1) { a.X = CX(_dragOrig.X + dx); a.Y = CY(_dragOrig.Y + dy); }
            if (handle == 2) { a.X2 = CX(_dragOrig.X2 + dx); a.Y2 = CY(_dragOrig.Y2 + dy); }
            if (handle is 6 or 7 or 8)
            {

                double tH = handle == 6 ? 0.25 : handle == 7 ? 0.50 : 0.75;
                double Wt(double tk) => Math.Exp(-Math.Pow((tk - tH) / 0.34, 2));
                a.C1x = CX(_dragOrigC.C1x + dx * Wt(0.25)); a.C1y = CY(_dragOrigC.C1y + dy * Wt(0.25));
                a.C2x = CX(_dragOrigC.C2x + dx * Wt(0.50)); a.C2y = CY(_dragOrigC.C2y + dy * Wt(0.50));
                a.C3x = CX(_dragOrigC.C3x + dx * Wt(0.75)); a.C3y = CY(_dragOrigC.C3y + dy * Wt(0.75));
            }
            return;
        }
        switch (handle)
        {
            case 0:
                a.X = CX(_dragOrig.X + dx); a.Y = CY(_dragOrig.Y + dy);
                break;
            case 3:
                a.W = Math.Clamp(SnapNorm(_dragOrig.W + dx, w0), MinW, 1 - a.X);
                break;
            case 4:
                a.H = Math.Clamp(SnapNorm(_dragOrig.H + dy, h0), MinH, 1 - a.Y);
                break;
            case 5:
                a.W = Math.Clamp(SnapNorm(_dragOrig.W + dx, w0), MinW, 1 - a.X);
                a.H = Math.Clamp(SnapNorm(_dragOrig.H + dy, h0), MinH, 1 - a.Y);
                break;
        }
    }

    private void StartDrag(PageView pv, PdfAnnotation a, int handle, PointerPressedEventArgs e)
    {
        Select(a, focusEditor: false);
        _dragUndoSnap = _annos.ToJson();
        _drag = a; _dragHandle = handle;
        _dragStartPt = e.GetPosition(pv.Overlay);
        _dragOrig = (a.X, a.Y, a.W, a.H, a.X2, a.Y2);
        _dragOrigC = (a.C1x, a.C1y, a.C2x, a.C2y, a.C3x, a.C3y);
        e.Pointer.Capture(pv.Overlay);
        e.Handled = true;
    }

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
            IsHitTestVisible = true, Cursor = Platform.AdaptiveCursors.For(StandardCursorType.SizeAll),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(selected ? 2.5 : 0),
            BorderBrush = NoteFocusBrush,
            Tag = a,
        };
        Canvas.SetLeft(rect, a.X * w); Canvas.SetTop(rect, a.Y * h);
        rect.Width = a.W * w; rect.Height = a.H * h;
        rect.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 0, e); };
        pv.Overlay.Children.Add(rect);
        if (selected)
        {
            AddDeleteButton(pv, a.X * w + a.W * w, a.Y * h, a);

            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = AccentBrush,
                Stroke = Brushes.White, StrokeThickness = 1.5,
                Cursor = Platform.AdaptiveCursors.For(StandardCursorType.BottomRightCorner),
                Tag = a,
            };
            Canvas.SetLeft(dot, a.X * w + a.W * w - 6); Canvas.SetTop(dot, a.Y * h + a.H * h - 6);
            dot.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 5, e); };
            pv.Overlay.Children.Add(dot);
        }
    }

    private void DrawTextAnno(PageView pv, PdfAnnotation a, bool sticky)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        double w0 = pv.WPt * PxPerPoint, h0 = pv.HPt * PxPerPoint;
        bool selected = ReferenceEquals(a, _selected);
        bool glassTheme = Services.ThemeManager.Current.GlassWindow;

        string noteRgb = sticky ? Rgb(a.Color) : "10151E";
        bool darkInk = sticky && DarkNoteInk(noteRgb, glassTheme);
        bool whiteChrome = sticky && !darkInk;
        var inkBrush = new SolidColorBrush(Color.Parse(
            sticky ? (darkInk ? "#1A1D26" : "#F2FFFFFF") : "#E610151E"));

        var editor = BuildNoteEditor(a, inkBrush);
        if (_editors.TryGetValue(a, out var stale)) stale.Document = new RichDocument();
        _editors[a] = editor;

        string pillHex = !sticky ? "#4D10151E"
            : whiteChrome ? (selected ? "#52FFFFFF" : "#30FFFFFF")
            : (selected ? "#59000000" : "#33000000");
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
            Background = sticky ? new SolidColorBrush(Color.Parse(whiteChrome ? "#12FFFFFF" : "#14000000")) : Brushes.Transparent,
            Child = gripBar,
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        DockPanel.SetDock(grip, Dock.Top);
        var content = new DockPanel();
        content.Children.Add(grip);
        content.Children.Add(editor);

        string closeHex = !sticky ? "#8C10151E" : whiteChrome ? "#8CFFFFFF" : "#8C000000";
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
        close.PointerPressed += (_, e) => e.Handled = true;
        close.PointerReleased += (_, e) => { Delete(a); e.Handled = true; };

        var resizeRight = new Border
        {
            Width = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        resizeRight.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 3, e); };
        var resizeBottom = new Border
        {
            Height = 8, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
        };
        resizeBottom.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 4, e); };
        var resizeCorner = new Border
        {
            Width = 15, Height = 15,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
        };
        resizeCorner.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 5, e); };

        Border MakeRing(double radius, string? hairHex) => new()
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(selected ? 2.5 : 1),
            BorderBrush = selected
                ? NoteFocusBrush
                : hairHex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hairHex)),
            IsHitTestVisible = false,
        };

        ImageBrush? backdropBrush = null;
        Border box;
        if (sticky)
        {

            var layers = new Panel();
            if (glassTheme)
            {
                backdropBrush = pv.Img.Source is Bitmap bmp ? new ImageBrush(bmp) { Stretch = Stretch.Fill } : null;
                layers.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = (IBrush?)backdropBrush ?? new SolidColorBrush(Color.Parse("#301A2030")),
                    Effect = new BlurEffect { Radius = 16 },
                    IsHitTestVisible = false,
                });
                layers.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.Parse("#B3" + noteRgb)),
                    IsHitTestVisible = false,
                });
            }
            else
                layers.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.Parse("#FF" + noteRgb)),
                    IsHitTestVisible = false,
                });
            layers.Children.Add(content);
            layers.Children.Add(resizeRight);
            layers.Children.Add(resizeBottom);
            layers.Children.Add(resizeCorner);
            layers.Children.Add(MakeRing(10, whiteChrome ? "#33FFFFFF" : "#33000000"));
            layers.Children.Add(close);

            var clip = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = layers };
            box = new Border
            {
                Width = a.W * w0, MinHeight = a.H * h0,
                CornerRadius = new CornerRadius(10), Child = clip,
                BoxShadow = BoxShadows.Parse(selected ? "0 9 28 0 #80000000" : "0 4 16 0 #59000000"),
                BorderThickness = new Thickness(0),
                Transitions = new Transitions
                {
                    new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(140) },
                },
            };
        }
        else
        {

            var layers = new Panel();
            layers.Children.Add(content);
            layers.Children.Add(resizeRight);
            layers.Children.Add(resizeBottom);
            layers.Children.Add(resizeCorner);
            layers.Children.Add(MakeRing(8, null));
            layers.Children.Add(close);
            box = new Border
            {
                Width = a.W * w0, MinHeight = a.H * h0,
                CornerRadius = new CornerRadius(8), Child = layers,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
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

            if (!ReferenceEquals(_selected, a)) Select(a, focusEditor: false);
            e.Handled = true;
        };

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
        if (a == _justAdded) { _justAdded = null; Motion.ScaleIn(box, 0.9, 100); }
    }

    private void DrawArrow(PageView pv, PdfAnnotation a)
    {
        double w = pv.Overlay.Width, h = pv.Overlay.Height;
        bool selected = ReferenceEquals(a, _selected);
        var brush = new SolidColorBrush(SolidColor(a.Color));
        double thick = ArrowThickness;

        var pts = ArrowPoints(a, w, h);
        var s = pts[0]; var en = pts[^1];
        var from = pts.Count >= 2 ? pts[^2] : s;
        string style = string.IsNullOrEmpty(a.HeadStyle) ? "triangle" : a.HeadStyle!;
        double headLen = ArrowHeadLen * (a.HeadScale <= 0 ? 1.0 : a.HeadScale);

        var hit = new Shapes.Path
        {
            Data = PolyGeometry(pts), Stroke = Brushes.Transparent, StrokeThickness = Math.Max(16, thick * 5),
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = true, Cursor = Platform.AdaptiveCursors.For(StandardCursorType.SizeAll), Tag = a,
        };
        hit.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, 0, e); };

        var shaftPts = new List<Point>(pts);
        if (style is "triangle" or "diamond" or "circle" && pts.Count >= 2)
        {
            double ang = Math.Atan2(en.Y - from.Y, en.X - from.X);
            double back = headLen * (style == "circle" ? 0.5 : 0.85);
            shaftPts[^1] = new Point(en.X - back * Math.Cos(ang), en.Y - back * Math.Sin(ang));
        }
        var shaft = new Shapes.Path
        {
            Data = PolyGeometry(shaftPts), Stroke = brush, StrokeThickness = thick,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false, Tag = a,
        };
        pv.Overlay.Children.Add(hit);
        pv.Overlay.Children.Add(shaft);
        if (ArrowHeadShape(style, en, from, headLen, thick, brush) is { } head) pv.Overlay.Children.Add(head);

        if (selected)
        {
            if (a.Curved)
            {
                foreach (var (cx, cy, handle) in new[] { (a.C1x, a.C1y, 6), (a.C2x, a.C2y, 7), (a.C3x, a.C3y, 8) })
                    pv.Overlay.Children.Add(Handle(pv, a, new Point(cx * w, cy * h), handle, control: true));
            }
            pv.Overlay.Children.Add(Handle(pv, a, s, 1, control: false));
            pv.Overlay.Children.Add(Handle(pv, a, en, 2, control: false));
            AddDeleteButton(pv, Math.Max(s.X, en.X), Math.Min(s.Y, en.Y), a);
        }
    }

    private static List<Point> ArrowPoints(PdfAnnotation a, double w, double h)
    {
        var p0 = new Point(a.X * w, a.Y * h);
        var p4 = new Point(a.X2 * w, a.Y2 * h);
        if (!a.Curved) return new List<Point> { p0, p4 };
        var p1 = new Point(a.C1x * w, a.C1y * h);
        var p2 = new Point(a.C2x * w, a.C2y * h);
        var p3 = new Point(a.C3x * w, a.C3y * h);
        const int n = 32;
        var o = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            double b0 = u * u * u * u, b1 = 4 * u * u * u * t, b2 = 6 * u * u * t * t, b3 = 4 * u * t * t * t, b4 = t * t * t * t;
            o.Add(new Point(
                b0 * p0.X + b1 * p1.X + b2 * p2.X + b3 * p3.X + b4 * p4.X,
                b0 * p0.Y + b1 * p1.Y + b2 * p2.Y + b3 * p3.Y + b4 * p4.Y));
        }
        return o;
    }

    private static Geometry PolyGeometry(IReadOnlyList<Point> pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], false);
        for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i]);
        c.EndFigure(false);
        return g;
    }

    private static Control? ArrowHeadShape(string style, Point tip, Point from, double len, double thick, IBrush brush)
    {
        if (style == "none") return null;
        double ang = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        Point P(double back, double side) => new(
            tip.X - back * Math.Cos(ang) - side * Math.Sin(ang),
            tip.Y - back * Math.Sin(ang) + side * Math.Cos(ang));
        switch (style)
        {
            case "open":
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                { c.BeginFigure(P(len, len * 0.52), false); c.LineTo(tip); c.LineTo(P(len, -len * 0.52)); c.EndFigure(false); }
                return new Shapes.Path
                {
                    Data = geo, Stroke = brush, StrokeThickness = Math.Max(thick, len * 0.17),
                    StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false,
                };
            }
            case "diamond":
                return new Shapes.Polygon
                {
                    Fill = brush, IsHitTestVisible = false,
                    Points = new Avalonia.Collections.AvaloniaList<Point> { tip, P(len * 0.5, len * 0.42), P(len, 0), P(len * 0.5, -len * 0.42) },
                };
            case "circle":
            {
                double d = len * 0.92;
                var el = new Shapes.Ellipse { Width = d, Height = d, Fill = brush, IsHitTestVisible = false };
                Canvas.SetLeft(el, tip.X - d / 2); Canvas.SetTop(el, tip.Y - d / 2);
                return el;
            }
            default:
                return new Shapes.Polygon
                {
                    Fill = brush, IsHitTestVisible = false,
                    Points = new Avalonia.Collections.AvaloniaList<Point> { tip, P(len, len * 0.5), P(len, -len * 0.5) },
                };
        }
    }

    private double ArrowThickness => Math.Clamp(3 * _zoom, 2.0, 10.0);
    private double ArrowHeadLen => Math.Clamp(22 * _zoom, 15.0, 64.0);

    private Control Handle(PageView pv, PdfAnnotation a, Point at, int handle, bool control)
    {
        double d = control ? 13 : 12;
        var dot = new Shapes.Ellipse
        {
            Width = d, Height = d,
            Fill = control ? new SolidColorBrush(Color.Parse("#F0FFFFFF")) : AccentBrush,
            Stroke = control ? AccentBrush : Brushes.White, StrokeThickness = control ? 2 : 1.5,
            Cursor = new Cursor(StandardCursorType.Hand), Tag = a,
        };
        Canvas.SetLeft(dot, at.X - d / 2); Canvas.SetTop(dot, at.Y - d / 2);
        dot.PointerPressed += (_, e) => { if (Left(e, pv)) StartDrag(pv, a, handle, e); };
        return dot;
    }

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
            Cursor = new Cursor(StandardCursorType.Hand), Child = glyph, Tag = a,
        };
        btn.PointerEntered += (_, _) => { btn.Background = new SolidColorBrush(Color.Parse("#E6E5484D")); glyph.Stroke = Brushes.White; };
        btn.PointerExited += (_, _) => { btn.Background = restBg; glyph.Stroke = restFg; };
        ToolTip.SetTip(btn, "Delete");
        Canvas.SetLeft(btn, x - d / 2); Canvas.SetTop(btn, y - d / 2);
        btn.PointerPressed += (_, e) => { Delete(a); e.Handled = true; };
        pv.Overlay.Children.Add(btn);
    }

    private RichTextEditor BuildNoteEditor(PdfAnnotation a, IBrush foreground)
    {
        var editor = NewNoteEditor(DocFor(a), foreground);
        editor.GotFocus += (_, _) =>
        {
            if (!ReferenceEquals(_selected, a)) Select(a, focusEditor: true);
            else ShowFmtBar(editor);
        };
        return editor;
    }

    private RichTextEditor NewNoteEditor(RichDocument doc, IBrush foreground) => new()
    {
        Document = doc,
        Margin = new Thickness(10, 4, 10, 8),
        Foreground = foreground,
        CaretBrush = AccentBrush,
        LinkBrush = AccentBrush,
        SelectionBrush = new SolidColorBrush(Color.Parse("#554DA6FF")),
        FontFamily = Services.AppFonts.Family(RichTextEditor.EditorFontPref),
        FontSize = 13,
    };

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
        if (text.Length == 0) return doc;
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

    private void ShowFmtBar(RichTextEditor editor) { FmtBar.Target = editor; FmtBar.IsEnabled = true; FmtBar.Opacity = 1; }
    private void HideFmtBar() { FmtBar.Target = null; FmtBar.IsEnabled = false; FmtBar.Opacity = 0.45; }

    private void Delete(PdfAnnotation a)
    {
        PushUndo();
        _annos.Items.Remove(a);
        _docs.Remove(a);
        _editors.Remove(a);
        if (ReferenceEquals(_selected, a)) { _selected = null; HideFmtBar(); }
        SaveNow();

        var pv = _pages.FirstOrDefault(p => p.Index == a.Page);
        var targets = pv?.Overlay.Children.OfType<Control>().Where(c => ReferenceEquals(c.Tag, a)).ToList();
        if (pv is null || targets is null || targets.Count == 0)
        {
            foreach (var p in _pages) RedrawPage(p);
            return;
        }
        foreach (var t in targets)
        {
            if (t is Border { Child: Control inner } card && card.RenderTransform is not null)
            {

                card.BoxShadow = default;
                card.BorderBrush = Brushes.Transparent;
                Motion.CollapseOut(inner, 90);
            }
            else Motion.CollapseOut(t, 90);
        }
        DispatcherTimer.RunOnce(() => { foreach (var p in _pages) RedrawPage(p); }, TimeSpan.FromMilliseconds(100));
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
        else
        {
            HideFmtBar();
            if (a is not null) Focus();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox or RichTextEditor) return;
        bool ctrl = Services.Keymap.HasCommand(e.KeyModifiers);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && e.Key == Key.Z && !shift) { Undo(); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) { Redo(); e.Handled = true; return; }
        if (_selected is { } a && (e.Key == Key.Delete || e.Key == Key.Back))
        {
            Delete(a);
            e.Handled = true;
        }
    }

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string? _dragUndoSnap;

    private void PushUndo()
    {
        _undoStack.Push(_annos.ToJson());
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(_annos.ToJson());
        RestoreSnapshot(_undoStack.Pop());
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(_annos.ToJson());
        RestoreSnapshot(_redoStack.Pop());
    }

    private void RestoreSnapshot(string json)
    {

        var restored = PdfAnnotationDoc.FromJson(json);
        _annos.Items.Clear();
        _annos.Items.AddRange(restored.Items);
        _docs.Clear();
        _editors.Clear();
        _selected = null;
        _drag = null;
        HideFmtBar();
        foreach (var pv in _pages) RedrawPage(pv);
        SaveNow();
    }

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
        c.DrawRoundRect(r, 3.5f, 3.5f, p);
    }

    private static void FlattenArrow(SkiaSharp.SKCanvas c, PdfAnnotation a, float wpt, float hpt)
    {
        var pts = ArrowPoints(a, wpt, hpt);
        var tip = pts[^1]; var from = pts.Count >= 2 ? pts[^2] : pts[0];
        var col = SkColor(SolidHex(a.Color));
        const float thick = 2.4f;
        string style = string.IsNullOrEmpty(a.HeadStyle) ? "triangle" : a.HeadStyle!;
        double headLen = 15.0 * (a.HeadScale <= 0 ? 1.0 : a.HeadScale);

        var shaftPts = new List<Point>(pts);
        if (style is "triangle" or "diamond" or "circle" && pts.Count >= 2)
        {
            double ang0 = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
            double back = headLen * (style == "circle" ? 0.5 : 0.85);
            shaftPts[^1] = new Point(tip.X - back * Math.Cos(ang0), tip.Y - back * Math.Sin(ang0));
        }
        using var stroke = new SkiaSharp.SKPaint
        {
            Color = col, IsAntialias = true, StrokeWidth = thick,
            StrokeCap = SkiaSharp.SKStrokeCap.Round, StrokeJoin = SkiaSharp.SKStrokeJoin.Round,
            Style = SkiaSharp.SKPaintStyle.Stroke,
        };
        using (var sp = new SkiaSharp.SKPath())
        {
            sp.MoveTo((float)shaftPts[0].X, (float)shaftPts[0].Y);
            for (int i = 1; i < shaftPts.Count; i++) sp.LineTo((float)shaftPts[i].X, (float)shaftPts[i].Y);
            c.DrawPath(sp, stroke);
        }

        if (style == "none") return;
        double ang = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        SkiaSharp.SKPoint HP(double back, double side) => new(
            (float)(tip.X - back * Math.Cos(ang) - side * Math.Sin(ang)),
            (float)(tip.Y - back * Math.Sin(ang) + side * Math.Cos(ang)));
        var t = new SkiaSharp.SKPoint((float)tip.X, (float)tip.Y);
        if (style == "open")
        {
            using var op = new SkiaSharp.SKPaint
            {
                Color = col, IsAntialias = true, StrokeWidth = (float)Math.Max(thick, headLen * 0.17),
                StrokeCap = SkiaSharp.SKStrokeCap.Round, StrokeJoin = SkiaSharp.SKStrokeJoin.Round,
                Style = SkiaSharp.SKPaintStyle.Stroke,
            };
            using var hp = new SkiaSharp.SKPath();
            var l = HP(headLen, headLen * 0.52); var r = HP(headLen, -headLen * 0.52);
            hp.MoveTo(l); hp.LineTo(t); hp.LineTo(r);
            c.DrawPath(hp, op);
            return;
        }
        if (style == "circle")
        {
            using var fillc = new SkiaSharp.SKPaint { Color = col, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
            c.DrawCircle(t, (float)(headLen * 0.46), fillc);
            return;
        }
        using var fill = new SkiaSharp.SKPaint { Color = col, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
        using var path = new SkiaSharp.SKPath();
        if (style == "diamond")
        { path.MoveTo(t); path.LineTo(HP(headLen * 0.5, headLen * 0.42)); path.LineTo(HP(headLen, 0)); path.LineTo(HP(headLen * 0.5, -headLen * 0.42)); }
        else
        { path.MoveTo(t); path.LineTo(HP(headLen, headLen * 0.5)); path.LineTo(HP(headLen, -headLen * 0.5)); }
        path.Close();
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

        if (sticky && pageBmp is not null && Services.ThemeManager.Current.GlassWindow)
        {
            c.Save();
            using var rr = new SkiaSharp.SKRoundRect(rect, 6.5f, 6.5f);
            c.ClipRoundRect(rr, antialias: true);
            using var blur = new SkiaSharp.SKPaint { ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(5, 5) };
            c.DrawBitmap(pageBmp, new SkiaSharp.SKRect(0, 0, wpt, hpt), blur);
            c.Restore();
        }

        c.DrawImage(img, rect, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
    }

    private byte[]? RenderNoteImage(PdfAnnotation a, bool sticky, out double pxW, out double pxH)
    {
        pxW = pxH = 0;
        var page = _pages.FirstOrDefault(p => p.Index == a.Page);
        if (page is null) return null;
        double w0 = page.WPt * PxPerPoint;
        pxW = Math.Max(8, a.W * w0);
        bool glassTheme = Services.ThemeManager.Current.GlassWindow;
        string noteRgb = sticky ? Rgb(a.Color) : "10151E";
        bool darkInk = sticky && DarkNoteInk(noteRgb, glassTheme);
        bool whiteChrome = sticky && !darkInk;
        var inkBrush = new SolidColorBrush(Color.Parse(
            sticky ? (darkInk ? "#1A1D26" : "#F2FFFFFF") : "#E610151E"));
        var doc = !string.IsNullOrEmpty(a.Rich) ? RichDocJson.FromJson(a.Rich) : LegacyDoc(a);
        var editor = NewNoteEditor(doc, inkBrush);

        var grip = new Border
        {
            Height = 16, CornerRadius = new CornerRadius(10, 10, 0, 0),
            Background = sticky ? new SolidColorBrush(Color.Parse(whiteChrome ? "#12FFFFFF" : "#14000000")) : Brushes.Transparent,
            Child = sticky
                ? new Border
                {
                    Width = 38, Height = 4, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.Parse(whiteChrome ? "#30FFFFFF" : "#33000000")),
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
            layers.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse((glassTheme ? "#B3" : "#FF") + noteRgb)),
            });
            layers.Children.Add(content);
            box = new Border
            {
                Width = pxW,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse(whiteChrome ? "#33FFFFFF" : "#33000000")),
                Child = layers,
            };
        }
        else
        {

            box = new Border { Width = pxW, Child = content };
        }

        Avalonia.Media.TextOptions.SetTextRenderingMode(box, Avalonia.Media.TextRenderingMode.Antialias);
        box.Measure(new Size(pxW, double.PositiveInfinity));
        pxH = Math.Max(8, Math.Ceiling(box.DesiredSize.Height));
        box.Arrange(new Rect(0, 0, pxW, pxH));

        const double ss = 3.5;
        var px = new PixelSize((int)Math.Ceiling(pxW * ss), (int)Math.Ceiling(pxH * ss));
        using var rtb = new RenderTargetBitmap(px, new Vector(96 * ss, 96 * ss));
        rtb.Render(box);
        using var mm = new MemoryStream();
        rtb.Save(mm);
        return mm.ToArray();
    }

    private static bool Left(PointerPressedEventArgs e, PageView pv) =>
        e.GetCurrentPoint(pv.Overlay).Properties.IsLeftButtonPressed;
    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
        if (!_loaded || _sidecarPath.Length == 0) return;
        try { File.WriteAllText(_sidecarPath, _annos.ToJson()); }
        catch {  }
        PdfAnnotationHub.NotifyChanged(_pdfPath, this);
    }
}
