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

public enum MindmapLayout { Radial, Hybrid, TopDown }

public sealed class NoteCanvas : Panel
{

    public static double NoteRadiusPref = 9;

    private CanvasDocument? _doc;
    private bool _canResize = true;

    public CanvasDocument? Document
    {
        get => _doc;
        set { _doc = value; Rebuild(); }
    }

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

    public bool HistoryEnabled { get; set; } = true;

    public string? ImageRoot { get; set; }

    public Action<string>? OpenPdfRequested { get; set; }

    public void AddImage(string relPath, double x, double y, double width = 340)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, SnapX(x)); y = Math.Max(0, SnapY(y)); }
        var box = _doc.AddBox(x, y, width);
        box.ImagePath = relPath;
        AddBoxView(box);
        _doc.CommitGeometry();
    }

    public void AddAttachment(string relPath, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, SnapX(x)); y = Math.Max(0, SnapY(y)); }
        var box = _doc.AddBox(x, y, 260);
        box.AttachPath = relPath;
        AddBoxView(box);
        _doc.CommitGeometry();
    }

    public void AddTable(int rows, int cols, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, SnapX(x)); y = Math.Max(0, SnapY(y)); }
        double width = Math.Clamp(cols * 130, NoteBox.MinWidth, 940);
        var view = AddBoxView(_doc.AddTableBox(x, y, rows, cols, width));
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
        _doc.CommitGeometry();
    }

    public void AddDivider(string orientation, double x, double y)
    {
        if (_doc is null) return;
        if (SnapToGrid) { x = Math.Max(0, SnapX(x)); y = Math.Max(0, SnapY(y)); }
        var box = _doc.AddBox(x, y);
        box.Divider = orientation;
        if (orientation == "v") { box.Width = 22; box.H = 240; }
        else { box.Width = 320; box.H = 22; }
        AddBoxView(box);
        _doc.CommitGeometry();
    }

    public Func<Task<bool>>? ConfirmDelete { get; set; }

    public bool SnapToGrid { get; set; }

    public bool AlwaysShowBorders { get; set; }

    public bool CreateOnDoubleClick { get; set; }

    private readonly GuideLayer _guides = new();

    private readonly LinkLayer _links = new();
    private string _pageStyle = PageStyles.Freeform;
    private string _gridStyle = PageStyles.Blank;
    private int _mode;
    private Size _viewport;

    internal double SnapX(double v) => GridMath.SnapX(v, _gridStyle);

    internal double SnapY(double v) => GridMath.SnapY(v, _gridStyle);

    public void SetStyles(string gridStyle, string pageStyle, int mode)
    {
        _gridStyle = gridStyle;
        _pageStyle = pageStyle;
        _mode = mode;
        _guides.SetStyles(gridStyle, pageStyle, mode);
        EnsureRegions();
        RefreshMindmapPorts();
        InvalidateMeasure();
    }

    public void SetViewport(Size viewport)
    {
        if (viewport == _viewport) return;
        _viewport = viewport;
        _guides.Viewport = viewport;
        _guides.InvalidateVisual();
        DockRegions();
        InvalidateMeasure();
        InvalidateArrange();
    }

    public bool IsMindmap => _pageStyle == PageStyles.Mindmap;

    public string? MindmapColor { get; set; }

    public const string DefaultBubbleColor = "#8B9099";

    public double MindmapBubbleWidth { get; set; } = 220;

    public MindmapLayout TidyLayout { get; set; } = MindmapLayout.Radial;

    public bool MindmapDiagonalPorts { get; set; }

    public bool MindmapStraightLines
    {
        get => _links.Straight;
        set { _links.Straight = value; _links.InvalidateVisual(); }
    }

    public bool MindmapPaintActive { get; set; }
    public string? MindmapPaintColor { get; set; }

    internal void PaintBubble(NoteBoxView view) => RecolorBox(view, MindmapPaintColor);

    public void RefreshMindmapPorts()
    {
        foreach (var child in Children)
            if (child is NoteBoxView v) v.RefreshChrome();
    }

    public void AddBubble(double cx, double cy, BubbleKind kind = BubbleKind.Title)
    {
        if (_doc is null) return;
        double w = kind == BubbleKind.Title ? MindmapBubbleWidth : MindmapBubbleWidth * 1.3;
        double bx = cx - w / 2, by = cy - 18;
        if (SnapToGrid) { bx = SnapX(bx); by = SnapY(by); }
        var box = _doc.AddBox(Math.Max(0, bx), Math.Max(0, by), w);
        box.Kind = kind;
        box.Color = MindmapColor ?? DefaultBubbleColor;
        var view = AddBoxView(box);
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
    }

    public bool AddConnectedBubble()
    {
        if (ActiveBubble() is not { } from) return false;
        AddConnectedFrom(from.Box);
        return true;
    }

    public void AddConnectedFrom(NoteBox from)
    {
        if (_doc is null) return;
        double nx = from.X + from.Width + 110, ny = from.Y;
        if (SnapToGrid) { nx = SnapX(nx); ny = SnapY(ny); }
        var box = _doc.AddBox(Math.Max(0, nx), Math.Max(0, ny), from.Width);
        box.Kind = from.Kind;
        box.Color = from.Color ?? MindmapColor ?? DefaultBubbleColor;
        var view = AddBoxView(box);
        _doc.ToggleLink(from, box, "E", "W");
        _links.AnimateLinkIn(_doc.Links[^1]);
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
    }

    internal void DuplicateBox(NoteBoxView view)
    {
        if (_doc is null) return;
        var src = view.Box;
        var clone = RichDocJson.FromDtos(RichDocJson.ToDtos(src.Doc));
        var box = _doc.AddBox(src.X + 26, src.Y + 26, src.Width, clone);
        box.Color = src.Color;
        box.Kind = src.Kind;
        box.Central = src.Central;
        box.FontScale = src.FontScale;
        box.H = src.H;
        var nv = AddBoxView(box);
        _doc.CommitGeometry();
        Dispatcher.UIThread.Post(nv.FocusEditor, DispatcherPriority.Background);
    }

    public void TidyMindmap()
    {
        if (_doc is null || _doc.Boxes.Count < 2) return;
        var boxes = _doc.Boxes;
        var heights = new System.Collections.Generic.Dictionary<NoteBox, double>();
        foreach (var child in Children)
            if (child is NoteBoxView v) heights[v.Box] = v.Bounds.Height > 1 ? v.Bounds.Height : Math.Max(v.Box.H, 44);
        double H(NoteBox b) => heights.TryGetValue(b, out var h) ? h : Math.Max(b.H, 44);

        var adj = boxes.ToDictionary(b => b, _ => new System.Collections.Generic.List<NoteBox>());
        foreach (var l in _doc.Links)
            if (adj.ContainsKey(l.A) && adj.ContainsKey(l.B)) { adj[l.A].Add(l.B); adj[l.B].Add(l.A); }
        var root = boxes.FirstOrDefault(b => b.Central) ?? ActiveBubble()?.Box
                   ?? boxes.OrderByDescending(b => adj[b].Count).First();

        var children = boxes.ToDictionary(b => b, _ => new System.Collections.Generic.List<NoteBox>());
        var visited = new System.Collections.Generic.HashSet<NoteBox> { root };
        var q = new System.Collections.Generic.Queue<NoteBox>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            foreach (var m in adj[n])
                if (visited.Add(m)) { children[n].Add(m); q.Enqueue(m); }
        }
        if (visited.Count < 2) return;

        double cx = root.X + root.Width / 2, cy = root.Y + H(root) / 2;
        var targets = new System.Collections.Generic.Dictionary<NoteBox, Point> { [root] = new Point(cx, cy) };

        switch (TidyLayout)
        {
            case MindmapLayout.Radial: LayoutRadial(); break;
            case MindmapLayout.TopDown: LayoutTopDown(); break;
            default: LayoutHybrid(); break;
        }

        void LayoutRadial()
        {
            const double vgap = 22, hstep = 62, agap = 34;
            var subH = new System.Collections.Generic.Dictionary<NoteBox, double>();
            double SubH(NoteBox n)
            {
                var k = children[n];
                if (k.Count == 0) return subH[n] = H(n);
                double s = -vgap;
                foreach (var c in k) s += SubH(c) + vgap;
                return subH[n] = Math.Max(H(n), s);
            }
            SubH(root);

            double ax = root.Width / 2 + 240;
            double ay = Math.Max(H(root) / 2 + 150, ax * 0.72);

            void PlaceColumn(System.Collections.Generic.List<NoteBox> ch, int side, double parentHalfW, double atX, double atY)
            {
                double band = -vgap;
                foreach (var c in ch) band += subH[c] + vgap;
                double top = atY - band / 2;
                foreach (var c in ch)
                {
                    double childCx = atX + side * (parentHalfW + c.Width / 2 + hstep);
                    double childCy = top + subH[c] / 2;
                    targets[c] = new Point(childCx, childCy);
                    PlaceColumn(children[c], side, c.Width / 2, childCx, childCy);
                    top += subH[c] + vgap;
                }
            }

            targets[root] = new Point(cx, cy);
            var kids = children[root];
            var deep = kids.Where(c => children[c].Count > 0).ToList();
            var lone = kids.Where(c => children[c].Count == 0).ToList();

            var right = new System.Collections.Generic.List<NoteBox>();
            var left = new System.Collections.Generic.List<NoteBox>();
            double rS = 0, lS = 0;
            foreach (var c in deep.OrderByDescending(c => subH[c]))
                if (rS <= lS) { right.Add(c); rS += subH[c]; } else { left.Add(c); lS += subH[c]; }
            void PlaceSide(System.Collections.Generic.List<NoteBox> group, int side)
            {
                if (group.Count == 0) return;
                double span = 0;
                foreach (var c in group) span += subH[c] + vgap;
                double baseA = side > 0 ? 0 : Math.PI;
                double acc = 0;
                foreach (var c in group)
                {

                    double frac = span > 1e-6 ? (acc + subH[c] / 2) / span - 0.5 : 0;
                    double a = baseA - side * frac * 2.1;
                    double ex = ax * Math.Cos(a);
                    double minCx = root.Width / 2 + c.Width / 2 + 46;
                    double ccx = Math.Abs(ex) < minCx ? cx + side * minCx : cx + ex;
                    double ccy = cy + ay * Math.Sin(a);
                    targets[c] = new Point(ccx, ccy);
                    PlaceColumn(children[c], side, c.Width / 2, ccx, ccy);
                    acc += subH[c] + vgap;
                }
            }
            PlaceSide(right, 1);
            PlaceSide(left, -1);

            var topRow = new System.Collections.Generic.List<NoteBox>();
            var botRow = new System.Collections.Generic.List<NoteBox>();
            for (int i = 0; i < lone.Count; i++) (i % 2 == 0 ? topRow : botRow).Add(lone[i]);
            void PlaceArc(System.Collections.Generic.List<NoteBox> group, int vside)
            {
                if (group.Count == 0) return;
                double totalW = -agap;
                foreach (var c in group) totalW += c.Width + agap;
                double x = cx - totalW / 2;
                foreach (var c in group)
                {
                    double ccx = x + c.Width / 2;
                    double nx = Math.Clamp((ccx - cx) / ax, -1, 1);
                    double ey = ay * Math.Sqrt(Math.Max(0, 1 - nx * nx));
                    double ccy = cy + vside * Math.Max(H(root) / 2 + H(c) / 2 + 40, ey);
                    targets[c] = new Point(ccx, ccy);
                    x += c.Width + agap;
                }
            }
            PlaceArc(topRow, -1);
            PlaceArc(botRow, 1);
        }

        void LayoutHybrid()
        {
            const double hGap = 56, vGap = 22;
            var subH = new System.Collections.Generic.Dictionary<NoteBox, double>();
            double SubHeight(NoteBox n)
            {
                var kids = children[n];
                if (kids.Count == 0) return subH[n] = H(n);
                double sum = -vGap;
                foreach (var c in kids) sum += SubHeight(c) + vGap;
                return subH[n] = Math.Max(H(n), sum);
            }
            SubHeight(root);
            void PlaceColumn(System.Collections.Generic.List<NoteBox> kids, int side, double parentHalfW, double atX, double atY)
            {
                double band = -vGap;
                foreach (var c in kids) band += subH[c] + vGap;
                double top = atY - band / 2;
                foreach (var c in kids)
                {
                    double childCx = atX + side * (parentHalfW + c.Width / 2 + hGap);
                    double childCy = top + subH[c] / 2;
                    targets[c] = new Point(childCx, childCy);
                    PlaceColumn(children[c], side, c.Width / 2, childCx, childCy);
                    top += subH[c] + vGap;
                }
            }
            var deep = children[root].Where(c => children[c].Count > 0).ToList();
            var singles = children[root].Where(c => children[c].Count == 0).ToList();
            var right = new System.Collections.Generic.List<NoteBox>();
            var left = new System.Collections.Generic.List<NoteBox>();
            double rSum = 0, lSum = 0;
            foreach (var c in deep.OrderByDescending(c => subH[c]))
                if (rSum <= lSum) { right.Add(c); rSum += subH[c] + vGap; } else { left.Add(c); lSum += subH[c] + vGap; }
            PlaceColumn(right, +1, root.Width / 2, cx, cy);
            PlaceColumn(left, -1, root.Width / 2, cx, cy);

            double sideExtent = 0;
            foreach (var c in right) sideExtent = Math.Max(sideExtent, subH[c] / 2);
            foreach (var c in left) sideExtent = Math.Max(sideExtent, subH[c] / 2);
            void PlaceRow(System.Collections.Generic.List<NoteBox> row, int vside)
            {
                if (row.Count == 0) return;
                const double colGap = 40;
                double totalW = -colGap;
                foreach (var c in row) totalW += c.Width + colGap;
                double rowH = row.Max(H);
                double x = cx - totalW / 2;
                double y = cy + vside * (Math.Max(H(root) / 2, sideExtent) + 54 + rowH / 2);
                foreach (var c in row)
                {
                    targets[c] = new Point(x + c.Width / 2, y);
                    x += c.Width + colGap;
                }
            }
            var topRow = new System.Collections.Generic.List<NoteBox>();
            var botRow = new System.Collections.Generic.List<NoteBox>();
            for (int i = 0; i < singles.Count; i++) (i % 2 == 0 ? topRow : botRow).Add(singles[i]);
            PlaceRow(topRow, -1);
            PlaceRow(botRow, +1);
        }

        void LayoutTopDown()
        {
            const double vStep = 150, hGap = 34;
            var subW = new System.Collections.Generic.Dictionary<NoteBox, double>();
            double SubWidth(NoteBox n)
            {
                var kids = children[n];
                if (kids.Count == 0) return subW[n] = n.Width;
                double sum = -hGap;
                foreach (var c in kids) sum += SubWidth(c) + hGap;
                return subW[n] = Math.Max(n.Width, sum);
            }
            SubWidth(root);
            double firstDrop = H(root) / 2 + 145;
            void Place(NoteBox n, double centerX, int depth)
            {
                targets[n] = new Point(centerX, depth == 0 ? cy : cy + firstDrop + (depth - 1) * vStep);
                var kids = children[n];
                double band = -hGap;
                foreach (var c in kids) band += subW[c] + hGap;
                double x = centerX - band / 2;
                foreach (var c in kids)
                {
                    Place(c, x + subW[c] / 2, depth + 1);
                    x += subW[c] + hGap;
                }
            }
            Place(root, cx, 0);
        }

        foreach (var l in _doc.Links)
            if (targets.TryGetValue(l.A, out var pa) && targets.TryGetValue(l.B, out var pb))
            {
                if (TidyLayout == MindmapLayout.TopDown)
                {
                    bool aUpper = pa.Y <= pb.Y;
                    l.DirA = aUpper ? "S" : "N";
                    l.DirB = aUpper ? "N" : "S";
                }
                else
                {
                    l.DirA = Compass(pb.X - pa.X, pb.Y - pa.Y);
                    l.DirB = Compass(pa.X - pb.X, pa.Y - pb.Y);
                }
            }

        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (var kv in targets)
        {
            minX = Math.Min(minX, kv.Value.X - kv.Key.Width / 2);
            minY = Math.Min(minY, kv.Value.Y - H(kv.Key) / 2);
        }
        double shiftX = minX < 40 ? 40 - minX : 0, shiftY = minY < 40 ? 40 - minY : 0;
        if (shiftX != 0 || shiftY != 0)
            foreach (var k in targets.Keys.ToList())
                targets[k] = new Point(targets[k].X + shiftX, targets[k].Y + shiftY);

        var start = targets.Keys.ToDictionary(b => b, b => (b.X, b.Y));
        Views.Motion.Clock(430, p =>
        {
            double e = Views.Motion.EaseOut(p);
            foreach (var b in targets.Keys)
            {
                var t = targets[b];
                b.X = Math.Max(0, Views.Motion.Lerp(start[b].X, t.X - b.Width / 2, e));
                b.Y = Math.Max(0, Views.Motion.Lerp(start[b].Y, t.Y - H(b) / 2, e));
            }
            InvalidateMeasure();
        }, done: () => _doc.CommitGeometry());
    }

    private static string Compass(double dx, double dy)
    {
        double deg = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (deg >= -22.5 && deg < 22.5) return "E";
        if (deg >= 22.5 && deg < 67.5) return "SE";
        if (deg >= 67.5 && deg < 112.5) return "S";
        if (deg >= 112.5 && deg < 157.5) return "SW";
        if (deg >= 157.5 || deg < -157.5) return "W";
        if (deg >= -157.5 && deg < -112.5) return "NW";
        if (deg >= -112.5 && deg < -67.5) return "N";
        return "NE";
    }

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

    private NoteBoxView? ActiveBubble()
    {
        if (ActiveEditor is null) return null;
        foreach (var child in Children)
            if (child is NoteBoxView v && ReferenceEquals(v.Editor, ActiveEditor)) return v;
        return null;
    }

    public string? ActiveBubbleColor => ActiveBubble()?.Box.Color;

    internal void BeginLink(NoteBoxView from, string dir, Point canvasPt)
        => _links.BeginPending(from.Box, dir, canvasPt);

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

    internal void EndLink(Point canvasPt)
    {
        var src = _links.PendingSource;
        string srcDir = _links.PendingSourceDir;
        NoteBoxView? snap = null;
        string dstDir = "W";
        if (src is not null) snap = FindSnap(canvasPt, src, out dstDir);
        _links.CancelPending();
        foreach (var child in Children)
            if (child is NoteBoxView v) v.SetLinkTarget(false);
        if (src is null || _doc is null || snap is null) return;
        var existing = _doc.Links.FirstOrDefault(l =>
            (ReferenceEquals(l.A, src) && ReferenceEquals(l.B, snap.Box)) ||
            (ReferenceEquals(l.A, snap.Box) && ReferenceEquals(l.B, src)));
        if (_doc.ToggleLink(src, snap.Box, srcDir, dstDir))
            _links.AnimateLinkIn(_doc.Links[^1]);
        else if (existing is not null)
            _links.AnimateLinkRemoval(existing);
        _links.InvalidateVisual();
        _doc.CommitGeometry();
    }

    private NoteBoxView? FindSnap(Point p, NoteBox src, out string dir)
    {
        const double margin = 34;
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

    private static double RectDistance(Rect r, Point p)
    {
        double dx = Math.Max(Math.Max(r.X - p.X, p.X - r.Right), 0);
        double dy = Math.Max(Math.Max(r.Y - p.Y, p.Y - r.Bottom), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void SetBubbleColor(string? color)
    {
        MindmapColor = color;
        if (_selection.Count > 0)
        {
            foreach (var sv in _selection) { sv.Box.Color = color; sv.RefreshChrome(); }
            _links.InvalidateVisual();
            _doc?.CommitGeometry();
            return;
        }
        if (ActiveBubble() is { } v)
        {
            v.Box.Color = color;
            v.RefreshChrome();
            _links.InvalidateVisual();
            _doc?.CommitGeometry();
        }
    }

    internal void RecolorBox(NoteBoxView view, string? color)
    {
        view.Box.Color = color;
        view.RefreshChrome();
        _links.InvalidateVisual();
        if (ReferenceEquals(ActiveEditor, view.Editor))
        {
            MindmapColor = color;
            ActiveEditorChanged?.Invoke(ActiveEditor);
        }
        _doc?.CommitGeometry();
    }

    internal void SetCentral(NoteBoxView view, bool central)
    {
        if (view.Box.Central == central) return;
        view.Box.Central = central;
        view.Box.Width = central ? view.Box.Width * 1.3 : view.Box.Width / 1.3;
        view.SetFontScale(central ? 1.45 : 1.0);
        view.RefreshChrome();
        _links.InvalidateVisual();
    }

    internal void SetBubbleKind(NoteBoxView view, BubbleKind kind)
    {
        if (view.Box.Kind == kind) return;
        view.Box.Kind = kind;
        if (kind != BubbleKind.Title && view.Box.Width < 240) view.Box.Width = 240;
        view.RefreshChrome();
        view.InvalidateMeasure();
        _links.InvalidateVisual();
        _doc?.CommitGeometry();
    }

    internal void BringToFront(NoteBoxView view)
    {
        if (_doc is null) return;
        if (_doc.Boxes.Remove(view.Box)) _doc.Boxes.Add(view.Box);
        Children.Remove(view);
        Children.Add(view);
        _doc.CommitGeometry();
    }

    internal void SendToBack(NoteBoxView view)
    {
        if (_doc is null) return;
        if (_doc.Boxes.Remove(view.Box)) _doc.Boxes.Insert(0, view.Box);
        Children.Remove(view);
        int i = 0;
        while (i < Children.Count && Children[i] is not NoteBoxView) i++;
        Children.Insert(i, view);
        _doc.CommitGeometry();
    }

    public RichTextEditor? ActiveEditor { get; private set; }
    public event Action<RichTextEditor?>? ActiveEditorChanged;

    public event Action? TrashChanged;

    public NoteCanvas()
    {

        Background = Brushes.Transparent;
        Focusable = true;

        ContextRequested += OnCanvasContext;

        _labelEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitLabel(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { CommitLabel(false); e.Handled = true; }
        };
        _labelEditor.LostFocus += (_, _) => { if (_editingLink is not null) CommitLabel(true); };
    }

    private void EditLinkLabel(MindLink link)
    {
        _editingLink = link;
        _labelEditor.Text = link.Label ?? "";
        _labelPos = _links.LinkMidpoint(link) ?? default;
        _labelEditor.IsVisible = true;
        InvalidateArrange();
        Dispatcher.UIThread.Post(() => { _labelEditor.Focus(); _labelEditor.SelectAll(); }, DispatcherPriority.Background);
    }

    private void CommitLabel(bool save)
    {
        if (_editingLink is { } link && save)
        {
            var text = _labelEditor.Text?.Trim();
            link.Label = string.IsNullOrEmpty(text) ? null : text;
            _doc?.CommitGeometry();
        }
        _editingLink = null;
        _labelEditor.IsVisible = false;
        _links.InvalidateVisual();
        Focus();
    }

    private void OnCanvasContext(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
    {
        if (_doc is null || !ReferenceEquals(e.Source, this)) return;
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
                if (SnapToGrid) { bx = Math.Max(0, SnapX(bx)); by = Math.Max(0, SnapY(by)); }
                var v = AddBoxView(_doc.AddBox(bx, by, Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640)));
                Dispatcher.UIThread.Post(v.FocusEditor, DispatcherPriority.Background);
            });
        }
        if (menu.Items.Count == 0) return;
        Views.MenuFx.Attach(menu);
        menu.Open(this);
        e.Handled = true;
    }

    private readonly TextBlock _hint = new()
    {
        Text = "Click anywhere and start typing",
        FontSize = 13.5, IsHitTestVisible = false, IsVisible = false,
    };

    private readonly TextBox _labelEditor = new()
    {
        IsVisible = false, MinWidth = 46, FontSize = 12, Padding = new Thickness(6, 2),
    };
    private MindLink? _editingLink;
    private Point _labelPos;

    private readonly System.Collections.Generic.HashSet<NoteBoxView> _selection = new();
    private readonly Border _rubber = new()
    {
        IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(9),
    };
    private Point _rubberStart, _rubberCur;
    private bool _rubbering, _rubberDown;
    private System.Collections.Generic.Dictionary<NoteBox, (double X, double Y)>? _groupOrigins;

    private void Rebuild()
    {
        Children.Clear();
        SetActive(null);
        Children.Add(_guides);
        _guides.Refresh();
        _links.Doc = _doc;
        _links.Resolve = BoxRect;
        _links.Refresh();
        Children.Add(_links);
        Children.Add(_hint);
        _editingLink = null;
        _labelEditor.IsVisible = false;
        Children.Add(_labelEditor);
        _selection.Clear();
        _rubber.IsVisible = false;
        _rubber.BorderBrush = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent), 0.9);
        _rubber.Background = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent), 0.12);
        Children.Add(_rubber);
        EnsureRegions();
        if (_doc is not null)
            foreach (var box in _doc.Boxes)
                Children.Add(new NoteBoxView(this, box));
        UpdateHint();
        InvalidateMeasure();
    }

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
        DockRegions();

        double w = 0, h = 0, notesFoot = 0;
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v) { child.Measure(Size.Infinity); continue; }
            v.Measure(new Size(v.Box.Width, double.PositiveInfinity));
            double bottom = v.Box.Y + Math.Max(v.DesiredSize.Height, v.Box.H);
            w = Math.Max(w, v.Box.X + v.Box.Width);
            h = Math.Max(h, bottom);
            if (v.Box.Region != "summary") notesFoot = Math.Max(notesFoot, bottom);
        }

        DockCornellSummary(notesFoot);
        _guides.ContentBottom = notesFoot;

        foreach (var child in Children)
            if (child is NoteBoxView sv && sv.Box.Region == "summary")
                h = Math.Max(h, sv.Box.Y + Math.Max(sv.DesiredSize.Height, sv.Box.H));

        return new Size(w + 220, h + 320);
    }

    private void DockRegions()
    {
        if (_doc is null || _viewport.Width <= 0 || _viewport.Height <= 0) return;
        if (_mode == PageStyles.ModeStartersOnly) return;
        var regions = PageStyleGuides.Regions(_pageStyle, _viewport, default);
        if (regions.Count == 0) return;
        EnsureRegions();
        foreach (var child in Children)
        {
            if (child is not NoteBoxView v || v.Box.Region is not { } id) continue;
            foreach (var (rid, rect) in regions)
                if (rid == id) { v.Box.X = rect.X; v.Box.Y = rect.Y; v.Box.Width = rect.Width; break; }
        }
    }

    private void DockCornellSummary(double notesFoot)
    {
        if (_pageStyle != PageStyles.Cornell || _viewport.Width <= 0 || _viewport.Height <= 0) return;
        var (_, _, summary) = PageStyleGuides.CornellRegions(_viewport.Width, _viewport.Height, notesFoot);
        foreach (var child in Children)
            if (child is NoteBoxView v && v.Box.Region == "summary")
                v.Box.Y = summary.Y;
    }

    private void EnsureRegions()
    {
        if (_doc is null || _doc.Boxes.Count == 0) return;
        if (_mode == PageStyles.ModeStartersOnly) return;

        PageStyleTemplate.RetagLegacyStarters(_doc.Boxes, _pageStyle, _viewport.Width > 0 ? _viewport : new Size(900, 600));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, _guides) || ReferenceEquals(child, _links))
            {
                child.Arrange(new Rect(finalSize));
                continue;
            }
            if (ReferenceEquals(child, _labelEditor))
            {
                var d = child.DesiredSize;
                child.Arrange(new Rect(_labelPos.X - d.Width / 2, _labelPos.Y - d.Height / 2, d.Width, d.Height));
                continue;
            }
            if (ReferenceEquals(child, _rubber))
            {
                child.Arrange(new Rect(Math.Min(_rubberStart.X, _rubberCur.X), Math.Min(_rubberStart.Y, _rubberCur.Y),
                    Math.Abs(_rubberStart.X - _rubberCur.X), Math.Abs(_rubberStart.Y - _rubberCur.Y)));
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
        _links.Animate();
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_doc is null || !ReferenceEquals(e.Source, this)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (_pageStyle == PageStyles.Mindmap)
        {

            if (e.ClickCount >= 2 && _links.HitLink(p) is { } hitLink) { EditLinkLabel(hitLink); e.Handled = true; return; }
            if (e.ClickCount >= 2) { AddBubble(p.X, p.Y); e.Handled = true; return; }
            _rubberStart = _rubberCur = p;
            _rubbering = false;
            _rubberDown = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (CreateOnDoubleClick && e.ClickCount < 2) return;
        double bx = p.X - 11, by = p.Y - 16;
        if (SnapToGrid) { bx = Math.Max(0, SnapX(bx)); by = Math.Max(0, SnapY(by)); }
        var view = AddBoxView(_doc.AddBox(bx, by, Math.Clamp(RichTextEditor.NewNoteWidthPref, 240, 640)));
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_rubberDown) return;
        _rubberCur = e.GetPosition(this);
        if (!_rubbering && (Math.Abs(_rubberCur.X - _rubberStart.X) > 4 || Math.Abs(_rubberCur.Y - _rubberStart.Y) > 4))
        {
            _rubbering = true;
            _rubber.Opacity = 0;
            _rubber.IsVisible = true;
            Views.Motion.FadeIn(_rubber, 110);
        }
        if (_rubbering) InvalidateArrange();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_rubberDown) return;
        _rubberDown = false;
        e.Pointer.Capture(null);
        if (_rubbering)
        {
            _rubbering = false;
            Views.Motion.FadeOut(_rubber, 150, () => _rubber.IsVisible = false);
            var rect = new Rect(Math.Min(_rubberStart.X, _rubberCur.X), Math.Min(_rubberStart.Y, _rubberCur.Y),
                                Math.Abs(_rubberStart.X - _rubberCur.X), Math.Abs(_rubberStart.Y - _rubberCur.Y));
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ClearSelection();
            foreach (var child in Children)
                if (child is NoteBoxView v && rect.Intersects(v.Bounds)) AddToSelection(v);
            InvalidateArrange();
            Focus();
        }
        else
        {
            ClearSelection();
            SetActive(null);
            Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && _pageStyle == PageStyles.Mindmap && _selection.Count > 0)
        {
            foreach (var v in _selection.ToList()) DeleteBoxToHistory(v);
            _selection.Clear();
            TrashChanged?.Invoke();
            e.Handled = true;
        }
    }

    internal bool IsSelected(NoteBoxView v) => _selection.Contains(v);
    private void AddToSelection(NoteBoxView v) { if (_selection.Add(v)) v.SetSelected(true); }
    private void ClearSelection() { foreach (var v in _selection) v.SetSelected(false); _selection.Clear(); }

    internal void BeginGroupDrag() => _groupOrigins = _selection.ToDictionary(v => v.Box, v => (v.Box.X, v.Box.Y));

    internal bool GroupDragTo(double dx, double dy)
    {
        if (_groupOrigins is null) return false;
        foreach (var (box, o) in _groupOrigins) { box.X = Math.Max(0, o.X + dx); box.Y = Math.Max(0, o.Y + dy); }
        InvalidateMeasure();
        return true;
    }

    internal void EndGroupDrag() => _groupOrigins = null;

    private NoteBoxView AddBoxView(NoteBox box)
    {
        var view = new NoteBoxView(this, box);
        Children.Add(view);

        view.RenderTransformOrigin = RelativePoint.Center;
        Views.Motion.Tween(view, 0, 0, 0.5, 0, 0, 1, 320,
            s => { double c1 = 2.4, c3 = c1 + 1, u = s - 1; return 1 + c3 * u * u * u + c1 * u * u; }, 0, 1);
        UpdateHint();
        InvalidateMeasure();
        return view;
    }

    public void RestoreBox(NoteBox box, double? x = null, double? y = null)
    {
        if (_doc is null) return;
        _doc.RestoreFromTrash(box, x, y);
        AddBoxView(box);
        foreach (var l in _doc.Links)
            if (ReferenceEquals(l.A, box) || ReferenceEquals(l.B, box))
                _links.AnimateLinkIn(l);
        TrashChanged?.Invoke();
    }

    internal void SetActive(RichTextEditor? editor)
    {
        if (editor is not null && _selection.Count > 0) ClearSelection();
        if (ReferenceEquals(ActiveEditor, editor)) return;
        ActiveEditor = editor;
        ActiveEditorChanged?.Invoke(editor);
    }

    internal async void RequestDelete(NoteBoxView view)
    {
        if (view.Box.Locked) return;
        if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
        if (!view.Box.IsEmpty && ConfirmDelete is not null && !await ConfirmDelete()) return;
        if (HistoryEnabled && !view.Box.IsEmpty)
        {
            if (BoxRect(view.Box) is { } r) _links.AnimateBubbleRemoval(view.Box, r);
            _doc.DeleteToTrash(view.Box);
            AnimateOutAndDetach(view);
            TrashChanged?.Invoke();
        }
        else
        {
            DeleteBoxPermanently(view);
        }
    }

    internal void DeleteBoxPermanently(NoteBoxView view)
    {
        if (BoxRect(view.Box) is { } r) _links.AnimateBubbleRemoval(view.Box, r);
        _doc?.RemoveBox(view.Box);
        AnimateOutAndDetach(view);
    }

    private void DeleteBoxToHistory(NoteBoxView view)
    {
        if (_doc is null) return;
        if (HistoryEnabled && !view.Box.IsEmpty)
        {
            if (BoxRect(view.Box) is { } r) _links.AnimateBubbleRemoval(view.Box, r);
            _doc.DeleteToTrash(view.Box);
            AnimateOutAndDetach(view);
        }
        else
        {
            DeleteBoxPermanently(view);
        }
    }

    private void AnimateOutAndDetach(NoteBoxView view)
    {
        if (ReferenceEquals(ActiveEditor, view.Editor)) SetActive(null);

        view.RenderTransformOrigin = RelativePoint.Center;
        Views.Motion.Tween(view, 0, 0, 1, 0, 0, 0.35, 170, Views.Motion.EaseIn, view.Opacity, 0, () =>
        {
            Children.Remove(view);
            UpdateHint();
            InvalidateMeasure();
        });
    }

    private void DetachView(NoteBoxView view)
    {
        Children.Remove(view);
        if (ReferenceEquals(ActiveEditor, view.Editor)) SetActive(null);
        UpdateHint();
        InvalidateMeasure();
    }

    internal void OnBoxDragEnd(NoteBoxView view) { _links.InvalidateVisual(); }

    internal void OnEditorLostFocus(NoteBoxView view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
            if (_pageStyle == PageStyles.Mindmap) return;
            if (!view.Box.IsEmpty || view.Box.Locked || view.IsKeyboardFocusWithin) return;
            if (view.Manipulated) return;
            DeleteBoxPermanently(view);
        }, DispatcherPriority.Background);
    }
}

