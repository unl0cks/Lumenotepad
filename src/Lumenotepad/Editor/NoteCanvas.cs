using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumenotepad.Editor;

/// <summary>The freeform page canvas (the OneNote model): any number of movable, width-resizable
/// note containers, each holding its own rich document. Click empty space to start a new container
/// there; a container that loses focus while still empty evaporates.</summary>
public sealed class NoteCanvas : Panel
{
    private CanvasDocument? _doc;

    /// <summary>The page's canvas document; setting it rebuilds all container views.</summary>
    public CanvasDocument? Document
    {
        get => _doc;
        set { _doc = value; Rebuild(); }
    }

    /// <summary>The editor of the most recently focused container (what the toolbar targets).</summary>
    public RichTextEditor? ActiveEditor { get; private set; }
    public event Action<RichTextEditor?>? ActiveEditorChanged;

    // An un-rendered control is not hit-testable — bare-canvas clicks would fall through.
    public NoteCanvas() => Background = Brushes.Transparent;

    private void Rebuild()
    {
        Children.Clear();
        SetActive(null);
        if (_doc is not null)
            foreach (var box in _doc.Boxes)
                Children.Add(new NoteBoxView(this, box));
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0, h = 0;
        foreach (var child in Children)
        {
            var v = (NoteBoxView)child;
            v.Measure(new Size(v.Box.Width, double.PositiveInfinity));
            w = Math.Max(w, v.Box.X + v.Box.Width);
            h = Math.Max(h, v.Box.Y + v.DesiredSize.Height);
        }
        return new Size(w + 220, h + 320);        // breathing room so the page can always grow by clicking
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var v = (NoteBoxView)child;
            v.Arrange(new Rect(v.Box.X, v.Box.Y, v.Box.Width, v.DesiredSize.Height));
        }
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Only clicks on bare canvas start a container — clicks inside one bubble with another Source.
        if (_doc is null || !ReferenceEquals(e.Source, this)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var view = AddBoxView(_doc.AddBox(p.X - 11, p.Y - 16));
        Dispatcher.UIThread.Post(view.FocusEditor, DispatcherPriority.Background);
        e.Handled = true;
    }

    private NoteBoxView AddBoxView(NoteBox box)
    {
        var view = new NoteBoxView(this, box);
        Children.Add(view);
        InvalidateMeasure();
        return view;
    }

    internal void SetActive(RichTextEditor? editor)
    {
        if (ReferenceEquals(ActiveEditor, editor)) return;
        ActiveEditor = editor;
        ActiveEditorChanged?.Invoke(editor);
    }

    internal void DeleteBox(NoteBoxView view)
    {
        _doc?.RemoveBox(view.Box);
        Children.Remove(view);
        if (ReferenceEquals(ActiveEditor, view.Editor)) SetActive(null);
        InvalidateMeasure();
    }

    /// <summary>OneNote behavior: an empty container evaporates once focus has settled elsewhere.
    /// Deferred a beat so the click that stole focus can land first.</summary>
    internal void OnEditorLostFocus(NoteBoxView view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_doc is null || !_doc.Boxes.Contains(view.Box)) return;
            if (!view.Box.IsEmpty || view.IsKeyboardFocusWithin) return;
            DeleteBox(view);
        }, DispatcherPriority.Background);
    }
}

/// <summary>One container: hover/focus chrome, a top drag-grip (right-click → delete), the editor,
/// and a right-edge width-resize strip. Geometry lives on the NoteBox model; the canvas arranges
/// from it, so drags just mutate the model and re-measure.</summary>
internal sealed class NoteBoxView : Panel
{
    private static readonly IBrush HoverBorder = new SolidColorBrush(Color.Parse("#26FFFFFF"));
    private static readonly IBrush FocusBorder = new SolidColorBrush(Color.Parse("#4D4DA6FF"));
    private static readonly IBrush GripFill = new SolidColorBrush(Color.Parse("#12FFFFFF"));
    private static readonly IBrush GripBarFill = new SolidColorBrush(Color.Parse("#3DFFFFFF"));

    internal NoteBox Box { get; }
    internal RichTextEditor Editor { get; }

    private readonly NoteCanvas _canvas;
    private readonly Border _chrome;
    private readonly Border _grip;
    private readonly Border _gripBar;
    private readonly Border _resize;
    private bool _hover;

    public NoteBoxView(NoteCanvas canvas, NoteBox box)
    {
        _canvas = canvas;
        Box = box;
        Editor = new RichTextEditor { Document = box.Doc, Margin = new Thickness(10, 3, 10, 9) };

        _gripBar = new Border
        {
            Width = 38, Height = 4, CornerRadius = new CornerRadius(2), Background = GripBarFill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        _grip = new Border
        {
            Height = 15, Background = Brushes.Transparent, Child = _gripBar,
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        DockPanel.SetDock(_grip, Dock.Top);

        var body = new DockPanel();
        body.Children.Add(_grip);
        body.Children.Add(Editor);

        _chrome = new Border
        {
            Child = body, CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
        };

        _resize = new Border
        {
            Width = 7, HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };

        Children.Add(_chrome);
        Children.Add(_resize);

        PointerEntered += (_, _) => { _hover = true; UpdateChrome(); };
        PointerExited += (_, _) => { _hover = false; UpdateChrome(); };
        Editor.GotFocus += (_, _) => { _canvas.SetActive(Editor); UpdateChrome(); };
        Editor.LostFocus += (_, _) => { UpdateChrome(); _canvas.OnEditorLostFocus(this); };

        WireDrag(_grip, move: true);
        WireDrag(_resize, move: false);

        _grip.ContextRequested += (_, e) =>
        {
            var menu = new ContextMenu();
            var del = new MenuItem { Header = "Delete container" };
            del.Click += (_, _) => _canvas.DeleteBox(this);
            menu.Items.Add(del);
            menu.Open(_grip);
            e.Handled = true;
        };
    }

    internal void FocusEditor() => Editor.Focus();

    private void UpdateChrome()
    {
        bool focused = Editor.IsFocused;
        _chrome.BorderBrush = focused ? FocusBorder : _hover ? HoverBorder : Brushes.Transparent;
        _grip.Background = _hover || focused ? GripFill : Brushes.Transparent;
        _gripBar.IsVisible = _hover || focused;
    }

    private Point _dragStart;
    private (double X, double Y, double W) _dragOrigin;
    private bool _dragging;

    private void WireDrag(Control handle, bool move)
    {
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            _dragStart = e.GetPosition(_canvas);
            _dragOrigin = (Box.X, Box.Y, Box.Width);
            _dragging = true;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(_canvas);
            if (move)
            {
                Box.X = Math.Max(0, _dragOrigin.X + p.X - _dragStart.X);
                Box.Y = Math.Max(0, _dragOrigin.Y + p.Y - _dragStart.Y);
            }
            else
            {
                Box.Width = Math.Clamp(_dragOrigin.W + p.X - _dragStart.X, NoteBox.MinWidth, 1600);
            }
            _canvas.InvalidateMeasure();
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            _canvas.Document?.CommitGeometry();      // persist the final geometry once
            e.Handled = true;
        };
    }
}
