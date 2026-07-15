using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumenotepad.Editor;

/// <summary>The freeform page canvas (the OneNote model): any number of movable, resizable note
/// containers, each holding its own rich document. Click empty space to start a new container
/// there; a container that loses focus while still empty evaporates. Deleted containers go to the
/// page's history and can be dragged back onto the canvas.</summary>
public sealed class NoteCanvas : Panel
{
    /// <summary>In-process drag-drop format for restoring a box from the deleted history.</summary>
    public static readonly DataFormat<NoteBox> TrashFormat =
        DataFormat.CreateInProcessFormat<NoteBox>("lumenotepad-trash-box");

    /// <summary>Note-container corner radius (M8 Part 6 roundness pref; default 9). Views read it
    /// at construction — pushing a new value takes effect on the next canvas rebuild.</summary>
    public static double NoteRadiusPref = 9;

    private CanvasDocument? _doc;
    private bool _canResize = true;

    /// <summary>The page's canvas document; setting it rebuilds all container views.</summary>
    public CanvasDocument? Document
    {
        get => _doc;
        set { _doc = value; Rebuild(); }
    }

    /// <summary>Whether containers show resize handles ("Resizable pages" preference).</summary>
    public bool CanResize
    {
        get => _canResize;
        set
        {
            _canResize = value;
            foreach (var child in Children)
                if (child is NoteBoxView v) v.RefreshChrome();
        }
    }

    /// <summary>Whether deleting a container keeps it in the page history ("Deleted pages history" preference).</summary>
    public bool HistoryEnabled { get; set; } = true;

    /// <summary>Asked before a container is deleted via its ✕ button / menu; null = no prompt.</summary>
    public Func<Task<bool>>? ConfirmDelete { get; set; }

    /// <summary>"Snap to grid" preference: drag/resize/placement land on the 20px cell.</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>"Create notes with double-click" preference: a bare-canvas single click does nothing.</summary>
    public bool CreateOnDoubleClick { get; set; }

    // The bottom guide layer: grid-style paper background + page-style guide lines (M9).
    private readonly GuideLayer _guides = new();
    // The mindmap connector layer: above the guides, under every container (M9 Part 5).
    private readonly LinkLayer _links = new();
    private string _pageStyle = PageStyles.Freeform;