internal sealed class NoteBoxView : Panel
{

    [System.Flags]
    private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    private readonly SolidColorBrush _edge = new(Colors.Transparent, 0);
    private DispatcherTimer? _edgeFade;

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

    private IBrush _closeRestBg = Brushes.Transparent;
    private IBrush _closeRestFg = Brushes.Gray;
    private IBrush _closeHoverBg = CloseHoverBg;
    private readonly Border _resizeLeft;
    private readonly Border _resizeRight;
    private readonly Border _resizeTop;
    private readonly Border _resizeBottom;
    private readonly Border _resizeCorner;
    private readonly Border _resizeCornerTL;
    private readonly Border _resizeCornerBL;

    private readonly System.Collections.Generic.List<(Border Port, bool Diagonal, string Dir)> _ports = new();
    private bool _linking;
    private bool _linkTarget;
    private bool _selected;
    private bool _hover;

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
            Cursor = Platform.AdaptiveCursors.For(StandardCursorType.SizeAll),
        };
        DockPanel.SetDock(_grip, Dock.Top);

        var body = new DockPanel();
        if (box.Divider is not null)
        {

            _grip.Height = double.NaN;
            _grip.CornerRadius = new CornerRadius(r);
            _grip.Child = BuildDividerLine(box.Divider);
            body.Children.Add(_grip);
        }
        else
        {
            body.Children.Add(_grip);
            if (box.ImagePath is { Length: > 0 })
                body.Children.Add(BuildImage(box.ImagePath));
            else if (box.AttachPath is { Length: > 0 })
                body.Children.Add(BuildAttachment(box.AttachPath));
            else if (box.Table is not null)
            {
                _tableHost = new Border { Child = BuildTableGrid() };
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

            ClipToBounds = true,
        };

        _closeRestFg = CloseFg;
        _closeGlyph = new TextBlock
        {

            Text = "\uE711",
            FontFamily = Avalonia.Application.Current?.FindResource("IconFont") as FontFamily
                         ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
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
        _close.PointerPressed += (_, e) => e.Handled = true;
        _close.PointerReleased += (_, e) => { _canvas.RequestDelete(this); e.Handled = true; };

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
            Background = Brushes.Transparent, Cursor = Platform.AdaptiveCursors.For(cur),
        };
        _resizeLeft = EdgeStrip(HorizontalAlignment.Left, VerticalAlignment.Stretch, true, StandardCursorType.SizeWestEast);
        _resizeRight = EdgeStrip(HorizontalAlignment.Right, VerticalAlignment.Stretch, true, StandardCursorType.SizeWestEast);
        _resizeTop = EdgeStrip(HorizontalAlignment.Stretch, VerticalAlignment.Top, false, StandardCursorType.SizeNorthSouth);
        _resizeBottom = EdgeStrip(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, false, StandardCursorType.SizeNorthSouth);
        _resizeCornerTL = Corner(HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner);
        _resizeCornerBL = Corner(HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner);
        _resizeCorner = Corner(HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner);

        const double o = -7;
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

        Children.Add(_chrome);
        Children.Add(_resizeLeft);
        Children.Add(_resizeRight);
        Children.Add(_resizeTop);
        Children.Add(_resizeBottom);
        Children.Add(_resizeCornerTL);
        Children.Add(_resizeCornerBL);
        Children.Add(_resizeCorner);
        Children.Add(_close);
        foreach (var (port, _, _) in _ports) Children.Add(port);

        AddHandler(InputElement.PointerPressedEvent, OnPaintClick, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        PointerEntered += (_, _) => { _hover = true; RefreshChrome(); };
        PointerExited += (_, _) => { _hover = false; RefreshChrome(); };

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

                var central = new MenuItem { Header = Box.Central ? "Remove central bubble" : "Make central bubble" };
                central.Click += (_, _) => _canvas.SetCentral(this, !Box.Central);
                menu.Items.Add(central);

                var type = new MenuItem { Header = "Bubble type" };
                void KindItem(string h, BubbleKind k)
                {
                    var m = new MenuItem { Header = h };
                    if (Box.Kind == k)
                        m.Icon = new Border
                        {
                            Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent)),
                            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                        };
                    m.Click += (_, _) => _canvas.SetBubbleKind(this, k);
                    type.Items.Add(m);
                }
                KindItem("Title", BubbleKind.Title);
                KindItem("Information", BubbleKind.Info);
                KindItem("Callout", BubbleKind.Callout);
                menu.Items.Add(type);
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

                var front = new MenuItem { Header = "Bring to front" };
                front.Click += (_, _) => _canvas.BringToFront(this);
                menu.Items.Add(front);
                var back = new MenuItem { Header = "Send to back" };
                back.Click += (_, _) => _canvas.SendToBack(this);
                menu.Items.Add(back);
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
            Views.MenuFx.Attach(menu);
            menu.Open(_grip);
            e.Handled = true;
        };

        ApplyFontScale();
        RefreshChrome();
    }

    private void OnPaintClick(object? sender, PointerPressedEventArgs e)
    {
        if (!_canvas.MindmapPaintActive || !_canvas.IsMindmap) return;
        if (Box.Divider is not null || Box.ImagePath is not null || Box.Table is not null || Box.AttachPath is not null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _canvas.PaintBubble(this);
        e.Handled = true;
    }

    private void OnBubbleKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.D && Services.Keymap.HasCommand(e.KeyModifiers) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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
        catch {  }
        return img;
    }

    private Control BuildAttachment(string relPath)
    {
        var t = Services.ThemeManager.Current;
        var text = new SolidColorBrush(Color.Parse(t.PaperText));
        var muted = new SolidColorBrush(Color.Parse(t.PaperTextMuted));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock
        {
            Text = "", FontFamily = Avalonia.Application.Current?.FindResource("IconFont") as FontFamily
                         ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
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

    private void OpenAttachment()
    {
        try
        {
            var root = _canvas.ImageRoot;
            var full = root is { Length: > 0 }
                ? System.IO.Path.Combine(root, Box.AttachPath!) : Box.AttachPath!;
            if (!System.IO.File.Exists(full)) return;

            if (full.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && _canvas.OpenPdfRequested is { } openPdf)
                openPdf(full);
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = full, UseShellExecute = true });
        }
        catch {  }
    }

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

        bool active = _hover || focused || _dragging;
        if (Box.Color is { } hex && Color.TryParse(hex, out var bc))
        {

            _chrome.Background = new SolidColorBrush(bc, active ? 0.28 : 0.18);
            _chrome.BorderBrush = new SolidColorBrush(bc, active ? 1.0 : 0.72);
        }
        else
        {
            _chrome.Background = Brushes.Transparent;

            bool strong = _dragging || focused;
            var edgeSrc = strong ? FocusBorder : HoverBorder;
            if (edgeSrc is ISolidColorBrush sb) _edge.Color = sb.Color;
            double target = strong || _hover ? 1 : (_canvas.AlwaysShowBorders ? 0.55 : 0);
            FadeEdge(target);
            _chrome.BorderBrush = _edge;
        }
        _grip.Background = active ? GripFill : Brushes.Transparent;
        _gripBar.IsVisible = active;
        _close.IsVisible = active && !Box.Locked;

        bool resize = _canvas.CanResize && !Box.Locked;
        _resizeRight.IsVisible = resize && Box.Divider != "v";
        _resizeBottom.IsVisible = resize && Box.Divider != "h";

        bool full = resize && Box.Divider is null;
        _resizeLeft.IsVisible = full;
        _resizeTop.IsVisible = full;
        _resizeCorner.IsVisible = full;
        _resizeCornerTL.IsVisible = full;
        _resizeCornerBL.IsVisible = full;

        bool normalBox = Box.Divider is null && Box.ImagePath is null && Box.Table is null && Box.AttachPath is null;
        bool bubble = _canvas.IsMindmap && normalBox;
        var kind = Box.Kind;
        bool titlePill = kind == BubbleKind.Title;

        _chrome.BorderThickness = !bubble ? new Thickness(1)
            : kind == BubbleKind.Callout ? new Thickness(7, 1.6, 1.6, 1.6)
            : new Thickness(titlePill ? (Box.Central ? 6.5 : 2.6) : 2.2);
        if (normalBox)
        {
            double rad = !bubble ? NoteCanvas.NoteRadiusPref
                       : titlePill ? 999
                       : kind == BubbleKind.Info ? 16
                       : 7;
            _chrome.CornerRadius = new CornerRadius(rad);
            _grip.CornerRadius = new CornerRadius(rad, rad, 0, 0);
        }
        if (bubble)
        {

            Editor.ForceCenter = titlePill;
            Editor.ForceBold = titlePill && Box.Central;
            Editor.Margin = titlePill ? new Thickness(10, 3, 10, 20)
                          : kind == BubbleKind.Callout ? new Thickness(15, 5, 12, 10)
                          : new Thickness(12, 5, 12, 10);

            _close.Width = _close.Height = 16;
            _close.CornerRadius = new CornerRadius(8);
            _close.Clip = null;
            _closeGlyph.FontSize = 8;
            _closeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            _closeGlyph.VerticalAlignment = VerticalAlignment.Center;
            _closeGlyph.Margin = default;
            _closeRestBg = new SolidColorBrush(Colors.Black, 0.22);
            _closeRestFg = new SolidColorBrush(Colors.White, 0.80);
            _closeHoverBg = new SolidColorBrush(Color.Parse("#E81123"));
        }
        else if (normalBox)
        {
            _close.Width = _close.Height = 17;
            _close.CornerRadius = new CornerRadius(0, NoteCanvas.NoteRadiusPref, 0, 6);
            _close.Margin = default;
            _close.Clip = null;
            _closeGlyph.FontSize = 7.5;
            _closeGlyph.Margin = default;
            _closeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            _closeGlyph.VerticalAlignment = VerticalAlignment.Center;
            _closeRestBg = Brushes.Transparent;
            _closeRestFg = CloseFg;
            _closeHoverBg = CloseHoverBg;
        }
        _close.Background = _closeRestBg;
        _closeGlyph.Foreground = _closeRestFg;

        bool showPorts = bubble && (active || _linking || _linkTarget);
        var portBrush = Box.Color is { } ph && Color.TryParse(ph, out var pc)
            ? new SolidColorBrush(pc) : new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent));
        foreach (var (port, diagonal, _) in _ports)
        {
            port.IsVisible = showPorts && (!diagonal || _canvas.MindmapDiagonalPorts);
            if (port.IsVisible) port.Background = portBrush;
        }

        if (_selected)
            _chrome.BorderBrush = new SolidColorBrush(Color.Parse(Services.ThemeManager.Current.Accent));
    }

    private void FadeEdge(double target)
    {
        if (System.Math.Abs(_edge.Opacity - target) < 0.01) return;
        _edgeFade?.Stop();
        if (!Views.Motion.Enabled) { _edge.Opacity = target; return; }
        double from = _edge.Opacity;
        _edgeFade = Views.Motion.Clock(130,
            p => _edge.Opacity = Views.Motion.Lerp(from, target, Views.Motion.EaseOut(p)),
            done: () => _edge.Opacity = target);
    }

    internal void SetLinkTarget(bool on)
    {
        if (_linkTarget == on) return;
        _linkTarget = on;
        RefreshChrome();
    }

    internal void SetSelected(bool on)
    {
        if (_selected == on) return;
        _selected = on;
        RefreshChrome();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_canvas.IsMindmap)
        {
            PlaceDiagonalPorts(finalSize.Width, finalSize.Height);
            bool bubble = Box.Divider is null && Box.ImagePath is null && Box.Table is null && Box.AttachPath is null;
            if (bubble)
            {

                double bt = _chrome.BorderThickness.Top;
                double cr = Box.Kind switch
                {
                    BubbleKind.Title => Math.Min(finalSize.Width, finalSize.Height) / 2,
                    BubbleKind.Info => 16,
                    _ => 7,
                };
                double m = 0.293 * cr + 0.707 * bt + 1;
                _close.Margin = new Thickness(0, m, m, 0);
            }
        }
        return base.ArrangeOverride(finalSize);
    }

    private void PlaceDiagonalPorts(double w, double h)
    {
        double rad = Math.Min(w, h) / 2;
        double m = 0.2929 * rad - 7;
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

    internal bool Manipulated { get; private set; }

    private void WireDrag(Control handle, Edge edges)
    {
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            if (Box.Locked) return;
            _dragStart = e.GetPosition(_canvas);

            _dragOrigin = (Box.X, Box.Y, Box.Width, Box.H > 0 ? Box.H : Bounds.Height);
            _dragging = true;
            Manipulated = true;
            if (edges == Edge.None && _canvas.IsSelected(this)) _canvas.BeginGroupDrag();
            RefreshChrome();
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
            if (edges == Edge.None)
            {
                if (!_canvas.GroupDragTo(dx, dy))
                {
                    double nx = ox + dx, ny = oy + dy;
                    if (snap) { nx = _canvas.SnapX(nx); ny = _canvas.SnapY(ny); }
                    Box.X = Math.Max(0, nx);
                    Box.Y = Math.Max(0, ny);
                }
            }
            else
            {
                if ((edges & (Edge.Left | Edge.Right)) != 0)
                {
                    double nw = edges.HasFlag(Edge.Left) ? ow - dx : ow + dx;
                    if (snap) nw = _canvas.SnapX(nw);
                    nw = Math.Clamp(nw, Box.Divider == "h" ? NoteBox.MinDividerLength : NoteBox.MinWidth, 1600);
                    Box.Width = nw;
                    if (edges.HasFlag(Edge.Left)) Box.X = Math.Max(0, ox + ow - nw);
                }
                if ((edges & (Edge.Top | Edge.Bottom)) != 0)
                {
                    double nh = edges.HasFlag(Edge.Top) ? oh - dy : oh + dy;
                    if (snap) nh = _canvas.SnapY(nh);
                    nh = Math.Clamp(nh, Box.Divider == "v" ? NoteBox.MinDividerLength : NoteBox.MinHeight, 4000);
                    Box.H = nh;
                    if (edges.HasFlag(Edge.Top)) Box.Y = Math.Max(0, oy + oh - nh);
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
            RefreshChrome();
            if (edges == Edge.None) { _canvas.EndGroupDrag(); _canvas.OnBoxDragEnd(this); }
            _canvas.Document?.CommitGeometry();
            e.Handled = true;
        };
    }
}
