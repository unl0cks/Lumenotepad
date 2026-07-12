using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumenotepad.Editor;
using Lumenotepad.Models;
using Lumenotepad.Platform;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                TopLevel.GetTopLevel(this) is Window w && !WinChrome.BeginNativeMoveDrag(w))
                w.BeginMoveDrag(e);
        };

        MinBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = WindowState.Minimized; };
        MaxBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
        CloseBtn.Click += (_, _) => Window?.Close();

        // Header rename fields (notebook, page) commit on blur / Enter.
        foreach (var box in new[] { NotebookName, PageTitle })
        {
            box.LostFocus += (_, _) => Vm?.Save();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Vm?.Save(); ((Control?)s)?.Focus(); } };
        }

        // The canvas edits the selected page's document.
        DataContextChanged += (_, _) => HookVm();

        // The toolbar follows whichever note container was focused last; dock menu re-docks it (persisted).
        PageCanvas.ActiveEditorChanged += ed => { if (ed is not null) Toolbar.Target = ed; };

        // Deleting a container asks first; the deleted history panel lists what can come back.
        PageCanvas.ConfirmDelete = () =>
            Vm is { ConfirmDeleteContainer: false }
                ? System.Threading.Tasks.Task.FromResult(true)
                : ConfirmDialog.Show(Window!,
                    "Delete this container?",
                    PageCanvas.HistoryEnabled
                        ? "It will move to this page's deleted history — you can drag it back onto the page anytime."
                        : "The deleted history is turned off, so this can't be undone.");
        PageCanvas.TrashChanged += () => { if (TrashPanel.IsVisible) RefreshTrashPanel(); };
        HistoryBtn.Click += (_, _) =>
        {
            TrashPanel.IsVisible = !TrashPanel.IsVisible;
            if (TrashPanel.IsVisible) RefreshTrashPanel();
        };
        Toolbar.DockRequested += pos => { if (Vm is { } vm) vm.ToolbarPosition = pos; };
        Toolbar.ScopeRequested += scope => { if (Vm is { } vm) vm.ToolbarScope = scope; };

        // Section inline rename: double-click a tab, the rename button, or right-click → Rename.
        RenameSectionBtn.Click += (_, _) => BeginRenameSection(Vm?.SelectedSection);
        SectionsList.DoubleTapped += (_, e) => BeginRenameSection((e.Source as StyledElement)?.DataContext as Section);
        SectionsList.AddHandler(LostFocusEvent, OnSectionEditLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        SectionsList.AddHandler(KeyDownEvent, OnSectionEditKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // Delete lives on the right-click menu now (no delete buttons), always behind an "are you sure" prompt.
        SectionsList.ContextRequested += OnSectionsContextRequested;
        NotebooksList.ContextRequested += OnNotebooksContextRequested;
        PagesList.ContextRequested += OnPagesContextRequested;
        NotebookName.ContextRequested += OnNotebookNameContextRequested;

        // Homepage gallery: click a card to open it, right-click for open/rename/color/delete.
        HomeCards.AddHandler(TappedEvent, OnHomeCardTapped);
        HomeCards.ContextRequested += OnHomeCardContextRequested;
        RecentList.AddHandler(TappedEvent, (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is RecentPage r)
                Vm?.OpenRecentCommand.Execute(r);
        });

        PrefsBtn.Click += (_, _) => OpenPreferences();
        HomePrefsBtn.Click += (_, _) => OpenPreferences();
        SortBtn.Click += (_, _) => OpenSortMenu();

        // Rearrange mode: cards wiggle and can be dragged into new slots (click the button again to stop).
        RearrangeBtn.Click += (_, _) => SetRearranging(!_rearranging);
        HomeCards.AddHandler(PointerPressedEvent, OnRearrangePressed, RoutingStrategies.Tunnel);
        HomeCards.PointerMoved += OnRearrangeMoved;
        HomeCards.PointerReleased += OnRearrangeReleased;

        // Hover scale — driven from code-behind as a local RenderTransform because the :pointerover
        // STYLE path does not move RenderTransform in this build (SetHoverCard / SetHoverChip).
        HomeCards.PointerMoved += (_, e) => { if (_dragNotebook is null) SetHoverCard(Ancestor(e.Source, "nbcard")); };
        HomeCards.PointerExited += (_, _) => SetHoverCard(null);
        RecentList.PointerMoved += (_, e) => SetHoverChip(Ancestor(e.Source, "recentchip"));
        RecentList.PointerExited += (_, _) => SetHoverChip(null);

        // Keep the canvas plate's punched hole aligned with the page box (margin 14, radius 14).
        CanvasPlate.SizeChanged += (_, _) => UpdateCanvasPlateClip();
    }

    // ---- transform animation (delegates to the shared Motion engine) ------------------------------
    private static double ScaleNow(Visual b) => b.RenderTransform?.Value is { } m && m.M11 > 0 ? m.M11 : 1;

    // ---- hover scale (smooth, code-behind) --------------------------------------------------------

    private Border? _hoverCard;
    private Border? _hoverChip;
    // Cards mid drop-glide: hover must NOT touch them, or its Tween cancels the glide (and its onDone,
    // where the reorder happens) — which looked like "moving won't work" + the card popping.
    private readonly System.Collections.Generic.HashSet<Visual> _settling = new();

    private static Border? Ancestor(object? source, string cls)
    {
        if (source is not Visual v) return null;
        return v.FindAncestorOfType<Border>(includeSelf: true) is { } b && b.Classes.Contains(cls)
            ? b : v.GetVisualAncestors().OfType<Border>().FirstOrDefault(x => x.Classes.Contains(cls));
    }

    private void SetHoverCard(Border? card)
    {
        if (card is not null && _settling.Contains(card)) card = null;   // let the drop glide finish
        if (ReferenceEquals(_hoverCard, card)) return;
        var old = _hoverCard;
        _hoverCard = card;
        if (old is not null && !ReferenceEquals(old, _dragCard) && !_settling.Contains(old))
            Motion.Tween(old, 0, 0, ScaleNow(old), 0, 0, 1.0, 120);
        if (card is not null && !ReferenceEquals(card, _dragCard))
            Motion.Tween(card, 0, 0, ScaleNow(card), 0, 0, 1.04, 150);
    }

    private void SetHoverChip(Border? chip)
    {
        if (ReferenceEquals(_hoverChip, chip)) return;
        var old = _hoverChip;
        _hoverChip = chip;
        if (old is not null) Motion.Tween(old, 0, 0, ScaleNow(old), 0, 0, 1.0, 120);
        if (chip is not null) Motion.Tween(chip, 0, 0, ScaleNow(chip), 0, 0, 1.035, 150);
    }

    // ---- selected-item scale (rail chip, section tab, page row) -----------------------------------
    // The :selected STYLE can't move RenderTransform either, so the "lit" scale is driven here too.
    private Control? _selRail, _selSection, _selPage;

    private void ScaleSelect(ref Control? cur, Control? next, double scale)
    {
        if (ReferenceEquals(cur, next)) return;
        if (cur is not null) Motion.Tween(cur, 0, 0, ScaleNow(cur), 0, 0, 1.0, 140);
        cur = next;
        if (next is not null) Motion.Tween(next, 0, 0, ScaleNow(next), 0, 0, scale, 170);
    }

    private void UpdateSelectionScale()
    {
        Control? rail = null;
        if (Vm?.SelectedNotebook is { } nb && NotebooksList.ContainerFromItem(nb) is { } nc)
            rail = nc.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("railchip"));
        ScaleSelect(ref _selRail, rail, 1.05);
        ScaleSelect(ref _selSection, Vm?.SelectedSection is { } s ? SectionsList.ContainerFromItem(s) as Control : null, 1.03);
        ScaleSelect(ref _selPage, Vm?.SelectedPage is { } pg ? PagesList.ContainerFromItem(pg) as Control : null, 1.02);
    }

    // ---- gallery rearrange mode ----

    private bool _rearranging;
    private Notebook? _dragNotebook;

    private void SetRearranging(bool on)
    {
        _rearranging = on;
        ResetDrag();                                    // never leave a card stranded mid-drag
        HomeCards.Classes.Set("rearrange", on);
        RearrangeBtn.Classes.Set("on", on);
    }

    // The dragged card is rendered as a SNAPSHOT floating in DragLayer (a Canvas), so it can move
    // freely without being clipped to its grid cell. The real card is hidden while it floats; the
    // live reorder still runs on the real cards underneath so the grid reflows.
    private Border? _ghost;        // floating snapshot
    private Border? _dragCard;     // the real card, hidden during the drag
    private Size _ghostSize;
    private Point _grabOffset;     // cursor position relative to the ghost's top-left
    private DispatcherTimer? _ghostTween;

    private void ResetDrag()
    {
        _dragNotebook = null;
        _ghostTween?.Stop(); _ghostTween = null;
        if (_ghost is not null) { DragLayer.Children.Remove(_ghost); _ghost = null; }
        if (_dragCard is not null) { _dragCard.Opacity = 1; _dragCard = null; }
    }

    private void OnRearrangePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_rearranging) return;
        if ((e.Source as StyledElement)?.DataContext is not Notebook nb) return;
        var card = HomeCards.ContainerFromItem(nb)?.GetVisualDescendants()
            .OfType<Border>().FirstOrDefault(x => x.Classes.Contains("nbcard"));
        if (card is null || card.Bounds.Width < 1 || card.Bounds.Height < 1) return;

        _dragNotebook = nb;
        _dragCard = card;
        card.ClearValue(Visual.RenderTransformProperty);   // snapshot the card clean (no leftover hover scale)

        var size = card.Bounds.Size;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(System.Math.Max(1, (int)(size.Width * scaling)), System.Math.Max(1, (int)(size.Height * scaling))),
            new Vector(96 * scaling, 96 * scaling));
        rtb.Render(card);

        const double lift = 1.06;
        _ghostSize = new Size(size.Width * lift, size.Height * lift);
        _ghost = new Border
        {
            Width = _ghostSize.Width, Height = _ghostSize.Height,
            CornerRadius = new CornerRadius(14),
            BoxShadow = BoxShadows.Parse("0 10 26 0 #70000000"),
            Child = new Image { Source = rtb, Stretch = Stretch.Fill },
            IsHitTestVisible = false,
        };
        DragLayer.Children.Add(_ghost);
        card.Opacity = 0;                                  // the ghost stands in for it
        if (ReferenceEquals(_hoverCard, card)) _hoverCard = null;

        // Lift the ghost IN PLACE (centred on the card) so it doesn't jump to the cursor; then keep
        // the grab point under the cursor as it moves.
        var centre = card.TranslatePoint(new Point(size.Width / 2, size.Height / 2), DragLayer) ?? default;
        Canvas.SetLeft(_ghost, centre.X - _ghostSize.Width / 2);
        Canvas.SetTop(_ghost, centre.Y - _ghostSize.Height / 2);
        var cur = e.GetPosition(DragLayer);
        _grabOffset = new Point(cur.X - Canvas.GetLeft(_ghost), cur.Y - Canvas.GetTop(_ghost));

        e.Pointer.Capture(HomeCards);
        e.Handled = true;
    }

    private void PositionGhost(Point pInLayer)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, pInLayer.X - _grabOffset.X);
        Canvas.SetTop(_ghost, pInLayer.Y - _grabOffset.Y);
    }

    private void OnRearrangeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragNotebook is null || _ghost is null) return;
        PositionGhost(e.GetPosition(DragLayer));

        // Live reorder: slide the OTHER cards out of the way as the cursor enters their slot. The
        // dragged card's own (hidden) container moves too; the floating ghost shows where it is.
        var dragged = _dragNotebook;
        var container = HomeCards.ContainerFromItem(dragged);
        int curIdx = container is null ? -1 : HomeCards.IndexFromContainer(container);
        int target = -1;
        foreach (var c in HomeCards.GetRealizedContainers())
        {
            if (ReferenceEquals(c, container)) continue;
            var p = e.GetPosition(c);
            if (p.X < 0 || p.Y < 0 || p.X > c.Bounds.Width || p.Y > c.Bounds.Height) continue;
            target = HomeCards.IndexFromContainer(c);
            break;
        }
        if (target < 0 || target == curIdx) return;

        var old = new System.Collections.Generic.Dictionary<object, Point>();
        foreach (var c in HomeCards.GetRealizedContainers())
            if (HomeCards.ItemFromContainer(c) is { } it && !ReferenceEquals(it, dragged))
                old[it] = Center(c);
        Vm?.MoveNotebookTo(dragged, target, save: false);
        Dispatcher.UIThread.Post(() =>
        {
            if (_dragNotebook is null) return;
            foreach (var c in HomeCards.GetRealizedContainers())
            {
                if (HomeCards.ItemFromContainer(c) is not { } it) continue;
                var nbc = c.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Classes.Contains("nbcard"));
                if (nbc is null) continue;
                if (ReferenceEquals(it, dragged)) { nbc.Opacity = 0; _dragCard = nbc; continue; }  // keep it hidden after regen
                if (!old.TryGetValue(it, out var op)) continue;
                var np = Center(c);
                double dx = op.X - np.X, dy = op.Y - np.Y;
                if (System.Math.Abs(dx) < 0.5 && System.Math.Abs(dy) < 0.5) continue;
                _settling.Add(nbc);
                Motion.Tween(nbc, dx, dy, 1.0, 0, 0, 1.0, 170, onDone: () => _settling.Remove(nbc));
            }
        }, DispatcherPriority.Render);
    }

    private void OnRearrangeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragNotebook is null) return;
        var dragged = _dragNotebook;
        var ghost = _ghost;
        _dragNotebook = null; _ghost = null;
        e.Pointer.Capture(null);
        Vm?.Save();
        if (ghost is null) { ResetDrag(); return; }

        // Glide the ghost into the dragged card's (already-reordered) slot, then reveal the real card.
        var card = HomeCards.ContainerFromItem(dragged)?.GetVisualDescendants()
            .OfType<Border>().FirstOrDefault(x => x.Classes.Contains("nbcard"));
        Point targetTL;
        if (card is not null)
        {
            var cc = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), DragLayer) ?? default;
            targetTL = new Point(cc.X - _ghostSize.Width / 2, cc.Y - _ghostSize.Height / 2);
        }
        else targetTL = new Point(Canvas.GetLeft(ghost), Canvas.GetTop(ghost));

        var reveal = card ?? _dragCard;
        _dragCard = null;
        TweenGhost(ghost, targetTL, 190, () =>
        {
            DragLayer.Children.Remove(ghost);
            if (reveal is not null) reveal.Opacity = 1;
        });
    }

    private void TweenGhost(Border ghost, Point target, int ms, System.Action onDone)
    {
        _ghostTween?.Stop();
        // The ghost runs its own timer (Canvas position, not a transform) — honor the reduce-motion
        // pref here too so the drop snap matches the rest of the app.
        if (!Motion.Enabled)
        {
            Canvas.SetLeft(ghost, target.X);
            Canvas.SetTop(ghost, target.Y);
            _ghostTween = null;
            onDone();
            return;
        }
        double fx = Canvas.GetLeft(ghost), fy = Canvas.GetTop(ghost);
        if (double.IsNaN(fx)) fx = target.X;
        if (double.IsNaN(fy)) fy = target.Y;
        int step = 0, steps = System.Math.Max(1, Motion.Ms(ms) / 15);
        var timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            step++;
            double p = System.Math.Min(1.0, step / (double)steps);
            double ep = 1 - System.Math.Pow(1 - p, 3);
            Canvas.SetLeft(ghost, fx + (target.X - fx) * ep);
            Canvas.SetTop(ghost, fy + (target.Y - fy) * ep);
            if (step >= steps) { timer.Stop(); _ghostTween = null; onDone(); }
        };
        _ghostTween = timer;
        timer.Start();
    }

    private Point Center(Visual c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), HomeCards) ?? default;

    private void OpenSortMenu()
    {
        var byName = new MenuItem { Header = "Sort by name" };
        byName.Click += (_, _) => Vm?.SortNotebooksByName();
        var byRecent = new MenuItem { Header = "Recently edited first" };
        byRecent.Click += (_, _) => Vm?.SortNotebooksByRecent();
        var hint = new MenuItem
        {
            Header = "Custom order: right-click a card → Move", IsEnabled = false, FontSize = 11.5,
        };
        var menu = new MenuFlyout();
        menu.Items.Add(byName);
        menu.Items.Add(byRecent);
        menu.Items.Add(new Separator());
        menu.Items.Add(hint);
        menu.ShowAt(SortBtn);
    }

    private void UpdateCanvasPlateClip()
    {
        var b = CanvasPlate.Bounds;
        if (b.Width <= 30 || b.Height <= 30) { CanvasPlate.Clip = null; return; }
        var hole = new RectangleGeometry(new Rect(14, 14, b.Width - 28, b.Height - 28))
        {
            RadiusX = 14, RadiusY = 14,
        };
        CanvasPlate.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, b.Width, b.Height)), hole);
    }

    private PreferencesWindow? _prefs;

    private void OpenPreferences()
    {
        if (_prefs is not null) { _prefs.Activate(); return; }
        _prefs = new PreferencesWindow { DataContext = Vm };
        _prefs.Closed += (_, _) => _prefs = null;
        if (Window is { } w) _prefs.Show(w);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;
    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private MainViewModel? _hookedVm;
    private Avalonia.Threading.DispatcherTimer? _autosave;

    private void HookVm()
    {
        if (_hookedVm is not null)
        {
            _hookedVm.PropertyChanged -= OnVmPropertyChanged;
            _hookedVm.DocsDirtied -= OnDocsDirtied;
        }
        _hookedVm = Vm;
        if (_hookedVm is not null)
        {
            _hookedVm.PropertyChanged += OnVmPropertyChanged;
            _hookedVm.DocsDirtied += OnDocsDirtied;
            ApplyBulletPrefs(rebuild: false);
            ApplyEditorPrefs(rebuild: false);
            SyncEditorDocument();
            ApplyToolbarPlacement();
            ApplyCanvasPrefs();
            ApplyGlossyAccents();
            ApplyCardSize();
            ApplyPanels();
            ApplyGlassTint();
            ApplyMotionPrefs();
            ApplyHomeSurface();
            HookCollectionAnimations();
            Toolbar.SetFontPrefs(_hookedVm.ExtendedFonts, _hookedVm.DisabledFontsList);
            Toolbar.SetPalettes(
                _hookedVm.PaletteFor(highlight: true, FormatToolbar.BuiltInHighlights),
                _hookedVm.PaletteFor(highlight: false, FormatToolbar.BuiltInTextColors));
        }
    }

    /// <summary>Push the preferences-window canvas toggles onto the canvas.</summary>
    private void ApplyCanvasPrefs()
    {
        if (Vm is not { } vm) return;
        PageCanvas.CanResize = vm.ResizablePages;
        PageCanvas.HistoryEnabled = vm.DeletedHistory;
        PageCanvas.GridStyle = vm.PageGrid;
        PageCanvas.SnapToGrid = vm.GridSnap;
        if (!vm.DeletedHistory) TrashPanel.IsVisible = false;
        ApplyFlatCovers();
    }

    /// <summary>"Flat covers": a class on the hosts hides every Border.cardfx gloss overlay.</summary>
    private void ApplyFlatCovers()
    {
        bool flat = Vm?.FlatCovers ?? false;
        HomeCards.Classes.Set("flat", flat);
        NotebooksList.Classes.Set("flat", flat);
    }

    // ---- add / delete animations (list mutations rise in / collapse out) --------------------------
    private bool _notebooksHooked;
    private System.Collections.Specialized.INotifyCollectionChanged? _sections, _pages;

    private void HookCollectionAnimations()
    {
        if (!_notebooksHooked && Vm?.Notebooks is System.Collections.Specialized.INotifyCollectionChanged nc)
        { nc.CollectionChanged += OnNotebooksChanged; _notebooksHooked = true; }
        RehookSections();
        RehookPages();
    }

    private void RehookSections()
    {
        if (_sections is not null) _sections.CollectionChanged -= OnSectionsChanged;
        _sections = Vm?.SelectedNotebook?.Sections as System.Collections.Specialized.INotifyCollectionChanged;
        if (_sections is not null) _sections.CollectionChanged += OnSectionsChanged;
    }

    private void RehookPages()
    {
        if (_pages is not null) _pages.CollectionChanged -= OnPagesChanged;
        _pages = Vm?.SelectedSection?.Pages as System.Collections.Specialized.INotifyCollectionChanged;
        if (_pages is not null) _pages.CollectionChanged += OnPagesChanged;
    }

    private void OnNotebooksChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    { RiseAdded(e, HomeCards); RiseAdded(e, NotebooksList); }
    private void OnSectionsChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    { RiseAdded(e, SectionsList); }
    private void OnPagesChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    { RiseAdded(e, PagesList); }

    /// <summary>Rise newly-added item containers in (ignores Move — that's the drag reflow).</summary>
    private void RiseAdded(System.Collections.Specialized.NotifyCollectionChangedEventArgs e, ItemsControl list)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems is null) return;
        var added = e.NewItems.Cast<object>().ToList();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var it in added)
                if (list.ContainerFromItem(it) is Control c) Motion.RiseIn(c, Motion.Base);
        }, DispatcherPriority.Background);
    }

    /// <summary>Collapse an item's container out, then run the actual delete.</summary>
    private void CollapseThenDelete(Control? container, System.Action delete)
    {
        if (container is null) { delete(); return; }
        Motion.CollapseOut(container, Motion.Base, delete);
    }

    /// <summary>Initial rail/pages panel state (no animation); the toggles animate via Motion.Reveal.</summary>
    private void ApplyPanels()
    {
        if (Vm is not { } vm) return;
        RailPanel.Width = vm.IsRailVisible ? 64 : 0; RailPanel.Opacity = vm.IsRailVisible ? 1 : 0;
        PagesPanel.Width = vm.IsPagesVisible ? 224 : 0; PagesPanel.Opacity = vm.IsPagesVisible ? 1 : 0;
    }

    /// <summary>Initial home/editor surface state (no animation); the switch zooms in code (their
    /// IsVisible bindings were removed so we control the fade timing). BOTH stay laid out — the hidden
    /// one just sits at opacity 0 — so opening a notebook doesn't pay a first-time editor layout that
    /// stalls the click.</summary>
    private void ApplyHomeSurface()
    {
        bool home = Vm?.IsHomeVisible ?? true;
        HomeHost.IsVisible = true; HomeHost.Opacity = home ? 1 : 0; HomeHost.IsHitTestVisible = home;
        BodyDock.IsVisible = true; BodyDock.Opacity = home ? 0 : 1; BodyDock.IsHitTestVisible = !home;
    }

    /// <summary>"Glossy accents": gloss on the recents chips + accent-gradient selected pills.</summary>
    private void ApplyGlossyAccents()
    {
        bool glossy = Vm?.GlossyAccents ?? true;
        RecentList.Classes.Set("glossy", glossy);
        SectionsList.Classes.Set("glossy", glossy);
        PagesList.Classes.Set("glossy", glossy);
        // NOT the rail: its item now stretches full-width, so the glossy accent-gradient selection
        // fill would show as an ugly full-width blue bar. The rail chip shows its own colour + glow.
    }

    /// <summary>Gallery card size pref → the DynamicResource doubles the card template consumes.
    /// The CELL (and the shadow halo bound to the card size) must scale WITH the card — the cell
    /// carries the hover-growth/shadow slack, and a fixed cell makes Large cards overlap.</summary>
    private void ApplyCardSize()
    {
        var (w, h) = (Vm?.CardSize) switch
        {
            "Small" => (156.0, 104.0),
            "Large" => (236.0, 160.0),
            _ => (196.0, 132.0),
        };
        Resources["NbCardWidth"] = w;
        Resources["NbCardHeight"] = h;
        Resources["NbCardCellWidth"] = w + 28;    // same 28/30 slack the Medium layout always had
        Resources["NbCardCellHeight"] = h + 30;
    }

    /// <summary>"Glass tint": white/black veil under all content, tinting whatever the acrylic
    /// backdrop shows through. Hidden only when the theme shows no acrylic anywhere (GlassWindow
    /// false, i.e. solid frame + Full theme) or at ~zero — solid+FullOff themes still show it
    /// through the glass page box.</summary>
    private void ApplyGlassTint()
    {
        if (Vm is not { } vm) return;
        double t = System.Math.Clamp(vm.GlassTint, -1, 1);
        bool on = Services.ThemeManager.Current.GlassWindow && System.Math.Abs(t) > 0.01;
        GlassTintVeil.IsVisible = on;
        if (on) GlassTintVeil.Background =
            new SolidColorBrush(t >= 0 ? Colors.White : Colors.Black, System.Math.Abs(t) * 0.35);
    }

    /// <summary>Push the bullet/number prefs onto the editor statics; optionally rebuild the open
    /// page so existing note boxes re-render with the new furniture.</summary>
    private void ApplyBulletPrefs(bool rebuild)
    {
        if (Vm is not { } vm) return;
        RichTextEditor.BulletColorOverrides.Clear();
        foreach (var style in new[] { "dot", "arrow", "star", "heart", "flower", "spark" })
            if (vm.BulletColorFor(style) is { } hex) RichTextEditor.BulletColorOverrides[style] = hex;
        RichTextEditor.NumBoldDefault = vm.NumBoldDefault;
        RichTextEditor.NumItalicDefault = vm.NumItalicDefault;
        RichTextEditor.NumUnderlineDefault = vm.NumUnderlineDefault;
        RichTextEditor.NumStrikeDefault = vm.NumStrikeDefault;
        if (rebuild) PageCanvas.Document = PageCanvas.Document;
    }

    /// <summary>Push the editor prefs onto the shared statics; optionally rebuild so open note
    /// boxes pick up caret color/width changes immediately.</summary>
    private void ApplyEditorPrefs(bool rebuild)
    {
        if (Vm is not { } vm) return;
        RichTextEditor.CaretColorOverride = Services.ThemePalettes.NormalizeHex(vm.CaretColor);
        RichTextEditor.CaretWidthPref = System.Math.Clamp(vm.CaretWidth, 1, 3);
        RichTextEditor.CaretBlinkPref = vm.CaretBlink;
        RichTextEditor.DefaultHighlightPref = vm.DefaultHighlight;
        RichTextEditor.DateFormatPref = vm.DateFormat;
        RichTextEditor.NewNoteWidthPref = vm.NewNoteWidth;
        RichTextEditor.EditorFontPref = vm.EditorFont;
        RichTextEditor.EditorFontSizePref = vm.EditorFontSize;
        RichTextEditor.LineSpacingScalePref = vm.LineSpacingScale;
        RichTextEditor.ParagraphSpacingScalePref = vm.ParagraphSpacingScale;
        RichTextEditor.IndentScalePref = vm.IndentScale;
        RichTextEditor.SmartListsPref = vm.SmartLists;
        if (rebuild) PageCanvas.Document = PageCanvas.Document;
    }

    /// <summary>Push the motion prefs onto the shared engine (statics — affect every window).</summary>
    private void ApplyMotionPrefs()
    {
        if (Vm is not { } vm) return;
        Motion.Enabled = !vm.ReduceMotion;
        Motion.SpeedScale = vm.MotionSpeed switch { "Calm" => 1.4, "Snappy" => 0.6, _ => 1.0 };
    }

    /// <summary>Place the toolbar per the VM: docked to a side of either the WINDOW body or the PAGE box.</summary>
    private void ApplyToolbarPlacement()
    {
        if (Vm is not { } vm) return;
        var dock = vm.ToolbarPosition switch
        {
            "Left" => Avalonia.Controls.Dock.Left,
            "Right" => Avalonia.Controls.Dock.Right,
            "Bottom" => Avalonia.Controls.Dock.Bottom,
            _ => Avalonia.Controls.Dock.Top,
        };
        var host = vm.ToolbarScope == "Page" ? PageDock : BodyDock;
        if (!ReferenceEquals(Toolbar.Parent, host))
        {
            (Toolbar.Parent as Panel)?.Children.Remove(Toolbar);
            host.Children.Insert(0, Toolbar);      // DockPanel: last child fills, so docked items go first
        }
        DockPanel.SetDock(Toolbar, dock);
        Toolbar.SetPlacement(dock, vm.ToolbarScope == "Page");
        Motion.FadeIn(Toolbar, Motion.Base);       // fade the toolbar in on (re)placement
    }

    // Debounced autosave: flush dirty page docs after ~0.9s of typing idle.
    private void OnDocsDirtied()
    {
        if (_autosave is null)
        {
            _autosave = new Avalonia.Threading.DispatcherTimer();
            _autosave.Tick += (_, _) => { _autosave!.Stop(); Vm?.FlushDirtyDocs(); };
        }
        _autosave.Stop();
        _autosave.Interval = System.TimeSpan.FromMilliseconds(
            System.Math.Clamp(Vm?.AutosaveMs ?? 900, 100, 5000));
        _autosave.Start();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Selection changed (or the notebook/section swapped the list's ItemsSource) — re-assert the
        // section/page ListBox selection so the :selected styling (scale + glow) shows by default.
        if (e.PropertyName is nameof(MainViewModel.SelectedNotebook)
            or nameof(MainViewModel.SelectedSection) or nameof(MainViewModel.SelectedPage))
            ReassertListSelection();

        // Re-point the add-animation hooks at the current section/page collections.
        if (e.PropertyName == nameof(MainViewModel.SelectedNotebook)) RehookSections();
        if (e.PropertyName == nameof(MainViewModel.SelectedSection)) RehookPages();

        // Section switch: the repopulated pages list rises in instead of popping.
        if (e.PropertyName == nameof(MainViewModel.SelectedSection))
            Dispatcher.UIThread.Post(() => Motion.RiseIn(PagesList, Motion.Base), DispatcherPriority.Background);

        if (e.PropertyName == nameof(MainViewModel.SelectedPage))
        {
            // Fade the current page out, THEN swap + rise the new one in (SyncEditorDocument does the swap).
            if (PageCanvas.Document is not null) Motion.FadeOut(PageDock, Motion.Fast, SyncEditorDocument);
            else SyncEditorDocument();
        }
        else if (e.PropertyName is nameof(MainViewModel.ToolbarPosition) or nameof(MainViewModel.ToolbarScope))
            ApplyToolbarPlacement();
        else if (e.PropertyName is nameof(MainViewModel.ResizablePages) or nameof(MainViewModel.DeletedHistory)
                 or nameof(MainViewModel.PageGrid) or nameof(MainViewModel.GridSnap))
            ApplyCanvasPrefs();
        else if (e.PropertyName == nameof(MainViewModel.FlatCovers))
            ApplyFlatCovers();
        else if (e.PropertyName == nameof(MainViewModel.GlossyAccents))
            ApplyGlossyAccents();
        else if (e.PropertyName == nameof(MainViewModel.CardSize))
            ApplyCardSize();
        else if (e.PropertyName == nameof(MainViewModel.GlassTint))
            ApplyGlassTint();
        else if (e.PropertyName is nameof(MainViewModel.ReduceMotion) or nameof(MainViewModel.MotionSpeed))
            ApplyMotionPrefs();
        else if (e.PropertyName is nameof(MainViewModel.BulletPrefsVersion)
                 or nameof(MainViewModel.NumBoldDefault) or nameof(MainViewModel.NumItalicDefault)
                 or nameof(MainViewModel.NumUnderlineDefault) or nameof(MainViewModel.NumStrikeDefault))
            ApplyBulletPrefs(rebuild: true);
        else if (e.PropertyName is nameof(MainViewModel.CaretColor) or nameof(MainViewModel.CaretWidth)
                 or nameof(MainViewModel.CaretBlink) or nameof(MainViewModel.DefaultHighlight)
                 or nameof(MainViewModel.DateFormat) or nameof(MainViewModel.NewNoteWidth)
                 or nameof(MainViewModel.EditorFont) or nameof(MainViewModel.EditorFontSize)
                 or nameof(MainViewModel.LineSpacingScale) or nameof(MainViewModel.ParagraphSpacingScale)
                 or nameof(MainViewModel.IndentScale) or nameof(MainViewModel.SmartLists))
            ApplyEditorPrefs(rebuild: true);
        else if (e.PropertyName is nameof(MainViewModel.ExtendedFonts) or nameof(MainViewModel.FontPrefsVersion))
        {
            if (Vm is { } fvm) Toolbar.SetFontPrefs(fvm.ExtendedFonts, fvm.DisabledFontsList);
        }
        else if (e.PropertyName == nameof(MainViewModel.PalettePrefsVersion))
        {
            if (Vm is { } pvm) Toolbar.SetPalettes(
                pvm.PaletteFor(true, FormatToolbar.BuiltInHighlights),
                pvm.PaletteFor(false, FormatToolbar.BuiltInTextColors));
        }
        else if (e.PropertyName == nameof(MainViewModel.IsRailVisible))
            Motion.Reveal(RailPanel, 64, Vm?.IsRailVisible ?? true);
        else if (e.PropertyName == nameof(MainViewModel.IsPagesVisible))
            Motion.Reveal(PagesPanel, 224, Vm?.IsPagesVisible ?? true);
        else if (e.PropertyName == nameof(MainViewModel.IsHomeVisible))
        {
            if (_rearranging) SetRearranging(false);          // leaving home exits rearrange mode
            bool home = Vm?.IsHomeVisible ?? true;
            if (home && Vm is { } vm)
            {
                // Card subtitles (counts) are converter-computed — re-realize on return home.
                HomeCards.ItemsSource = null;
                HomeCards.ItemsSource = vm.Notebooks;
            }
            // Zoom: the incoming surface grows in from 0.95 while the outgoing one shrinks to 0.95 and
            // fades — reads as "zoom into the notebook" / "shrink back to the gallery". Scales kept <=1
            // so a full-screen surface never overflows + clips at the window edges. Quick (240ms).
            var show = home ? (Control)HomeHost : BodyDock;
            var hide = home ? (Control)BodyDock : HomeHost;
            const double small = 0.95;
            const int ms = 170;
            show.RenderTransformOrigin = hide.RenderTransformOrigin = Avalonia.RelativePoint.Center;
            hide.IsHitTestVisible = false;                                 // both stay laid out (opacity only)
            Motion.Tween(hide, 0, 0, 1, 0, 0, small, ms, Motion.EaseOutSoft, 1, 0);
            show.IsHitTestVisible = true;
            Motion.Tween(show, 0, 0, small, 0, 0, 1, ms + 40, Motion.EaseOutSoft, 0, 1);   // +40ms tail = soft landing
        }
        else if (e.PropertyName is nameof(MainViewModel.Theme)
                 or nameof(MainViewModel.FullTheme) or nameof(MainViewModel.PaperLight)
                 or nameof(MainViewModel.CustomAccent) or nameof(MainViewModel.AccentFollowsNotebook))
        {
            // Note containers read their paper-region brushes at construction — rebuild them.
            PageCanvas.Document = PageCanvas.Document;
            if (TrashPanel.IsVisible) RefreshTrashPanel();
            if (Content is Control root) Motion.FadeIn(root, Motion.Fast);   // soft cross to the new theme
            // Posted: MainWindow's own PropertyChanged handler updates ThemeManager.Current on this
            // same VM event, and subscription order between the two views isn't guaranteed — read
            // GlassWindow only after that handler has had a chance to run.
            Dispatcher.UIThread.Post(ApplyGlassTint, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Avalonia's ListBox drops its bound selection when its nested ItemsSource swaps
    /// (SelectedNotebook.Sections / SelectedSection.Pages): it coerces SelectedIndex to -1 and the
    /// unchanged bound value never re-pushes, so the item never reads as :selected. Re-assert it once
    /// the new containers have materialized so the default section/page light up without a click.
    /// </summary>
    private void ReassertListSelection()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Vm is not { } vm) return;
            if (vm.SelectedSection is { } sec && !ReferenceEquals(SectionsList.SelectedItem, sec))
                SectionsList.SelectedItem = sec;
            if (vm.SelectedPage is { } pg && !ReferenceEquals(PagesList.SelectedItem, pg))
                PagesList.SelectedItem = pg;
            UpdateSelectionScale();     // drive the "lit" scale (the style path can't move RenderTransform)
        }, DispatcherPriority.Background);
    }

    private void SyncEditorDocument()
    {
        Vm?.FlushDirtyDocs();                      // the page being left saves immediately
        PageCanvas.Document = Vm?.SelectedPage is { } page ? Vm.DocumentFor(page) : null;
        if (PageCanvas.Document is null) { TrashPanel.IsVisible = false; return; }
        if (TrashPanel.IsVisible) RefreshTrashPanel();
        // Page switch: title + canvas rise in instead of the content popping.
        Dispatcher.UIThread.Post(() => Motion.RiseIn(PageDock, Motion.Base), DispatcherPriority.Background);
    }

    // ---- deleted-containers history panel ----

    private void RefreshTrashPanel()
    {
        TrashList.Children.Clear();
        if (PageCanvas.Document is not { } doc) return;
        if (doc.Trash.Count == 0)
        {
            TrashList.Children.Add(new TextBlock
            {
                Text = "Nothing here yet.", FontSize = 12,
                Foreground = (IBrush)this.FindResource("TextMutedBrush")!,
            });
            return;
        }
        foreach (var box in doc.Trash.ToList())
            TrashList.Children.Add(BuildTrashChip(box));
    }

    private Control BuildTrashChip(NoteBox box)
    {
        var preview = box.Doc.GetText().Replace('\n', ' ').Trim();
        if (preview.Length == 0) preview = "(empty container)";
        else if (preview.Length > 64) preview = preview[..64] + "…";

        var text = new TextBlock
        {
            Text = preview, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var restore = new Button
        {
            Theme = (ControlTheme)this.FindResource("IconButton")!,
            Width = 24, Height = 24, FontSize = 12, Content = "",
            FontFamily = (FontFamily)this.FindResource("IconFont")!,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(restore, "Put back where it was");
        restore.Click += (_, _) => PageCanvas.RestoreBox(box);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        Grid.SetColumn(restore, 1);
        grid.Children.Add(restore);

        var chip = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14FFFFFF")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand), Child = grid,
        };
        chip.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(NoteCanvas.TrashFormat, box));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        };
        return chip;
    }

    private void BeginRenameSection(Section? sec)
    {
        if (sec is null) return;
        if (Vm is { } vm) vm.SelectedSection = sec;
        sec.IsEditing = true;
        // The rename TextBox becomes visible this frame; focus + select it once it's realized.
        Dispatcher.UIThread.Post(() =>
        {
            var box = SectionsList.ContainerFromItem(sec)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box is not null) Motion.FadeIn(box, Motion.Fast);
            box?.Focus();
            box?.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void CommitSection(Section sec)
    {
        if (!sec.IsEditing) return;
        sec.IsEditing = false;
        Vm?.Save();
    }

    private void OnSectionEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: Section sec }) CommitSection(sec);
    }

    private void OnSectionEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: Section sec }) return;
        if (e.Key is Key.Enter or Key.Escape) { CommitSection(sec); SectionsList.Focus(); e.Handled = true; }
    }

    // ---- right-click delete menus (with confirm) for notebooks, sections, pages ----

    private void OnSectionsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Section sec) return;
        if (Vm is { } vm) vm.SelectedSection = sec;
        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginRenameSection(sec);
        var delete = new MenuItem { Header = "Delete section" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this section?",
            $"“{Label(sec.Name)}” and all its pages will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeleteSection ?? true,
            () => CollapseThenDelete(SectionsList.ContainerFromItem(sec) as Control, () => Vm?.DeleteSectionCommand.Execute(sec)));
        OpenMenu(e, rename, delete);
    }

    private void OnNotebooksContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Notebook nb) return;
        if (Vm is { } vm) vm.SelectedNotebook = nb;
        var delete = new MenuItem { Header = "Delete notebook" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this notebook?",
            $"“{Label(nb.Name)}” and all its sections and pages will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeleteNotebook ?? true,
            () => CollapseThenDelete(NotebooksList.ContainerFromItem(nb) as Control, () => Vm?.DeleteNotebookCommand.Execute(nb)));
        OpenMenu(e, delete);
    }

    private void OnNotebookNameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm?.SelectedNotebook is not { } nb) return;
        var delete = new MenuItem { Header = "Delete notebook" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this notebook?",
            $"“{Label(nb.Name)}” and all its sections and pages will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeleteNotebook ?? true,
            () => CollapseThenDelete(NotebooksList.ContainerFromItem(nb) as Control, () => Vm?.DeleteNotebookCommand.Execute(nb)));
        OpenMenu(e, delete);
    }

    private void OnPagesContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Models.Page pg) return;
        if (Vm is { } vm) vm.SelectedPage = pg;
        var delete = new MenuItem { Header = "Delete page" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this page?",
            $"“{Label(pg.Title)}” will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeletePage ?? true,
            () => CollapseThenDelete(PagesList.ContainerFromItem(pg) as Control, () => Vm?.DeletePageCommand.Execute(pg)));
        OpenMenu(e, delete);
    }

    // ---- homepage gallery ----

    private void OnHomeCardTapped(object? sender, TappedEventArgs e)
    {
        if (_rearranging) return;
        if ((e.Source as StyledElement)?.DataContext is Notebook nb)
            Vm?.OpenNotebookCommand.Execute(nb);
    }

    private void OnHomeCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_rearranging) return;
        if ((e.Source as StyledElement)?.DataContext is not Notebook nb) return;

        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => Vm?.OpenNotebookCommand.Execute(nb);

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) =>
        {
            Vm?.OpenNotebookCommand.Execute(nb);
            Dispatcher.UIThread.Post(() => { NotebookName.Focus(); NotebookName.SelectAll(); },
                                     DispatcherPriority.Background);
        };

        // Color → 9 hue families, each expanding into its 5 shades.
        var color = new MenuItem { Header = "Color" };
        foreach (var (family, shades) in MainViewModel.NotebookPalette)
        {
            var fam = new MenuItem { Header = family, Icon = Swatch(shades[2].Hex) };
            foreach (var (shadeName, hex) in shades)
            {
                var item = new MenuItem { Header = shadeName, Icon = Swatch(hex) };
                string chosen = hex;
                item.Click += (_, _) => Vm?.SetNotebookColor(nb, chosen);
                fam.Items.Add(item);
            }
            color.Items.Add(fam);
        }

        var moveLeft = new MenuItem { Header = "Move left" };
        moveLeft.Click += (_, _) => Vm?.MoveNotebook(nb, -1);
        var moveRight = new MenuItem { Header = "Move right" };
        moveRight.Click += (_, _) => Vm?.MoveNotebook(nb, +1);

        var cover = new MenuItem { Header = "Choose cover image…" };
        cover.Click += async (_, _) => await PickCover(nb);

        var delete = new MenuItem { Header = "Delete notebook" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this notebook?",
            $"“{Label(nb.Name)}” and all its sections and pages will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeleteNotebook ?? true,
            () => CollapseThenDelete(HomeCards.ContainerFromItem(nb) as Control, () => Vm?.DeleteNotebookCommand.Execute(nb)));

        if (nb.CoverPath is not null)
        {
            var removeCover = new MenuItem { Header = "Remove cover image" };
            removeCover.Click += (_, _) => Vm?.ClearNotebookCover(nb);
            OpenMenu(e, open, rename, moveLeft, moveRight, color, cover, removeCover, delete);
        }
        else
        {
            OpenMenu(e, open, rename, moveLeft, moveRight, color, cover, delete);
        }
    }

    private async System.Threading.Tasks.Task PickCover(Notebook nb)
    {
        if (Vm is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a cover image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
                },
            },
        });
        if (files.Count == 0) return;
        if (files[0].TryGetLocalPath() is not { } path || Window is not { } w) return;

        // Let the user frame the part of the image the card shows (fixed card aspect).
        var cropped = await CoverCropDialog.Show(w, path);
        if (cropped is null) return;
        try { Vm.SetNotebookCover(nb, cropped); }
        finally { try { System.IO.File.Delete(cropped); } catch { } }
    }

    private static Border Swatch(string hex)
    {
        var c = Color.Parse(hex);
        return new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(c),
            BorderBrush = new SolidColorBrush(Converters.Shade(c, -0.38)),
            BorderThickness = new Thickness(1),
        };
    }

    private static string Label(string? s) => string.IsNullOrWhiteSpace(s) ? "Untitled" : s;

    private static void OpenMenu(ContextRequestedEventArgs e, params MenuItem[] items)
    {
        var menu = new ContextMenu();
        foreach (var i in items) menu.Items.Add(i);
        if (e.Source is Control c) { menu.Open(c); e.Handled = true; }
    }

    private async void ConfirmThenDelete(string title, string message, bool ask, System.Action delete)
    {
        if (!ask) { delete(); return; }
        if (Window is not { } w) return;
        if (await ConfirmDialog.Show(w, title, message)) delete();
    }
}