    /// <summary>Push the page's effective styles (grid background, method guides, apply mode).</summary>
    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _pageStyle = pageStyle;
        _guides.SetStyles(gridStyle, pageStyle, mode);
    }

    /// <summary>The visible page area — guide dividers anchor to it (MainView pushes it on layout).</summary>
    public void SetViewport(Size viewport)
    {
        _guides.Viewport = viewport;
        _guides.InvalidateVisual();
    }

    /// <summary>The editor of the most recently focused container (what the toolbar targets).</summary>
    public RichTextEditor? ActiveEditor { get; private set; }
    public event Action<RichTextEditor?>? ActiveEditorChanged;

    /// <summary>Raised when the deleted history changes (delete/restore) so panels can refresh.</summary>
    public event Action? TrashChanged;

    public NoteCanvas()
    {
        // An un-rendered control is not hit-testable — bare-canvas clicks would fall through.
        Background = Brushes.Transparent;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(TrashFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var box = e.DataTransfer.TryGetValue(TrashFormat);
            if (box is null || _doc is null || !_doc.Trash.Contains(box)) return;
            var p = e.GetPosition(this);
            RestoreBox(box, p.X - 11, p.Y - 16);
            e.Handled = true;
        });
    }

    // A quiet starter hint on empty pages, so a blank canvas never feels dead. A child element
    // because Panel.Render is sealed (same lesson as the TextPresenter selection underlay).
    private readonly TextBlock _hint = new()
    {
        Text = "Click anywhere and start typing",
        FontSize = 13.5, IsHitTestVisible = false, IsVisible = false,
    };

    private void Rebuild()
    {
        Children.Clear();
        SetActive(null);
        Children.Add(_guides);         // first child = bottom of z-order: under every container
        _guides.Refresh();             // theme changes arrive as a Document reset — re-tint here
        _links.Doc = _doc;             // connectors run under the bubbles, over the paper
        _links.Resolve = BoxRect;
        _links.Refresh();
        Children.Add(_links);
        Children.Add(_hint);
        if (_doc is not null)
            foreach (var box in _doc.Boxes)
                Children.Add(new NoteBoxView(this, box));
        UpdateHint();
        InvalidateMeasure();
    }

    /// <summary>A box's current on-screen rect (the arranged view), for the link layer.</summary>
    private Rect? BoxRect(NoteBox box)
    {
        foreach (var child in Children)
            if (child is NoteBoxView v && ReferenceEquals(v.Box, box))
                return v.Bounds;
        return null;
    }

    private void UpdateHint()
    {
        _hint.Foreground = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.PaperTextMuted));
        _hint.IsVisible = _doc is not null && _doc.Boxes.Count == 0;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0, h = 0;
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v) { child.Measure(Size.Infinity); continue; }
            v.Measure(new Size(v.Box.Width, double.PositiveInfinity));
            w = Math.Max(w, v.Box.X + v.Box.Width);
            h = Math.Max(h, v.Box.Y + Math.Max(v.DesiredSize.Height, v.Box.H));
        }
        return new Size(w + 220, h + 320);        // breathing room so the page can always grow by clicking
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, _guides) || ReferenceEquals(child, _links))
            {
                child.Arrange(new Rect(finalSize));     // full-page layers under every container
                continue;
            }
            if (child is not NoteBoxView v)
            {
                var d = child.DesiredSize;
                child.Arrange(new Rect(
                    Math.Max(0, (finalSize.Width - d.Width) / 2),
                    Math.Min(170, finalSize.Height / 3), d.Width, d.Height));
                continue;
            }
            v.Arrange(new Rect(v.Box.X, v.Box.Y, v.Box.Width, Math.Max(v.DesiredSize.Height, v.Box.H)));
        }
        _links.InvalidateVisual();               // connectors follow bubbles live during drags
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Only clicks on bare canvas start a container — clicks inside one bubble with another Source.
        if (_doc is null || !ReferenceEquals(e.Source, this)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (CreateOnDoubleClick && e.ClickCount < 2) return;
        var p = e.GetPosition(this);
        double bx = p.X - 11, by = p.Y - 16;
        if (SnapToGrid) { bx = Math.Max(0, GridMath.Snap(bx)); by = Math.Max(0, GridMath.Snap(by)); }
        var view = AddBoxView(_doc.AddBox(bx, by, Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640)));
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
        e.Handled = true;
    }

    private NoteBoxView AddBoxView(NoteBox box)
    {
        var view = new NoteBoxView(this, box);
        Children.Add(view);
        UpdateHint();
        InvalidateMeasure();
        return view;
    }

    /// <summary>Bring a box back from the deleted history, optionally at a new spot.</summary>
    public void RestoreBox(NoteBox box, double? x = null, double? y = null)
    {
        if (_doc is null) return;
        _doc.RestoreFromTrash(box, x, y);
        AddBoxView(box);
        TrashChanged?.Invoke();
    }

    internal void SetActive(RichTextEditor? editor)
    {
        if (ReferenceEquals(ActiveEditor, editor)) return;
        ActiveEditor = editor;
        ActiveEditorChanged?.Invoke(editor);
    }

    /// <summary>✕ button / context-menu delete: confirm, then trash (or remove outright when the
    /// history is disabled). Empty boxes skip the prompt — there is nothing to lose.</summary>
    internal async void RequestDelete(NoteBoxView view)
    {
        if (view.Box.Locked) return;                       // rigid starters can't be deleted
        if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
        if (!view.Box.IsEmpty && ConfirmDelete is not null && !await ConfirmDelete()) return;
        if (HistoryEnabled && !view.Box.IsEmpty)
        {
            _doc.DeleteToTrash(view.Box);
            DetachView(view);
            TrashChanged?.Invoke();
        }
        else
        {
            DeleteBoxPermanently(view);
        }
    }

    internal void DeleteBoxPermanently(NoteBoxView view)
    {
        _doc?.RemoveBox(view.Box);
        DetachView(view);
    }

    private void DetachView(NoteBoxView view)
    {
        Children.Remove(view);
        if (ReferenceEquals(ActiveEditor, view.Editor)) SetActive(null);
        UpdateHint();
        InvalidateMeasure();
    }

    /// <summary>Mindmap linking (M9 Part 5): a MOVE drag that ends with the bubble overlapping
    /// another toggles a link between them (drop again to unlink). Only while the page's effective
    /// style is Mindmap — every other style keeps plain drags. A heavy overlap nudges the dropped
    /// bubble past the target's nearest edge so both stay visible with the connector showing.</summary>
    internal void OnBoxDragEnd(NoteBoxView view)
    {
        if (_doc is null || _pageStyle != PageStyles.Mindmap) return;
        NoteBoxView? hit = null;
        foreach (var child in Children)                    // later child = higher z — keep the last hit
            if (child is NoteBoxView v && !ReferenceEquals(v, view) && v.Bounds.Intersects(view.Bounds))
                hit = v;
        if (hit is null) return;
        bool linked = _doc.ToggleLink(view.Box, hit.Box);
        if (linked) NudgeApart(view, hit);
        _links.InvalidateVisual();
    }

    private void NudgeApart(NoteBoxView moved, NoteBoxView target)
    {
        var a = moved.Bounds;
        var b = target.Bounds;
        var overlap = a.Intersect(b);
        if (overlap.Width * overlap.Height < 0.35 * a.Width * a.Height) return;   // just touching — leave it
        if (Math.Abs(a.Center.X - b.Center.X) >= Math.Abs(a.Center.Y - b.Center.Y))
            moved.Box.X = a.Center.X >= b.Center.X ? b.Right + 24 : Math.Max(0, b.X - a.Width - 24);
        else
            moved.Box.Y = a.Center.Y >= b.Center.Y ? b.Bottom + 24 : Math.Max(0, b.Y - a.Height - 24);
        InvalidateMeasure();
    }

    /// <summary>OneNote behavior: an empty container evaporates once focus has settled elsewhere.
    /// Deferred a beat so the click that stole focus can land first.</summary>
    internal void OnEditorLostFocus(NoteBoxView view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
            if (!view.Box.IsEmpty || view.Box.Locked || view.IsKeyboardFocusWithin) return;
            DeleteBoxPermanently(view);
        }, DispatcherPriority.Background);
    }
}

