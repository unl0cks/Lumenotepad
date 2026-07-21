using System;
using System.Linq;
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

    /// <summary>Absolute folder that image-box ("images/xxx.png") and attachment-box
    /// ("assets/report.pdf") paths resolve against — the current notebook's folder, pushed by
    /// MainView when the page loads (M10).</summary>
    public string? ImageRoot { get; set; }

    /// <summary>Opens a PDF attachment in the in-app viewer/annotator (MainView wires this). Null ⇒
    /// PDFs fall back to the OS default app like any other attachment.</summary>
    public Action<string>? OpenPdfRequested { get; set; }

    /// <summary>Insert an image box (M10): a movable/resizable container showing the picture at
    /// <paramref name="relPath"/> (relative to <see cref="ImageRoot"/>).</summary>
    public void AddImage(string relPath, double x, double y, double width = 340)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, GridMath.Snap(x)); y = Math.Max(0, GridMath.Snap(y)); }
        var box = _doc.AddBox(x, y, width);
        box.ImagePath = relPath;
        AddBoxView(box);
        _doc.CommitGeometry();                 // persist with the image path set
    }

    /// <summary>Insert a file-attachment box (M11): a movable chip showing the file's name;
    /// double-click opens it with the default app. <paramref name="relPath"/> is relative to
    /// <see cref="ImageRoot"/> (the notebook folder).</summary>
    public void AddAttachment(string relPath, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, GridMath.Snap(x)); y = Math.Max(0, GridMath.Snap(y)); }
        var box = _doc.AddBox(x, y, 260);
        box.AttachPath = relPath;
        AddBoxView(box);
        _doc.CommitGeometry();                 // persist with the attachment path set
    }

    /// <summary>Insert a table box (M11): a rows×cols grid of rich-text cells, focused on the first cell.</summary>
    public void AddTable(int rows, int cols, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, GridMath.Snap(x)); y = Math.Max(0, GridMath.Snap(y)); }
        double width = Math.Clamp(cols * 130, NoteBox.MinWidth, 940);
        var view = AddBoxView(_doc.AddTableBox(x, y, rows, cols, width));
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
        _doc.CommitGeometry();
    }

    /// <summary>Insert a line divider: "h" (horizontal rule) or "v" (vertical rule) — a movable
    /// strip whose single resize handle stretches the line longer or shorter.</summary>
    public void AddDivider(string orientation, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, GridMath.Snap(x)); y = Math.Max(0, GridMath.Snap(y)); }
        var box = _doc.AddBox(x, y);
        box.Divider = orientation;
        if (orientation == "v") { box.Width = 22; box.H = 240; }   // thin strip, line runs down it
        else { box.Width = 320; box.H = 22; }                      // low strip, line runs across it
        AddBoxView(box);
        _doc.CommitGeometry();                 // persist with the divider kind set
    }

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
    private int _mode;
    private Size _viewport;

    /// <summary>Push the page's effective styles (grid background, method guides, apply mode).</summary>
    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _pageStyle = pageStyle;
        _mode = mode;
        _guides.SetStyles(gridStyle, pageStyle, mode);
        EnsureRegions();               // tag legacy structured starters so they dock like fresh ones
        RefreshMindmapPorts();         // views built before the style was set pick up the bubble look now
        InvalidateMeasure();
    }

    /// <summary>The visible page area — guide dividers AND docked region boxes anchor to it
    /// (MainView pushes it on layout / resize / zoom).</summary>
    public void SetViewport(Size viewport)
    {
        if (viewport == _viewport) return;
        _viewport = viewport;
        _guides.Viewport = viewport;
        _guides.InvalidateVisual();
        DockRegions();                 // reposition region boxes to the new viewport right away
        InvalidateMeasure();
        InvalidateArrange();           // force a re-arrange even if the content bounds didn't change
    }

    // ---- mind map (Mindmap page style): coloured bubbles + drag-to-connect links ----

    /// <summary>True while the page's method is Mindmap — MainView shows the mind-map toolbar then.</summary>
    public bool IsMindmap => _pageStyle == PageStyles.Mindmap;

    /// <summary>The colour new bubbles take (and the last colour picked in the mind-map toolbar).</summary>
    public string? MindmapColor { get; set; }

    /// <summary>The colour a bubble gets when none has been picked — a neutral gray so a fresh map
    /// reads cleanly before the user starts colour-coding.</summary>
    public const string DefaultBubbleColor = "#8B9099";

    /// <summary>Width of newly added bubbles (toolbar S/M/L). Defaults to the medium preset.</summary>
    public double MindmapBubbleWidth { get; set; } = 220;

    /// <summary>When true, bubbles also show the four diagonal (corner) connect ports (toolbar toggle).</summary>
    public bool MindmapDiagonalPorts { get; set; }

    /// <summary>Draw connectors as rigid straight lines instead of springy curves (toolbar toggle).</summary>
    public bool MindmapStraightLines
    {
        get => _links.Straight;
        set { _links.Straight = value; _links.InvalidateVisual(); }
    }

    /// <summary>Repaint every bubble's chrome — after the diagonal-ports toggle flips, or the style set.</summary>
    public void RefreshMindmapPorts()
    {
        foreach (var child in Children)
            if (child is NoteBoxView v) v.RefreshChrome();
    }

    /// <summary>Drop a new bubble centred on (<paramref name="cx"/>,<paramref name="cy"/>) and focus it.</summary>
    public void AddBubble(double cx, double cy)
    {
        if (_doc is null) return;
        double w = MindmapBubbleWidth;
        double bx = cx - w / 2, by = cy - 18;
        if (SnapToGrid) { bx = GridMath.Snap(bx); by = GridMath.Snap(by); }
        var box = _doc.AddBox(Math.Max(0, bx), Math.Max(0, by), w);
        box.Color = MindmapColor ?? DefaultBubbleColor;
        var view = AddBoxView(box);
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
    }

    /// <summary>Add a bubble beside the selected one and link them — fast branch-building. Returns false
    /// when nothing is selected, so the caller can fall back to a plain add at the viewport centre.</summary>
    public bool AddConnectedBubble()
    {
        if (ActiveBubble() is not { } from) return false;
        AddConnectedFrom(from.Box);
        return true;
    }

    /// <summary>Add a bubble to the right of <paramref name="from"/> and link the two, inheriting its
    /// colour (used by the toolbar and a bubble's context menu).</summary>
    public void AddConnectedFrom(NoteBox from)
    {
        if (_doc is null) return;
        double nx = from.X + from.Width + 110, ny = from.Y;
        if (SnapToGrid) { nx = GridMath.Snap(nx); ny = GridMath.Snap(ny); }
        var box = _doc.AddBox(Math.Max(0, nx), Math.Max(0, ny), from.Width);
        box.Color = from.Color ?? MindmapColor ?? DefaultBubbleColor;
        var view = AddBoxView(box);
        _doc.ToggleLink(from, box, "E", "W");
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
    }

    /// <summary>Duplicate a container (text, colour, size) a little down-right of the original.</summary>
    internal void DuplicateBox(NoteBoxView view)
    {
        if (_doc is null) return;
        var src = view.Box;
        var clone = RichDocJson.FromDtos(RichDocJson.ToDtos(src.Doc));
        var box = _doc.AddBox(src.X + 26, src.Y + 26, src.Width, clone);
        box.Color = src.Color;
        box.H = src.H;
        var nv = AddBoxView(box);
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(nv.FocusEditor, DispatcherPriority.Background);
    }

    /// <summary>The bounding rect of every container on the page (empty rect when there are none) — the
    /// mind-map "centre on map" command frames it.</summary>
    public Rect ContentBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v) continue;
            minX = Math.Min(minX, v.Box.X);
            minY = Math.Min(minY, v.Box.Y);
            maxX = Math.Max(maxX, v.Box.X + v.Box.Width);
            maxY = Math.Max(maxY, v.Box.Y + Math.Max(v.Bounds.Height, v.Box.H));
        }
        return maxX < minX ? default : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>The bubble whose editor is currently active, or null.</summary>
    private NoteBoxView? ActiveBubble()
    {
        if (ActiveEditor is null) return null;
        foreach (var child in Children)
            if (child is NoteBoxView v && ReferenceEquals(v.Editor, ActiveEditor)) return v;
        return null;
    }

    /// <summary>The active bubble's colour, if a bubble is selected (for the toolbar's ring).</summary>
    public string? ActiveBubbleColor => ActiveBubble()?.Box.Color;

    /// <summary>Connect-port drag started on <paramref name="from"/>'s <paramref name="dir"/> edge —
    /// begin the rubber-band line from that edge.</summary>
    internal void BeginLink(NoteBoxView from, string dir, Point canvasPt)
        => _links.BeginPending(from.Box, dir, canvasPt);

    /// <summary>The connect-port drag moved — chase the cursor, and once the cursor is over a bubble snap
    /// the line's tip onto that bubble's NEAREST port and light up its ports so the drop point is clear.</summary>
    internal void UpdateLink(Point canvasPt)
    {
        _links.PendingCursor = canvasPt;
        var src = _links.PendingSource;
        NoteBoxView? snap = null;
        string dir = "W";
        if (src is not null) snap = FindSnap(canvasPt, src, out dir);
        _links.PendingSnap = snap?.Box;
        _links.PendingSnapDir = dir;
        foreach (var child in Children)
            if (child is NoteBoxView v) v.SetLinkTarget(ReferenceEquals(v, snap));
        _links.Animate();
    }

    /// <summary>The connect-port drag ended — link the source to the port the tip snapped onto
    /// (releasing over empty canvas just cancels; releasing on an already-linked bubble unlinks).
    /// Persists so the connector survives a reload.</summary>
    internal void EndLink(Point canvasPt)
    {
        var src = _links.PendingSource;
        string srcDir = _links.PendingSourceDir;
        NoteBoxView? snap = null;
        string dstDir = "W";
        if (src is not null) snap = FindSnap(canvasPt, src, out dstDir);
        _links.CancelPending();
        foreach (var child in Children)                        // drop the hover highlight on every bubble
            if (child is NoteBoxView v) v.SetLinkTarget(false);
        if (src is null || _doc is null || snap is null) return;
        _doc.ToggleLink(src, snap.Box, srcDir, dstDir);        // anchor at the exact port snapped to
        _links.InvalidateVisual();
        _doc.CommitGeometry();       // links persist alongside geometry
    }

    /// <summary>The bubble the cursor is over while connecting (nearest whose body is within the margin,
    /// excluding the source), plus the port on it nearest the cursor — the dot the tip snaps to.</summary>
    private NoteBoxView? FindSnap(Point p, NoteBox src, out string dir)
    {
        const double margin = 34;   // "hovering over" the bubble (a little slack outside its body)
        NoteBoxView? best = null;
        double bestD = double.MaxValue;
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v || ReferenceEquals(v.Box, src)) continue;
            double d = RectDistance(v.Bounds, p);
            if (d <= margin && d < bestD) { bestD = d; best = v; }
        }
        dir = best is null ? "W" : LinkLayer.NearestDir(best.Bounds, p, MindmapDiagonalPorts);
        return best;
    }

    /// <summary>Distance from a point to a rectangle (0 when the point is inside).</summary>
    private static double RectDistance(Rect r, Point p)
    {
        double dx = Math.Max(Math.Max(r.X - p.X, p.X - r.Right), 0);
        double dy = Math.Max(Math.Max(r.Y - p.Y, p.Y - r.Bottom), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Set the mind-map colour: the default for new bubbles, and (when one is selected) applied
    /// to that bubble immediately.</summary>
    public void SetBubbleColor(string? color)
    {
        MindmapColor = color;
        if (ActiveBubble() is { } v)
        {
            v.Box.Color = color;
            v.RefreshChrome();
            _links.InvalidateVisual();     // its half of every attached connector's gradient recolours
            _doc?.CommitGeometry();
        }
    }

    /// <summary>Recolour a specific bubble (its right-click "Colour" menu) — updates its connectors'
    /// gradient and, when it's the selected one, the toolbar's colour ring.</summary>
    internal void RecolorBox(NoteBoxView view, string? color)
    {
        view.Box.Color = color;
        view.RefreshChrome();
        _links.InvalidateVisual();
        if (ReferenceEquals(ActiveEditor, view.Editor))
        {
            MindmapColor = color;
            ActiveEditorChanged?.Invoke(ActiveEditor);   // MainView re-rings the picker
        }
        _doc?.CommitGeometry();
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
        Focusable = true;   // so a bare-canvas click can pull focus off a bubble to deselect it

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

        ContextRequested += OnCanvasContext;     // right-click the bare canvas for a general menu
    }

    /// <summary>The general right-click menu on empty canvas (bubbles/editors keep their own). Offers the
    /// page-appropriate "add here" at the click point, plus a quick connect when a bubble is selected.</summary>
    private void OnCanvasContext(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
    {
        if (_doc is null || !ReferenceEquals(e.Source, this)) return;   // only true bare-canvas right-clicks
        var pos = e.TryGetPosition(this, out var p) ? p : new Point(80, 80);
        var menu = new ContextMenu();
        void Item(string header, Action act)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => act();
            menu.Items.Add(m);
        }
        if (_pageStyle == PageStyles.Mindmap)
        {
            Item("Add bubble here", () => AddBubble(pos.X, pos.Y));
            if (ActiveBubble() is { } ab)
                Item("Add connected to selected", () => AddConnectedFrom(ab.Box));
        }
        else
        {
            Item("Add note here", () =>
            {
                double bx = pos.X - 11, by = pos.Y - 16;
                if (SnapToGrid) { bx = Math.Max(0, GridMath.Snap(bx)); by = Math.Max(0, GridMath.Snap(by)); }
                var v = AddBoxView(_doc.AddBox(bx, by, Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640)));
                Dispatcher.UIThread.Post(v.FocusEditor, DispatcherPriority.Background);
            });
        }
        if (menu.Items.Count == 0) return;
        Views.MenuFx.Attach(menu);
        menu.Open(this);
        e.Handled = true;
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
        EnsureRegions();               // tag legacy structured starters before their views are built
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
        DockRegions();                             // snap every structured style's region boxes to the guides

        double w = 0, h = 0, notesFoot = 0;
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v) { child.Measure(Size.Infinity); continue; }
            v.Measure(new Size(v.Box.Width, double.PositiveInfinity));
            double bottom = v.Box.Y + Math.Max(v.DesiredSize.Height, v.Box.H);
            w = Math.Max(w, v.Box.X + v.Box.Width);
            h = Math.Max(h, bottom);
            if (v.Box.Region != "summary") notesFoot = Math.Max(notesFoot, bottom);  // summary excluded (breaks the loop)
        }

        DockCornellSummary(notesFoot);             // drop the summary band just below the notes content
        _guides.ContentBottom = notesFoot;         // the guide's summary rule uses the very same foot

        foreach (var child in Children)            // the summary box may have moved down — fold it into the height
            if (child is NoteBoxView sv && sv.Box.Region == "summary")
                h = Math.Max(h, sv.Box.Y + Math.Max(sv.DesiredSize.Height, sv.Box.H));

        return new Size(w + 220, h + 320);        // breathing room so the page can always grow by clicking
    }

    /// <summary>Snap every structured style's region boxes (X/Y/Width) to the live guide geometry.
    /// Purely viewport-driven, so it's safe to run each measure; Cornell's summary Y is finished later
    /// once the notes content foot is known (<see cref="DockCornellSummary"/>).</summary>
    private void DockRegions()
    {
        if (_doc is null || _viewport.Width <= 0 || _viewport.Height <= 0) return;
        if (_mode == PageStyles.ModeStartersOnly) return;              // starters-only keeps free boxes
        var regions = PageStyleGuides.Regions(_pageStyle, _viewport, default);
        if (regions.Count == 0) return;
        EnsureRegions();                                              // tag legacy starters (idempotent)
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v || v.Box.Region is not { } id) continue;
            foreach (var (rid, rect) in regions)
                if (rid == id) { v.Box.X = rect.X; v.Box.Y = rect.Y; v.Box.Width = rect.Width; break; }
        }
    }

    /// <summary>Place Cornell's summary region a small gap below the notes content foot (never above its
    /// 80%-of-screen home) — the same rule the guide's summary line uses, so line and box stay glued.</summary>
    private void DockCornellSummary(double notesFoot)
    {
        if (_pageStyle != PageStyles.Cornell || _viewport.Width <= 0 || _viewport.Height <= 0) return;
        var (_, _, summary) = PageStyleGuides.CornellRegions(_viewport.Width, _viewport.Height, notesFoot);
        foreach (var child in Children)
            if (child is NoteBoxView v && v.Box.Region == "summary")
                v.Box.Y = summary.Y;
    }

    /// <summary>Legacy structured pages (created before regions were docked) carry untagged starters —
    /// tag a pristine starter set (box count matching the style's region count, plain text boxes only)
    /// by creation order so they dock like freshly stamped ones. Pages the user has since reshaped
    /// (added/removed boxes, images, tables) are left alone as ordinary free boxes.</summary>
    private void EnsureRegions()
    {
        if (_doc is null || _doc.Boxes.Count == 0) return;
        if (_mode == PageStyles.ModeStartersOnly) return;                 // starters-only pages keep free boxes
        // Tag legacy starters (label-matched, robust to stray boxes); fresh pages arrive tagged → no-op.
        PageStyleTemplate.RetagLegacyStarters(_doc.Boxes, _pageStyle, _viewport.Width > 0 ? _viewport : new Size(900, 600));
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
        _links.Animate();                        // connectors spring after bubbles as they move, then settle
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Only clicks on bare canvas start a container — clicks inside one bubble with another Source.
        if (_doc is null || !ReferenceEquals(e.Source, this)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (_pageStyle == PageStyles.Mindmap)
        {
            // Mind map: a bare-canvas DOUBLE-click drops a bubble; a single click deselects the active
            // bubble (pull keyboard focus here so its editor blurs and its ports/chrome drop).
            if (e.ClickCount >= 2) { AddBubble(p.X, p.Y); }
            else { SetActive(null); Focus(); }
            e.Handled = true;
            return;
        }
        if (CreateOnDoubleClick && e.ClickCount < 2) return;
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

    /// <summary>A bubble MOVE drag ended. Linking now happens by dragging a bubble's connect port onto
    /// another (see <see cref="EndLink"/>), so a plain move no longer toggles links — repositioning a
    /// bubble over another is just a move. Kept as a hook the view calls after any drag.</summary>
    internal void OnBoxDragEnd(NoteBoxView view) { _links.InvalidateVisual(); }

    /// <summary>OneNote behavior: an empty container evaporates once focus has settled elsewhere.
    /// Deferred a beat so the click that stole focus can land first.</summary>
    internal void OnEditorLostFocus(NoteBoxView view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
            if (_pageStyle == PageStyles.Mindmap) return;   // mind-map bubbles persist even when empty
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
    /// <summary>Which edges a drag moves. None = a whole-box move (the grip).</summary>
    [System.Flags]
    private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

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
    // The close look (differs bubble vs card): resting + hover fills and glyph colour.
    private IBrush _closeRestBg = Brushes.Transparent;
    private IBrush _closeRestFg = Brushes.Gray;
    private IBrush _closeHoverBg = CloseHoverBg;
    private readonly Border _resizeLeft;
    private readonly Border _resizeRight;
    private readonly Border _resizeTop;
    private readonly Border _resizeBottom;
    private readonly Border _resizeCorner;       // bottom-right
    private readonly Border _resizeCornerTL;
    private readonly Border _resizeCornerBL;
    // mind-map: connect dots on the bubble's edges — drag one onto another bubble to link. Four
    // orthogonal (N/S/E/W) always, four diagonal (corners) when the toolbar toggle is on.
    private readonly System.Collections.Generic.List<(Border Port, bool Diagonal, string Dir)> _ports = new();
    private bool _linking;
    private bool _linkTarget;   // a link drag is hovering this bubble — show its ports as drop targets
    private bool _hover;

    // Table box (M11): the grid host + its cell editors in row-major order (rebuilt on structural edits).
    private Border? _tableHost;
    private readonly System.Collections.Generic.List<RichTextEditor> _cellEditors = new();

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

        Editor = MakeEditor(box.Doc, new Thickness(10, 3, 10, 9));

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
        if (box.Divider is not null)
        {
            // Divider box: the whole strip is the grip — grab the line anywhere to move it.
            _grip.Height = double.NaN;
            _grip.CornerRadius = new CornerRadius(r);
            _grip.Child = BuildDividerLine(box.Divider);
            body.Children.Add(_grip);
        }
        else
        {
            body.Children.Add(_grip);
            if (box.ImagePath is { Length: > 0 })
                body.Children.Add(BuildImage(box.ImagePath));   // image box: picture instead of the editor
            else if (box.AttachPath is { Length: > 0 })
                body.Children.Add(BuildAttachment(box.AttachPath));   // attachment box: file chip
            else if (box.Table is not null)
            {
                _tableHost = new Border { Child = BuildTableGrid() };   // table box: grid of cell editors
                body.Children.Add(_tableHost);
            }
            else
                body.Children.Add(Editor);
        }

        _chrome = new Border
        {
            Child = body, CornerRadius = new CornerRadius(r),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            // Clip content (notably the full-width grip "title bar" fill) to the rounded outline, so it
            // follows the pill's curved corners instead of overhanging them.
            ClipToBounds = true,
        };

        _closeRestFg = CloseFg;
        _closeGlyph = new TextBlock
        {
            Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
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
        _close.PointerEntered += (_, _) => { _close.Background = _closeHoverBg; _closeGlyph.Foreground = Brushes.White; };
        _close.PointerExited += (_, _) => { _close.Background = _closeRestBg; _closeGlyph.Foreground = _closeRestFg; };
        _close.PointerPressed += (_, e) => e.Handled = true;      // don't start a grip drag from the ✕
        _close.PointerReleased += (_, e) => { _canvas.RequestDelete(this); e.Handled = true; };

        // Overlay the close INSIDE the clipped chrome, so on a bubble the red tab follows the pill's
        // rounded top-right corner (clip cuts it to the outline) instead of a chip poking past it.
        _chrome.Child = null;                    // detach body so the panel can adopt it
        var chromeContent = new Panel();
        chromeContent.Children.Add(body);
        chromeContent.Children.Add(_close);
        _chrome.Child = chromeContent;

        // Edge strips inset from the ends so the corner squares below stay grabbable.
        Border EdgeStrip(HorizontalAlignment h, VerticalAlignment v, bool vertical, StandardCursorType cur) => new()
        {
            Width = vertical ? 7 : double.NaN, Height = vertical ? double.NaN : 7,
            HorizontalAlignment = h, VerticalAlignment = v,
            Margin = vertical ? new Thickness(0, 11) : new Thickness(11, 0),
            Background = Brushes.Transparent, Cursor = new Cursor(cur),
        };
        Border Corner(HorizontalAlignment h, VerticalAlignment v, StandardCursorType cur) => new()
        {
            Width = 14, Height = 14, HorizontalAlignment = h, VerticalAlignment = v,
            Background = Brushes.Transparent, Cursor = new Cursor(cur),
        };
        _resizeLeft = EdgeStrip(HorizontalAlignment.Left, VerticalAlignment.Stretch, true, StandardCursorType.SizeWestEast);
        _resizeRight = EdgeStrip(HorizontalAlignment.Right, VerticalAlignment.Stretch, true, StandardCursorType.SizeWestEast);
        _resizeTop = EdgeStrip(HorizontalAlignment.Stretch, VerticalAlignment.Top, false, StandardCursorType.SizeNorthSouth);
        _resizeBottom = EdgeStrip(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, false, StandardCursorType.SizeNorthSouth);
        _resizeCornerTL = Corner(HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner);
        _resizeCornerBL = Corner(HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner);
        _resizeCorner = Corner(HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner);

        // Mind-map connect ports on the bubble's edges: drag one onto another bubble to link them.
        const double o = -7;   // half the 14px dot, so it straddles the edge
        void AddPort(HorizontalAlignment h, VerticalAlignment vv, Thickness m, bool diagonal, string dir)
        {
            var port = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1.25), BorderBrush = Brushes.White, Background = B(t.Accent),
                HorizontalAlignment = h, VerticalAlignment = vv, Margin = m, IsVisible = false,
                Cursor = new Cursor(StandardCursorType.Cross),
            };
            ToolTip.SetTip(port, "Drag onto another bubble to connect");
            port.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(port).Properties.IsLeftButtonPressed) return;
                _linking = true;
                _canvas.BeginLink(this, dir, e.GetPosition(_canvas));
                e.Pointer.Capture(port);
                e.Handled = true;
            };
            port.PointerMoved += (_, e) => { if (_linking) { _canvas.UpdateLink(e.GetPosition(_canvas)); e.Handled = true; } };
            port.PointerReleased += (_, e) =>
            {
                if (!_linking) return;
                _linking = false;
                e.Pointer.Capture(null);
                _canvas.EndLink(e.GetPosition(_canvas));
                e.Handled = true;
            };
            _ports.Add((port, diagonal, dir));
        }
        AddPort(HorizontalAlignment.Center, VerticalAlignment.Top,    new Thickness(0, o, 0, 0), false, "N");
        AddPort(HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, o), false, "S");
        AddPort(HorizontalAlignment.Left,   VerticalAlignment.Center, new Thickness(o, 0, 0, 0), false, "W");
        AddPort(HorizontalAlignment.Right,  VerticalAlignment.Center, new Thickness(0, 0, o, 0), false, "E");
        AddPort(HorizontalAlignment.Left,   VerticalAlignment.Top,    new Thickness(o, o, 0, 0), true,  "NW");
        AddPort(HorizontalAlignment.Right,  VerticalAlignment.Top,    new Thickness(0, o, o, 0), true,  "NE");
        AddPort(HorizontalAlignment.Left,   VerticalAlignment.Bottom, new Thickness(o, 0, 0, o), true,  "SW");
        AddPort(HorizontalAlignment.Right,  VerticalAlignment.Bottom, new Thickness(0, 0, o, o), true,  "SE");

        Children.Add(_chrome);           // the ✕ lives inside the chrome (clipped); TR corner is the close
        Children.Add(_resizeLeft);
        Children.Add(_resizeRight);
        Children.Add(_resizeTop);
        Children.Add(_resizeBottom);
        Children.Add(_resizeCornerTL);
        Children.Add(_resizeCornerBL);
        Children.Add(_resizeCorner);
        foreach (var (port, _, _) in _ports) Children.Add(port);   // ports on top of the resize strip

        PointerEntered += (_, _) => { _hover = true; RefreshChrome(); };
        PointerExited += (_, _) => { _hover = false; RefreshChrome(); };
        // Image/divider/attachment/table boxes don't use the single main editor to focus / evaporate.
        if (box.ImagePath is null && box.Divider is null && box.AttachPath is null && box.Table is null)
        {
            Editor.GotFocus += (_, _) => { _canvas.SetActive(Editor); RefreshChrome(); };
            Editor.LostFocus += (_, _) => { RefreshChrome(); _canvas.OnEditorLostFocus(this); };
            Editor.AddHandler(InputElement.KeyDownEvent, OnBubbleKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        WireDrag(_grip, Edge.None);
        WireDrag(_resizeLeft, Edge.Left);
        WireDrag(_resizeRight, Edge.Right);
        WireDrag(_resizeTop, Edge.Top);
        WireDrag(_resizeBottom, Edge.Bottom);
        WireDrag(_resizeCornerTL, Edge.Left | Edge.Top);
        WireDrag(_resizeCornerBL, Edge.Left | Edge.Bottom);
        WireDrag(_resizeCorner, Edge.Right | Edge.Bottom);

        _grip.ContextRequested += (_, e) =>
        {
            var menu = new ContextMenu();
            bool plain = Box.Divider is null && Box.ImagePath is null && Box.Table is null && Box.AttachPath is null;
            if (_canvas.IsMindmap && plain)
            {
                var conn = new MenuItem { Header = "Add connected bubble" };
                conn.Click += (_, _) => _canvas.AddConnectedFrom(Box);
                menu.Items.Add(conn);
            }
            if (plain)
            {
                var dup = new MenuItem { Header = "Duplicate" };
                dup.Click += (_, _) => _canvas.DuplicateBox(this);
                menu.Items.Add(dup);

                var size = new MenuItem { Header = "Text size" };
                void SizeItem(string h, double s)
                {
                    var m = new MenuItem { Header = h };
                    m.Click += (_, _) => SetFontScale(s);
                    size.Items.Add(m);
                }
                SizeItem("Normal", 1.0);
                SizeItem("Large", 1.4);
                SizeItem("Title", 1.9);
                menu.Items.Add(size);
            }
            if (_canvas.IsMindmap && plain)
            {
                var col = new MenuItem { Header = "Colour" };
                void ColItem(string name, string? hex)
                {
                    var m = new MenuItem { Header = name };
                    if (hex is not null)
                        m.Icon = new Border
                        {
                            Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                            Background = new SolidColorBrush(Color.Parse(hex)),
                        };
                    m.Click += (_, _) => _canvas.RecolorBox(this, hex);
                    col.Items.Add(m);
                }
                ColItem("Default", null);
                foreach (var (family, shades) in ViewModels.MainViewModel.NotebookPalette)
                    ColItem(family, shades[2].Hex);
                foreach (var (name, hex) in ViewModels.MainViewModel.GrayscaleShades)
                    ColItem(name, hex);
                menu.Items.Add(col);
            }
            if (Box.AttachPath is not null)
            {
                var open = new MenuItem { Header = "Open attachment" };
                open.Click += (_, _) => OpenAttachment();
                menu.Items.Add(open);
            }
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var del = new MenuItem { Header = Box.Divider is null ? "Delete container" : "Delete divider" };
            del.Click += (_, _) => _canvas.RequestDelete(this);
            menu.Items.Add(del);
            Views.MenuFx.Attach(menu);     // rise-in + Lumen glass-variant popup acrylic
            menu.Open(_grip);
            e.Handled = true;
        };

        ApplyFontScale();
        RefreshChrome();
    }

    /// <summary>Mind-map keyboard shortcuts on a focused bubble: Tab spawns a linked child, Ctrl+D
    /// duplicates. Both are swallowed so the editor doesn't also act on the key.</summary>
    private void OnBubbleKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.D)
        {
            _canvas.DuplicateBox(this);
            e.Handled = true;
        }
        else if (_canvas.IsMindmap && e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            _canvas.AddConnectedFrom(Box);
            e.Handled = true;
        }
    }

    /// <summary>Apply this box's text-size multiplier to its editor (mind-map hierarchy).</summary>
    private void ApplyFontScale()
    {
        double baseSize = Math.Clamp(RichTextEditor.EditorFontSizePref, 11, 24);
        Editor.FontSize = baseSize * (Box.FontScale <= 0 ? 1.0 : Box.FontScale);
    }

    internal void SetFontScale(double scale)
    {
        Box.FontScale = scale;
        ApplyFontScale();
        _canvas.InvalidateMeasure();
        _canvas.Document?.CommitGeometry();
    }

    internal void FocusEditor()
    {
        if (Box.Table is not null) { if (_cellEditors.Count > 0) _cellEditors[0].Focus(); return; }
        if (Box.ImagePath is null && Box.Divider is null && Box.AttachPath is null) Editor.Focus();
    }

    /// <summary>Build a note editor (main box or a table cell) with the paper-region theme brushes
    /// and the user's font/spacing prefs.</summary>
    private RichTextEditor MakeEditor(RichDocument doc, Thickness margin)
    {
        var t = Services.ThemeManager.Current;
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        return new RichTextEditor
        {
            Document = doc, Margin = margin,
            Foreground = B(t.PaperText),
            CaretBrush = B(RichTextEditor.CaretColorOverride ?? t.Accent),
            LinkBrush = B(t.Accent),
            SelectionBrush = B(t.FieldSelection),
            FontFamily = Services.AppFonts.Family(RichTextEditor.EditorFontPref),
            FontSize = Math.Clamp(RichTextEditor.EditorFontSizePref, 11, 24),
            ParagraphSpacing = 4 * Math.Clamp(RichTextEditor.ParagraphSpacingScalePref, 0.5, 3),
        };
    }

    /// <summary>Build the table's cell grid: equal-width columns, auto-height rows, hairline gridlines,
    /// a rich-text editor per cell. Rebuilt whenever rows/columns are added or removed.</summary>
    private Control BuildTableGrid()
    {
        _cellEditors.Clear();
        var table = Box.Table!;
        var line = new SolidColorBrush(Color.Parse(Services.ThemePalettes.Alpha(
            Services.ThemeManager.Current.PaperText, 0x30)));

        var grid = new Grid();
        for (int c = 0; c < table.ColCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (int r = 0; r < table.RowCount; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int r = 0; r < table.RowCount; r++)
            for (int c = 0; c < table.ColCount; c++)
            {
                var ed = MakeEditor(table.Rows[r][c], new Thickness(7, 5, 7, 5));
                int rr = r, cc = c;
                ed.GotFocus += (_, _) => { _canvas.SetActive(ed); RefreshChrome(); };
                ed.AddHandler(InputElement.KeyDownEvent, (_, e) => OnCellKey(e, rr, cc), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                _cellEditors.Add(ed);

                var cell = new Border
                {
                    BorderBrush = line, BorderThickness = new Thickness(0, 0, 1, 1), Child = ed,
                };
                cell.ContextRequested += (_, e) => { OpenCellMenu(cell, rr, cc); e.Handled = true; };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

        // Outer border closes the top + left edges the per-cell borders leave open.
        return new Border
        {
            BorderBrush = line, BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid, Margin = new Thickness(8, 3, 8, 9),
        };
    }

    private void RebuildTable()
    {
        if (_tableHost is null) return;
        _tableHost.Child = BuildTableGrid();
        _canvas.InvalidateMeasure();
    }

    /// <summary>Tab / Shift+Tab walk the cells in reading order; Tab off the last cell grows a new row.</summary>
    private void OnCellKey(Avalonia.Input.KeyEventArgs e, int r, int c)
    {
        if (e.Key != Key.Tab || Box.Table is null) return;
        int idx = r * Box.Table.ColCount + c;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (idx > 0) { _cellEditors[idx - 1].Focus(); e.Handled = true; }
        }
        else if (idx < _cellEditors.Count - 1)
        {
            _cellEditors[idx + 1].Focus(); e.Handled = true;
        }
        else
        {
            _canvas.Document?.TableInsertRow(Box, -1);
            RebuildTable();
            if (idx + 1 < _cellEditors.Count) _cellEditors[idx + 1].Focus();
            e.Handled = true;
        }
    }

    private void OpenCellMenu(Control target, int r, int c)
    {
        var menu = new ContextMenu();
        void Item(string header, Action act)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => act();
            menu.Items.Add(m);
        }
        Item("Insert row above", () => { _canvas.Document?.TableInsertRow(Box, r); RebuildTable(); });
        Item("Insert row below", () => { _canvas.Document?.TableInsertRow(Box, r + 1); RebuildTable(); });
        Item("Insert column left", () => { _canvas.Document?.TableInsertColumn(Box, c); RebuildTable(); });
        Item("Insert column right", () => { _canvas.Document?.TableInsertColumn(Box, c + 1); RebuildTable(); });
        menu.Items.Add(new Separator());
        Item("Delete row", () => { _canvas.Document?.TableRemoveRow(Box, r); RebuildTable(); });
        Item("Delete column", () => { _canvas.Document?.TableRemoveColumn(Box, c); RebuildTable(); });
        Views.MenuFx.Attach(menu);
        menu.Open(target);
    }

    /// <summary>The picture control for an image box, loaded from ImageRoot + the box's relative path.</summary>
    private Control BuildImage(string relPath)
    {
        var img = new Avalonia.Controls.Image
        {
            Stretch = Stretch.Uniform, Margin = new Thickness(5, 0, 5, 5),
        };
        try
        {
            var root = _canvas.ImageRoot;
            var full = root is { Length: > 0 } ? System.IO.Path.Combine(root, relPath) : relPath;
            if (System.IO.File.Exists(full)) img.Source = new Avalonia.Media.Imaging.Bitmap(full);
        }
        catch { /* missing/unreadable image → empty box (still movable/deletable) */ }
        return img;
    }

    /// <summary>The file chip for an attachment box: paperclip glyph + filename + an open hint,
    /// tinted from the paper region's text so it themes. Double-click opens the file.</summary>
    private Control BuildAttachment(string relPath)
    {
        var t = Services.ThemeManager.Current;
        var text = new SolidColorBrush(Color.Parse(t.PaperText));
        var muted = new SolidColorBrush(Color.Parse(t.PaperTextMuted));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 16, Foreground = text, VerticalAlignment = VerticalAlignment.Center,
        });
        var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new TextBlock
        {
            Text = System.IO.Path.GetFileName(relPath), FontSize = 12.5, Foreground = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        lines.Children.Add(new TextBlock
        {
            Text = "Double-click to open", FontSize = 10.5, Foreground = muted,
        });
        row.Children.Add(lines);
        var chip = new Border
        {
            Child = row, Padding = new Thickness(12, 6, 12, 9), Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        chip.DoubleTapped += (_, e) => { OpenAttachment(); e.Handled = true; };
        return chip;
    }

    /// <summary>Hand the attached file to its default app (shell open). Missing files no-op.</summary>
    private void OpenAttachment()
    {
        try
        {
            var root = _canvas.ImageRoot;
            var full = root is { Length: > 0 }
                ? System.IO.Path.Combine(root, Box.AttachPath!) : Box.AttachPath!;
            if (!System.IO.File.Exists(full)) return;
            // PDFs open in the in-app viewer/annotator; everything else goes to its default app.
            if (full.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && _canvas.OpenPdfRequested is { } openPdf)
                openPdf(full);
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = full, UseShellExecute = true });
        }
        catch { /* no handler / locked file → nothing to do */ }
    }

    /// <summary>The line visual for a divider box, tinted from the paper text so it themes.</summary>
    private Control BuildDividerLine(string orientation)
    {
        var line = new Border
        {
            Background = new SolidColorBrush(Color.Parse(
                Services.ThemePalettes.Alpha(Services.ThemeManager.Current.PaperText, 0x59))),
            CornerRadius = new CornerRadius(1), IsHitTestVisible = false,
        };
        if (orientation == "v")
        {
            line.Width = 2;
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.Margin = new Thickness(0, 5);
        }
        else
        {
            line.Height = 2;
            line.VerticalAlignment = VerticalAlignment.Center;
            line.Margin = new Thickness(5, 0);
        }
        return line;
    }

    internal void RefreshChrome()
    {
        bool focused = Editor.IsFocused;
        // Capturing the pointer on a resize/grip handle drops the box's own IsPointerOver (so PointerExited
        // fires and would blank the border mid-drag) — treat an active drag as "keep the chrome lit".
        bool active = _hover || focused || _dragging;
        if (Box.Color is { } hex && Color.TryParse(hex, out var bc))
        {
            // Coloured bubble (mind-map): a soft fill of the colour plus a solid coloured edge that
            // brightens when the bubble is active — the whole card carries its colour category.
            _chrome.Background = new SolidColorBrush(bc, active ? 0.28 : 0.18);
            _chrome.BorderBrush = new SolidColorBrush(bc, active ? 1.0 : 0.72);
        }
        else
        {
            _chrome.Background = Brushes.Transparent;
            _chrome.BorderBrush = _dragging || focused ? FocusBorder : _hover ? HoverBorder : Brushes.Transparent;
        }
        _grip.Background = active ? GripFill : Brushes.Transparent;
        _gripBar.IsVisible = active;
        _close.IsVisible = active && !Box.Locked;
        // Hidden handles are also not hit-testable — the "Resizable pages" preference off = no resizing.
        // Dividers stretch along their axis only: the cross-axis and corner handles stay hidden.
        bool resize = _canvas.CanResize && !Box.Locked;
        _resizeRight.IsVisible = resize && Box.Divider != "v";
        _resizeBottom.IsVisible = resize && Box.Divider != "h";
        // Full all-sides + corners resize for real containers; dividers keep their one-axis handle only.
        bool full = resize && Box.Divider is null;
        _resizeLeft.IsVisible = full;
        _resizeTop.IsVisible = full;
        _resizeCorner.IsVisible = full;
        _resizeCornerTL.IsVisible = full;
        _resizeCornerBL.IsVisible = full;

        // Mind-map text bubbles read as circles: a pill (fully-rounded) chrome + matching grip top.
        bool normalBox = Box.Divider is null && Box.ImagePath is null && Box.Table is null && Box.AttachPath is null;
        bool bubble = _canvas.IsMindmap && normalBox;
        if (normalBox)   // leave divider/image/attachment/table chrome radii as their constructor set them
        {
            double rad = bubble ? 999 : NoteCanvas.NoteRadiusPref;
            _chrome.CornerRadius = new CornerRadius(rad);
            _grip.CornerRadius = new CornerRadius(rad, rad, 0, 0);
        }
        if (bubble)
        {
            // Centre the label: horizontally via paragraph Align, vertically via a bottom margin that
            // balances the grip (17) + top inset (3) sitting above the text, so it sits mid-bubble.
            if (Box.Doc.Paragraphs.Any(p => p.Align != TextAlign.Center))
            {
                foreach (var p in Box.Doc.Paragraphs) p.Align = TextAlign.Center;
                Editor.InvalidateMeasure();
                Editor.InvalidateVisual();
            }
            Editor.Margin = new Thickness(10, 3, 10, 20);
            // A red close tab flush in the top-right; it sits inside the clipped chrome, so the pill's
            // rounded corner cuts its outer edge — a corner button that follows the bubble's curve.
            _close.Width = _close.Height = 20;
            _close.CornerRadius = new CornerRadius(5);
            _close.Margin = default;
            _closeGlyph.FontSize = 9;
            _closeGlyph.Margin = new Thickness(0, 3, 3, 0);   // nudge the ✕ in, clear of the clipped corner
            _closeRestBg = new SolidColorBrush(Color.Parse("#E24B4B"));
            _closeRestFg = Brushes.White;
            _closeHoverBg = new SolidColorBrush(Color.Parse("#F1352B"));
        }
        else if (normalBox)   // ordinary note card: the ✕ hugs the square corner as before
        {
            _close.Width = _close.Height = 17;
            _close.CornerRadius = new CornerRadius(0, NoteCanvas.NoteRadiusPref, 0, 6);
            _close.Margin = default;
            _closeGlyph.FontSize = 7.5;
            _closeGlyph.Margin = default;
            _closeRestBg = Brushes.Transparent;
            _closeRestFg = CloseFg;
            _closeHoverBg = CloseHoverBg;
        }
        _close.Background = _closeRestBg;       // apply the resting look now the box kind is known
        _closeGlyph.Foreground = _closeRestFg;

        // Connect ports show while the bubble is active or a link is in flight; the diagonals wait for
        // the toolbar toggle. All wear the bubble colour (or accent) so the map's wiring reads clearly.
        bool showPorts = bubble && (active || _linking || _linkTarget);
        var portBrush = Box.Color is { } ph && Color.TryParse(ph, out var pc)
            ? new SolidColorBrush(pc) : new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent));
        foreach (var (port, diagonal, _) in _ports)
        {
            port.IsVisible = showPorts && (!diagonal || _canvas.MindmapDiagonalPorts);
            if (port.IsVisible) port.Background = portBrush;
        }
    }

    /// <summary>A link drag entered/left this bubble: light up its ports as drop targets (and drop them
    /// again on leave). Cheap no-op when the state hasn't changed — UpdateLink calls it every move.</summary>
    internal void SetLinkTarget(bool on)
    {
        if (_linkTarget == on) return;
        _linkTarget = on;
        RefreshChrome();
    }

    /// <summary>Panels arrange children to fill, so ports sit by alignment + margin. The diagonal ports
    /// must land on the rounded pill corners (radius = half the shorter side), not the empty rectangular
    /// corners — recompute their margins from the arranged size each pass.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_canvas.IsMindmap)
        {
            PlaceDiagonalPorts(finalSize.Width, finalSize.Height);
        }
        return base.ArrangeOverride(finalSize);
    }

    private void PlaceDiagonalPorts(double w, double h)
    {
        double rad = Math.Min(w, h) / 2;
        double m = 0.2929 * rad - 7;   // 45° point on the corner arc, less half the 14px dot
        foreach (var (port, diagonal, dir) in _ports)
        {
            if (!diagonal) continue;
            port.Margin = dir switch
            {
                "NW" => new Thickness(m, m, 0, 0),
                "NE" => new Thickness(0, m, m, 0),
                "SW" => new Thickness(m, 0, 0, m),
                "SE" => new Thickness(0, 0, m, m),
                _ => port.Margin,
            };
        }
    }

    private Point _dragStart;
    private (double X, double Y, double W, double H) _dragOrigin;
    private bool _dragging;

    private void WireDrag(Control handle, Edge edges)
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
            var (ox, oy, ow, oh) = _dragOrigin;
            bool snap = _canvas.SnapToGrid;
            if (edges == Edge.None)                        // whole-box move (the grip)
            {
                double nx = ox + dx, ny = oy + dy;
                if (snap) { nx = GridMath.Snap(nx); ny = GridMath.Snap(ny); }
                Box.X = Math.Max(0, nx);
                Box.Y = Math.Max(0, ny);
            }
            else
            {
                if ((edges & (Edge.Left | Edge.Right)) != 0)
                {
                    double nw = edges.HasFlag(Edge.Left) ? ow - dx : ow + dx;
                    if (snap) nw = GridMath.Snap(nw);
                    nw = Math.Clamp(nw, Box.Divider == "h" ? NoteBox.MinDividerLength : NoteBox.MinWidth, 1600);
                    Box.Width = nw;
                    if (edges.HasFlag(Edge.Left)) Box.X = Math.Max(0, ox + ow - nw);   // keep the right edge fixed
                }
                if ((edges & (Edge.Top | Edge.Bottom)) != 0)
                {
                    double nh = edges.HasFlag(Edge.Top) ? oh - dy : oh + dy;
                    if (snap) nh = GridMath.Snap(nh);
                    nh = Math.Clamp(nh, Box.Divider == "v" ? NoteBox.MinDividerLength : NoteBox.MinHeight, 4000);
                    Box.H = nh;
                    if (edges.HasFlag(Edge.Top)) Box.Y = Math.Max(0, oy + oh - nh);    // keep the bottom edge fixed
                }
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
            if (edges == Edge.None) _canvas.OnBoxDragEnd(this);
            _canvas.Document?.CommitGeometry();      // persist the final geometry once
            e.Handled = true;
        };
    }
}