/// <summary>One container: hover/focus chrome, a top drag-grip, a ✕ delete button, the editor, and
/// right/bottom/corner resize handles. Geometry lives on the NoteBox model; the canvas arranges
/// from it, so drags just mutate the model and re-measure.</summary>
internal sealed class NoteBoxView : Panel
{
    private enum DragMode { Move, Width, Height, Both }

    // Paper-region theme tokens, read at construction (theme changes rebuild the canvas views).
    private readonly IBrush HoverBorder;
    private readonly IBrush FocusBorder;
    private readonly IBrush GripFill;
    private readonly IBrush GripBarFill;
    private readonly IBrush CloseFg;
    private static readonly IBrush CloseHoverBg = new SolidColorBrush(Color.Parse("#66E81123"));

    internal NoteBox Box { get; }
    internal RichTextEditor Editor { get; }

    private readonly NoteCanvas _canvas;
    private readonly Border _chrome;
    private readonly Border _grip;
    private readonly Border _gripBar;
    private readonly Border _close;
    private readonly TextBlock _closeGlyph;
    private readonly Border _resizeRight;
    private readonly Border _resizeBottom;
    private readonly Border _resizeCorner;
    private bool _hover;

    public NoteBoxView(NoteCanvas canvas, NoteBox box)
    {
        _canvas = canvas;
        Box = box;

        var t = Services.ThemeManager.Current;
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        HoverBorder = B(t.NoteChromeHover);
        FocusBorder = B(t.NoteChromeFocus);
        GripFill = B(t.NoteGripFill);
        GripBarFill = B(t.NoteGripBar);
        CloseFg = B(Services.ThemePalettes.Alpha(t.PaperText, 0x8C));

        Editor = new RichTextEditor
        {
            Document = box.Doc, Margin = new Thickness(10, 3, 10, 9),
            Foreground = B(t.PaperText),
            CaretBrush = B(RichTextEditor.CaretColorOverride ?? t.Accent),
            LinkBrush = B(t.Accent),
            SelectionBrush = B(t.FieldSelection),
            FontFamily = Services.AppFonts.Family(RichTextEditor.EditorFontPref),
            FontSize = Math.Clamp(RichTextEditor.EditorFontSizePref, 11, 24),
            ParagraphSpacing = 4 * Math.Clamp(RichTextEditor.ParagraphSpacingScalePref, 0.5, 3),
        };

        _gripBar = new Border
        {
            Width = 38, Height = 4, CornerRadius = new CornerRadius(2), Background = GripBarFill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        double r = NoteCanvas.NoteRadiusPref;
        _grip = new Border
        {
            Height = 17, Background = Brushes.Transparent, Child = _gripBar,
            CornerRadius = new CornerRadius(r, r, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        DockPanel.SetDock(_grip, Dock.Top);

        var body = new DockPanel();
        body.Children.Add(_grip);
        body.Children.Add(Editor);

        _chrome = new Border
        {
            Child = body, CornerRadius = new CornerRadius(r),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
        };

        _closeGlyph = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 7.5, Foreground = CloseFg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _close = new Border
        {
            Width = 17, Height = 17, CornerRadius = new CornerRadius(0, r, 0, 6),
            Background = Brushes.Transparent, Child = _closeGlyph, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _close.PointerEntered += (_, _) => { _close.Background = CloseHoverBg; _closeGlyph.Foreground = Brushes.White; };
        _close.PointerExited += (_, _) => { _close.Background = Brushes.Transparent; _closeGlyph.Foreground = CloseFg; };
        _close.PointerPressed += (_, e) => e.Handled = true;      // don't start a grip drag from the ✕
        _close.PointerReleased += (_, e) => { _canvas.RequestDelete(this); e.Handled = true; };

        _resizeRight = new Border
        {
            Width = 7, HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        _resizeBottom = new Border
        {
            Height = 7, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
        };
        _resizeCorner = new Border
        {
            Width = 14, Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
        };

        Children.Add(_chrome);
        Children.Add(_resizeRight);
        Children.Add(_resizeBottom);
        Children.Add(_resizeCorner);
        Children.Add(_close);

        PointerEntered += (_, _) => { _hover = true; RefreshChrome(); };
        PointerExited += (_, _) => { _hover = false; RefreshChrome(); };
        Editor.GotFocus += (_, _) => { _canvas.SetActive(Editor); RefreshChrome(); };
        Editor.LostFocus += (_, _) => { RefreshChrome(); _canvas.OnEditorLostFocus(this); };

        WireDrag(_grip, DragMode.Move);
        WireDrag(_resizeRight, DragMode.Width);
        WireDrag(_resizeBottom, DragMode.Height);
        WireDrag(_resizeCorner, DragMode.Both);

        _grip.ContextRequested += (_, e) =>
        {
            var menu = new ContextMenu();
            var del = new MenuItem { Header = "Delete container" };
            del.Click += (_, _) => _canvas.RequestDelete(this);
            menu.Items.Add(del);
            Views.MenuFx.Attach(menu);     // rise-in + Lumen glass-variant popup acrylic
            menu.Open(_grip);
            e.Handled = true;
        };

        RefreshChrome();
    }

    internal void FocusEditor() => Editor.Focus();

    internal void RefreshChrome()
    {
        bool focused = Editor.IsFocused;
        // Capturing the pointer on a resize/grip handle drops the box's own IsPointerOver (so PointerExited
        // fires and would blank the border mid-drag) — treat an active drag as "keep the chrome lit".
        bool active = _hover || focused || _dragging;
        _chrome.BorderBrush = _dragging || focused ? FocusBorder : _hover ? HoverBorder : Brushes.Transparent;
        _grip.Background = active ? GripFill : Brushes.Transparent;
        _gripBar.IsVisible = active;
        _close.IsVisible = active && !Box.Locked;
        // Hidden handles are also not hit-testable — the "Resizable pages" preference off = no resizing.
        _resizeRight.IsVisible = _resizeBottom.IsVisible = _resizeCorner.IsVisible =
            _canvas.CanResize && !Box.Locked;
    }

    private Point _dragStart;
    private (double X, double Y, double W, double H) _dragOrigin;
    private bool _dragging;

    private void WireDrag(Control handle, DragMode mode)
    {
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            if (Box.Locked) return;                        // rigid starters: no move, no resize
            _dragStart = e.GetPosition(_canvas);
            // Height origin = what's on screen now, so the first drag pixel moves from there.
            _dragOrigin = (Box.X, Box.Y, Box.Width, Box.H > 0 ? Box.H : Bounds.Height);
            _dragging = true;
            RefreshChrome();                          // lock the border visible for the whole drag
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(_canvas);
            double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            if (mode == DragMode.Move)
            {
                double nx = _dragOrigin.X + dx, ny = _dragOrigin.Y + dy;
                if (_canvas.SnapToGrid) { nx = GridMath.Snap(nx); ny = GridMath.Snap(ny); }
                Box.X = Math.Max(0, nx);
                Box.Y = Math.Max(0, ny);
            }
            if (mode is DragMode.Width or DragMode.Both)
            {
                double nw = _dragOrigin.W + dx;
                if (_canvas.SnapToGrid) nw = GridMath.Snap(nw);
                Box.Width = Math.Clamp(nw, NoteBox.MinWidth, 1600);
            }
            if (mode is DragMode.Height or DragMode.Both)
            {
                double nh = _dragOrigin.H + dy;
                if (_canvas.SnapToGrid) nh = GridMath.Snap(nh);
                Box.H = Math.Clamp(nh, NoteBox.MinHeight, 4000);
            }
            _canvas.InvalidateMeasure();
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            RefreshChrome();                          // back to hover/focus-driven now the drag is done
            // Mindmap: dropping onto another bubble links them — BEFORE the commit, so any nudge
            // the link applies persists in the same save.
            if (mode == DragMode.Move) _canvas.OnBoxDragEnd(this);
            _canvas.Document?.CommitGeometry();      // persist the final geometry once
            e.Handled = true;
        };
    }
}
