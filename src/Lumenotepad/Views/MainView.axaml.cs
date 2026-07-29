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

        // macOS keeps its NATIVE window shell (traffic lights top-left, real fullscreen), so our custom
        // caption buttons are redundant — hide them — and shift the logo/title right so it clears the
        // traffic lights that now float over our extended title-bar band.
        if (!System.OperatingSystem.IsWindows())
        {
            CaptionButtons.IsVisible = false;
            TitleLeft.Margin = new Thickness(78, 0, 0, 0);
        }

        // Header rename fields (notebook, page) commit on blur / Enter.
        foreach (var box in new[] { NotebookName, PageTitle })
        {
            box.LostFocus += (_, _) => Vm?.Save();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Vm?.Save(); ((Control?)s)?.Focus(); } };
        }

        // The canvas edits the selected page's document.
        DataContextChanged += (_, _) => HookVm();

        // The toolbar follows whichever note container was focused last; dock menu re-docks it (persisted).
        PageCanvas.ActiveEditorChanged += ed => { if (ed is not null) Toolbar.Target = ed; RefreshMindmapRings(); };

        BuildMindmapBar();

        // A freshly downloaded font (font installer) should appear in the toolbar menu right away.
        Services.AppFonts.InstalledChanged += () =>
        {
            if (Vm is { } fvm) Toolbar.SetFontPrefs(fvm.ExtendedFonts, fvm.DisabledFontsList);
        };

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
        PageCanvas.OpenPdfRequested = path =>   // PDF attachments open in the in-app viewer/annotator
        {
            var viewer = new PdfViewerWindow(path, Vm?.DoubleClickCreate ?? false);
            if (Window is { } w) viewer.Show(w); else viewer.Show();
        };

        // The section/page "+" buttons open an Add / Rearrange / Open-as-PDF menu.
        SectionsAddBtn.Click += (_, _) => OpenSectionsMenu(SectionsAddBtn);
        SideSectionsAddBtn.Click += (_, _) => OpenSectionsMenu(SideSectionsAddBtn);
        PagesAddBtn.Click += (_, _) => OpenPagesMenu(PagesAddBtn);
        HistoryBtn.Click += (_, _) =>
        {
            TrashPanel.IsVisible = !TrashPanel.IsVisible;
            if (TrashPanel.IsVisible) { TagsPanel.IsVisible = false; RefreshTrashPanel(); }
        };
        // Tagged notes: the two side panels share the page's right edge — opening one closes the other.
        TagsBtn.Click += (_, _) =>
        {
            TagsPanel.IsVisible = !TagsPanel.IsVisible;
            if (TagsPanel.IsVisible) { TrashPanel.IsVisible = false; RefreshTagsPanel(); }
        };
        Toolbar.DockRequested += pos => { if (Vm is { } vm) vm.ToolbarPosition = pos; };
        Toolbar.ScopeRequested += scope => { if (Vm is { } vm) vm.ToolbarScope = scope; };
        Toolbar.CustomizeRequested += () => { if (Vm?.SelectedNotebook is { } nb) OpenNotebookWizard(nb); };
        Toolbar.InsertImageRequested += async () => await InsertImageAsync();
        Toolbar.InsertDividerRequested += InsertDivider;
        Toolbar.InsertAttachmentRequested += async () => await InsertAttachmentAsync();
        Toolbar.InsertTableRequested += InsertTable;
        Toolbar.InsertPdfRequested += async () => await InsertPdfAsync();

        // Section/page rename: double-click, the rename button, or right-click → Rename — all open
        // the zoomed rename overlay (the background blurs until the name is saved).
        RenameSectionBtn.Click += (_, _) => BeginRenameSection(Vm?.SelectedSection);
        SectionsList.DoubleTapped += (_, e) => BeginRenameSection((e.Source as StyledElement)?.DataContext as Section);
        // Sections sidebar (preference): same rename / context behaviour as the in-panel list.
        SideRenameSectionBtn.Click += (_, _) => BeginRenameSection(Vm?.SelectedSection);
        SectionsSidebarList.DoubleTapped += (_, e) => BeginRenameSection((e.Source as StyledElement)?.DataContext as Section);
        SectionsSidebarList.ContextRequested += OnSectionsContextRequested;
        PagesList.DoubleTapped += (_, e) => BeginRenamePage((e.Source as StyledElement)?.DataContext as Models.Page);
        RenameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { EndRenameOverlay(commit: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndRenameOverlay(commit: false); e.Handled = true; }
        };
        RenameVeil.PointerPressed += (_, _) => EndRenameOverlay(commit: true);

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

        // "New notebook" opens the wizard (M9) — the instant-create command remains for tests only.
        NewNotebookBtn.Click += (_, _) => OpenNotebookWizard();
        RailAddBtn.Click += (_, _) => OpenNotebookWizard();

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

        // Recycled containers keep whatever LOCAL Opacity/RenderTransform a Motion tween left on
        // them (a deleted row collapsed to opacity 0, a cancelled rise…) — the next item presented
        // in that container then renders INVISIBLE. Reset the visual state every time a container
        // is (re)prepared; legit add-animations start AFTER this (posted at Background priority).
        foreach (var list in new ItemsControl[] { NotebooksList, SectionsList, SectionsSidebarList, PagesList, HomeCards })
            list.ContainerPrepared += (_, e) =>
            {
                Motion.Stop(e.Container);
                e.Container.ClearValue(Visual.OpacityProperty);
                e.Container.ClearValue(Visual.RenderTransformProperty);
            };

        // Keep the canvas plate's punched hole aligned with the page box (margin 14, radius 14).
        CanvasPlate.SizeChanged += (_, _) => UpdateCanvasPlateClip();

        // Guides + starter templates anchor to the visible page area (in CANVAS coordinates, so
        // the zoom divides out).
        CanvasScroll.SizeChanged += (_, _) => PushCanvasViewport();
        // The page glides like every other pane (SmoothScroll leaves Ctrl+wheel to the zoom below).
        SmoothScroll.Attach(CanvasScroll);
        // Ctrl+wheel canvas zoom (M8 Part 6): 50%–200% in ×1.1 notches, Ctrl+0 resets. Shift+wheel pans the
        // canvas horizontally. Tunnel so the ScrollViewer never also scrolls vertically on the same notch.
        CanvasScroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SetCanvasZoom(_canvasZoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
                e.Handled = true;
            }
            // Shift+wheel horizontal panning now runs through SmoothScroll (attached above) so it
            // eases like every other scroll instead of jumping.
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if ((e.Key is Key.D0 or Key.NumPad0) && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { SetCanvasZoom(1.0); e.Handled = true; }
        }, RoutingStrategies.Tunnel);

        // Drag the pages panel's right edge to resize it (clamped); persists via the VM setting.
        bool panelDragging = false; double panelStartX = 0, panelStartW = 0;
        PagesResizeGrip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(PagesResizeGrip).Properties.IsLeftButtonPressed) return;
            panelDragging = true;
            panelStartX = e.GetPosition(this).X;
            panelStartW = PagesPanel.Bounds.Width;
            e.Pointer.Capture(PagesResizeGrip);
            e.Handled = true;
        };
        PagesResizeGrip.PointerMoved += (_, e) =>
        {
            if (!panelDragging) return;
            PagesPanel.Width = System.Math.Clamp(panelStartW + (e.GetPosition(this).X - panelStartX), 180, 340);
        };
        PagesResizeGrip.PointerReleased += (_, e) =>
        {
            if (!panelDragging) return;
            panelDragging = false;
            e.Pointer.Capture(null);
            if (Vm is { } pvm) pvm.PagesPanelWidth = PagesPanel.Width;   // persist once, on release
        };
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
        // Always carry opacity to 1: one tween per element, so this CANCELS any in-flight rise-in
        // on the same container — without opacity params the kill strands a just-added section/page
        // at opacity ~0 (invisible until its container is re-prepared by leaving the notebook).
        if (cur is not null) Motion.Tween(cur, 0, 0, ScaleNow(cur), 0, 0, 1.0, 140,
                                          fromOpacity: cur.Opacity, toOpacity: 1);
        cur = next;
        if (next is not null) Motion.Tween(next, 0, 0, ScaleNow(next), 0, 0, scale, 170,
                                           fromOpacity: next.Opacity, toOpacity: 1);
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
        _ghostTween = Motion.Clock(ms, p =>
        {
            double ep = Motion.EaseOut(p);
            Canvas.SetLeft(ghost, fx + (target.X - fx) * ep);
            Canvas.SetTop(ghost, fy + (target.Y - fy) * ep);
        }, done: () => { _ghostTween = null; onDone(); });
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
        MenuFx.AttachFlyout(menu);
        menu.ShowAt(SortBtn);
    }

    private void UpdateCanvasPlateClip()
    {
        var b = CanvasPlate.Bounds;
        if (b.Width <= 30 || b.Height <= 30) { CanvasPlate.Clip = null; return; }
        // The hole is inset 1.5px INSIDE the page box's border (margin 14, radius 14, 1px stroke):
        // a hole cut exactly at the border line leaves an anti-aliased seam where the acrylic
        // backdrop (the wallpaper) peeks between plate and border — a colored halo, worst at the
        // rounded corners (owner report). Tucking the plate edge under the border hides the seam.
        // Hole radius tracks the (roundness-scaled) page-box radius minus the 1.5px tuck-under.
        double holeR = System.Math.Max(1, System.Math.Round(14 * Services.ThemeManager.Roundness) - 1.5);
        var hole = new RectangleGeometry(new Rect(15.5, 15.5, b.Width - 31, b.Height - 31))
        {
            RadiusX = holeR, RadiusY = holeR,
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

    private void OpenNotebookWizard(Models.Notebook? edit = null)
    {
        if (Vm is not { } vm || Window is not { } w) return;
        new NotebookWizardWindow(vm, edit).ShowDialog(w);
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
            ApplyPdfPage();     // show the embedded viewer if the startup page is a PDF
            ApplyToolbarPlacement();
            ApplyCanvasPrefs();
            ApplyPaperTint();
            ApplyGlossyAccents();
            ApplyCardSize();
            ApplyPanels();
            ApplySectionsSidebar();
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
        PageCanvas.AlwaysShowBorders = vm.AlwaysShowBorders;
        PageCanvas.HistoryEnabled = vm.DeletedHistory;
        PageCanvas.TidyLayout = vm.MindmapTidyLayout switch
        {
            "Hybrid" => Editor.MindmapLayout.Hybrid,
            "TopDown" => Editor.MindmapLayout.TopDown,
            _ => Editor.MindmapLayout.Radial,
        };
        ApplyPageStyles();             // per-page effective styles (falls back to the global grid pref)
        PageCanvas.SnapToGrid = vm.GridSnap;
        PageCanvas.CreateOnDoubleClick = vm.DoubleClickCreate;
        PdfViewer.RoundedPagePref = vm.RoundedPdfCorners;
        PagePdfViewer.RefreshChrome();                 // re-round (or square) an already-open PDF
        if (!vm.DeletedHistory) TrashPanel.IsVisible = false;
        ApplyFlatCovers();
    }

    /// <summary>Resolve the selected page's effective grid + page styles (page ?? notebook ?? global
    /// pref) and push them onto the canvas guide layer.</summary>
    private void ApplyPageStyles()
    {
        if (Vm is not { } vm) return;
        string grid = vm.SelectedPage is { } pg && vm.SelectedNotebook is { } nb
            ? PageStyles.EffectiveGrid(pg, nb, vm.PageGrid)
            : PageStyles.MapGlobalGrid(vm.PageGrid);
        var (style, mode) = vm.SelectedPage is { } p && vm.SelectedNotebook is { } n
            ? PageStyles.EffectiveStyle(p, n)
            : (PageStyles.Freeform, 0);
        PageCanvas.SetStyles(grid, style, mode);
        ApplyMindmapBar();
    }

    // ---- mind-map toolbar (shown only on Mindmap pages) ----

    /// <summary>Show/hide the mind-map toolbar with the page style, and reflect the active bubble's colour.</summary>
    private void ApplyMindmapBar()
    {
        bool on = PageCanvas.IsMindmap && !PagePdfViewer.IsVisible;
        MindmapBar.IsVisible = on;
        if (on) RefreshMindmapRings();
    }

    /// <summary>Build the mind-map toolbar once in the app's icon-toolbar language: icon buttons,
    /// group separators, and flyout pickers (colour / size / options) — matching the format toolbar.</summary>
    private void BuildMindmapBar()
    {
        MindmapBarContent.Children.Clear();
        var iconFont = (FontFamily)Application.Current!.FindResource("IconFont")!;
        var iconTheme = (ControlTheme)Application.Current!.FindResource("IconButton")!;

        Button IconBtn(string glyph, string tip, double fs = 14)
        {
            var b = new Button
            {
                Theme = iconTheme, Width = 30, Height = 30, FontSize = fs,
                FontFamily = iconFont, Content = glyph,
            };
            ToolTip.SetTip(b, tip);
            return b;
        }
        void Sep() => MindmapBarContent.Children.Add(new Border
        {
            Width = 1, Height = 18, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = this.FindResource("FrameBorderBrush") as IBrush, Opacity = 0.7,
        });

        var addBubble = IconBtn("", "Add bubble — pick a type");
        addBubble.Flyout = BuildAddBubbleFlyout();
        MindmapBarContent.Children.Add(addBubble);

        var addConnected = IconBtn("", "Add a bubble linked to the selected one");
        addConnected.Click += (_, _) =>
        {
            if (PageCanvas.AddConnectedBubble()) return;
            var (x, y) = CanvasCentre();
            PageCanvas.AddBubble(x, y);
        };
        MindmapBarContent.Children.Add(addConnected);

        Sep();

        // Colour: an icon button carrying a swatch underline of the current colour, opening a palette.
        var colourGlyph = new TextBlock
        {
            Text = "", FontFamily = iconFont, FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var colourBtn = new Button
        {
            Theme = iconTheme, Width = 30, Height = 30,
            Content = colourGlyph,
            Flyout = BuildColourFlyout(),
        };
        ToolTip.SetTip(colourBtn, "Bubble colour");
        MindmapBarContent.Children.Add(colourBtn);

        var sizeBtn = new Button
        {
            Theme = iconTheme, Width = 30, Height = 30, FontSize = 13, Content = "Aa",
            Flyout = BuildSizeFlyout(),
        };
        ToolTip.SetTip(sizeBtn, "New bubble size");
        MindmapBarContent.Children.Add(sizeBtn);

        // Paint bucket (fill tool): pick a colour, then click bubbles to recolour them; click again to stop.
        var paintBtn = IconBtn("", "Paint bucket — pick a colour, then click bubbles to fill them", 15);
        _paintBtn = paintBtn;
        var paintFlyout = BuildPaintFlyout();
        paintBtn.Click += (_, _) =>
        {
            if (PageCanvas.MindmapPaintActive) { PageCanvas.MindmapPaintActive = false; RefreshPaintButton(); }
            else paintFlyout.ShowAt(paintBtn);
        };
        MindmapBarContent.Children.Add(paintBtn);
        RefreshPaintButton();

        Sep();

        var center = IconBtn("", "Frame all bubbles", 15);
        center.Click += (_, _) => CentreMap();
        MindmapBarContent.Children.Add(center);

        var tidy = IconBtn("", "Tidy up — arrange the map around the selected (or hub) bubble", 15);
        tidy.Click += (_, _) => PageCanvas.TidyMindmap();
        MindmapBarContent.Children.Add(tidy);

        var options = IconBtn("", "Mind-map options", 16);
        options.Flyout = BuildOptionsFlyout();
        MindmapBarContent.Children.Add(options);

        RefreshMindmapRings();
    }

    /// <summary>The canvas point at the centre of the current viewport (for "add here" actions).</summary>
    private (double X, double Y) CanvasCentre()
    {
        double x = (CanvasScroll.Offset.X + CanvasScroll.Bounds.Width / 2) / _canvasZoom;
        double y = (CanvasScroll.Offset.Y + CanvasScroll.Bounds.Height / 2) / _canvasZoom;
        return (x, y);
    }

    /// <summary>Scroll so the bounding box of every bubble is centred in the viewport.</summary>
    private void CentreMap()
    {
        var bb = PageCanvas.ContentBounds();
        if (bb.Width <= 0) return;
        double cx = bb.X + bb.Width / 2, cy = bb.Y + bb.Height / 2;
        double ox = cx * _canvasZoom - CanvasScroll.Bounds.Width / 2;
        double oy = cy * _canvasZoom - CanvasScroll.Bounds.Height / 2;
        CanvasScroll.Offset = new Vector(System.Math.Max(0, ox), System.Math.Max(0, oy));
    }

    /// <summary>The palette flyout: a row of shades per family plus grayscale, and a "No colour" reset.</summary>
    /// <summary>The "+" chooser: pick which bubble family to drop — a Title pill, an Information squircle,
    /// or a Callout. Each row shows a little shape preview so the choice reads at a glance.</summary>
    private Flyout BuildAddBubbleFlyout()
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(6), MinWidth = 224 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        MenuFx.AttachFlyout(flyout);

        var accent = this.FindResource("AccentBrush") as IBrush ?? Brushes.White;

        Border Preview(BubbleKind kind)
        {
            var b = new Border
            {
                Width = 34, Height = 22, VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#33FFFFFF")), BorderBrush = accent,
            };
            switch (kind)
            {
                case BubbleKind.Title:
                    b.CornerRadius = new CornerRadius(999); b.BorderThickness = new Thickness(2); break;
                case BubbleKind.Info:
                    b.CornerRadius = new CornerRadius(7); b.BorderThickness = new Thickness(1.6); break;
                default:   // Callout: thick left stripe
                    b.CornerRadius = new CornerRadius(3); b.BorderThickness = new Thickness(5, 1.2, 1.2, 1.2); break;
            }
            return b;
        }

        void Row(BubbleKind kind, string name, string desc)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = desc, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(Preview(kind));
            content.Children.Add(text);
            var btn = new Button
            {
                Theme = (ControlTheme)Application.Current!.FindResource("LumenButton")!,
                Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6),
            };
            btn.Click += (_, _) => { var (x, y) = CanvasCentre(); PageCanvas.AddBubble(x, y, kind); flyout.Hide(); };
            panel.Children.Add(btn);
        }

        Row(BubbleKind.Title, "Title bubble", "Rounded pill, centred text — topics and headings.");
        Row(BubbleKind.Info, "Information bubble", "Squircle card, left-aligned body text — details.");
        Row(BubbleKind.Callout, "Callout", "Squarer card with a left accent bar — asides and quotes.");
        return flyout;
    }

    private Flyout BuildColourFlyout()
    {
        var panel = new StackPanel { Spacing = 5, Margin = new Thickness(8) };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        MenuFx.AttachFlyout(flyout);

        void Row((string Name, string Hex)[] shades)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            foreach (var (name, hex) in shades)
            {
                var chip = MindmapSwatch(new SolidColorBrush(Color.Parse(hex)), name, 24);
                string pick = "#" + hex.TrimStart('#');
                SwatchHover(chip);
                chip.PointerPressed += (_, _) => { PageCanvas.SetBubbleColor(pick); RefreshMindmapRings(); flyout.Hide(); };
                row.Children.Add(chip);
            }
            panel.Children.Add(row);
        }
        foreach (var (family, shades) in ViewModels.MainViewModel.NotebookPalette) Row(shades);
        Row(ViewModels.MainViewModel.GrayscaleShades);

        var none = new Button
        {
            Theme = (ControlTheme)Application.Current!.FindResource("LumenButton")!,
            Content = "No colour", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 3, 0, 0),
        };
        none.Click += (_, _) => { PageCanvas.SetBubbleColor(null); RefreshMindmapRings(); flyout.Hide(); };
        panel.Children.Add(none);
        return flyout;
    }

    /// <summary>The S/M/L new-bubble-size menu.</summary>
    private MenuFlyout BuildSizeFlyout()
    {
        var mf = new MenuFlyout();
        void Opt(string label, double w)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => PageCanvas.MindmapBubbleWidth = w;
            mf.Items.Add(mi);
        }
        Opt("Small", 140);
        Opt("Medium", 220);
        Opt("Large", 320);
        return mf;
    }

    /// <summary>Toggles for connector style and the diagonal connect ports.</summary>
    private Flyout BuildOptionsFlyout()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(12, 9), MinWidth = 190 };
        Control ToggleRow(string label, bool init, System.Action<bool> onChanged)
        {
            var sw = new ToggleSwitch { IsChecked = init, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            sw.IsCheckedChanged += (_, _) => onChanged(sw.IsChecked == true);
            var lbl = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(sw, 1);
            row.Children.Add(lbl);
            row.Children.Add(sw);
            return row;
        }
        panel.Children.Add(ToggleRow("Straight links", PageCanvas.MindmapStraightLines, v => PageCanvas.MindmapStraightLines = v));
        panel.Children.Add(ToggleRow("Diagonal connect points", PageCanvas.MindmapDiagonalPorts,
            v => { PageCanvas.MindmapDiagonalPorts = v; PageCanvas.RefreshMindmapPorts(); }));
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        MenuFx.AttachFlyout(flyout);
        return flyout;
    }

    /// <summary>Swatch hover: border turns accent (matches the format toolbar's swatch style).</summary>
    private static void SwatchHover(Border b)
    {
        var rest = b.BorderBrush;
        var accent = Application.Current!.FindResource("AccentBrush") as IBrush ?? Brushes.White;
        b.PointerEntered += (_, _) => b.BorderBrush = accent;
        b.PointerExited += (_, _) => b.BorderBrush = rest;
    }

    private Border MindmapSwatch(IBrush bg, string tip, double size = 22)
    {
        bool bare = ReferenceEquals(bg, Brushes.Transparent);
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size < 24 ? 6 : 7),
            Background = bg, BorderThickness = new Thickness(1),
            BorderBrush = bare ? this.FindResource("FrameBorderBrush") as IBrush
                               : new SolidColorBrush(Color.Parse("#33FFFFFF")),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>The paint-bucket colour picker: choosing a colour sets the fill colour and turns the tool on.</summary>
    private Flyout BuildPaintFlyout()
    {
        var panel = new StackPanel { Spacing = 5, Margin = new Thickness(8) };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        MenuFx.AttachFlyout(flyout);
        void Row((string Name, string Hex)[] shades)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            foreach (var (name, hex) in shades)
            {
                var chip = MindmapSwatch(new SolidColorBrush(Color.Parse(hex)), name, 24);
                string pick = "#" + hex.TrimStart('#');
                SwatchHover(chip);
                chip.PointerPressed += (_, _) =>
                {
                    PageCanvas.MindmapPaintColor = pick;
                    PageCanvas.MindmapPaintActive = true;
                    flyout.Hide();
                    RefreshPaintButton();
                };
                row.Children.Add(chip);
            }
            panel.Children.Add(row);
        }
        foreach (var (family, shades) in ViewModels.MainViewModel.NotebookPalette) Row(shades);
        Row(ViewModels.MainViewModel.GrayscaleShades);
        return flyout;
    }

    /// <summary>Accent-light the paint-bucket button while the fill tool is active.</summary>
    private void RefreshPaintButton()
    {
        if (_paintBtn is null) return;
        _paintBtn.Foreground = PageCanvas.MindmapPaintActive
            ? (this.FindResource("AccentBrush") as IBrush ?? Brushes.White)
            : (this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White);
    }

    /// <summary>Kept as a no-op hook (the old colour-dot indicator was removed).</summary>
    private void RefreshMindmapRings() { }

    /// <summary>Temporary Part-1 entry point (the Part-4 Page dialog supersedes it): set the style,
    /// refresh the guides, and offer the starter containers — additive, never clears content.</summary>
    /// <summary>Per-notebook paper tint: the selected notebook's PaperTint hex as a translucent
    /// veil (fixed alpha keeps text readable on both light and dark paper).</summary>
    private void ApplyPaperTint()
    {
        var hex = Services.ThemePalettes.NormalizeHex(Vm?.SelectedNotebook?.PaperTint);
        PaperTintVeil.IsVisible = hex is not null;
        if (hex is not null) PaperTintVeil.Background = new SolidColorBrush(Color.Parse(hex), 0.22);
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

    /// <summary>Collapse an item's container out, then run the actual delete. The container may be
    /// RECYCLED for another item afterwards — restore its visual state once the delete has run.</summary>
    private void CollapseThenDelete(Control? container, System.Action delete)
    {
        if (container is null) { delete(); return; }
        Motion.CollapseOut(container, Motion.Base, () =>
        {
            delete();
            container.ClearValue(Visual.OpacityProperty);
            container.ClearValue(Visual.RenderTransformProperty);
        });
    }

    /// <summary>Initial rail/pages panel state (no animation); the toggles animate via Motion.Reveal.</summary>
    private void ApplyPanels()
    {
        if (Vm is not { } vm) return;
        RailPanel.Width = vm.IsRailVisible ? 64 : 0; RailPanel.Opacity = vm.IsRailVisible ? 1 : 0;
        PagesPanel.Width = vm.IsPagesVisible ? vm.PagesPanelWidth : 0; PagesPanel.Opacity = vm.IsPagesVisible ? 1 : 0;
    }

    /// <summary>"Sections in their own sidebar" preference: when on, the sections list moves out of
    /// the pages panel into a dedicated column, and the pages panel's inline sections header + list
    /// hide (SelectedSection binding still drives both — they share the VM). Toggling slides the
    /// column open/closed (width tween — RenderTransform styles are dead in this build); the initial
    /// apply on startup snaps so launch doesn't play a slide.</summary>
    private void ApplySectionsSidebar(bool animate = false)
    {
        if (Vm?.SingleMode == true)
        {
            // Single mode: no sections anywhere — the notebook is just pages.
            SectionsHeader.IsVisible = false;
            SectionsList.IsVisible = false;
            SectionsSidebar.IsVisible = false;
            SectionsSidebar.Width = 0; SectionsSidebar.Opacity = 0;
            return;
        }
        bool side = Vm?.SectionsSidebar ?? false;
        SectionsHeader.IsVisible = !side;
        SectionsList.IsVisible = !side;
        if (!animate)
        {
            SectionsSidebar.IsVisible = side;
            SectionsSidebar.Width = side ? 152 : 0;
            SectionsSidebar.Opacity = side ? 1 : 0;
            return;
        }
        if (side)
        {
            // Coming from hidden: start collapsed so the slide grows from 0, not from a stale width.
            if (!SectionsSidebar.IsVisible) { SectionsSidebar.Width = 0; SectionsSidebar.Opacity = 0; }
            SectionsSidebar.IsVisible = true;
            Motion.Reveal(SectionsSidebar, 152, show: true);
            Motion.RiseIn(SectionsSidebarList);
        }
        else
        {
            Motion.Reveal(SectionsSidebar, 152, show: false);   // width 0 + clip = gone; stays laid out
            Motion.RiseIn(SectionsList);                        // the inline list glides back in its place
        }
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
        SectionsSidebarList.Classes.Set("glossy", glossy);
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
        // macOS frosts far brighter than the DWM acrylic, so the Windows-tuned 35% ceiling left the
        // glass washed out even at -100% (tester: "-100% on the Mac looks like 30-35% on Windows").
        // Give the mac a deeper range so the slider actually reaches a dark glass.
        double max = System.OperatingSystem.IsWindows() ? 0.35 : 0.70;
        if (on) GlassTintVeil.Background =
            new SolidColorBrush(t >= 0 ? Colors.White : Colors.Black, System.Math.Abs(t) * max);
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
        var nb = vm.SelectedNotebook;
        RichTextEditor.EditorFontPref = nb?.DefaultFont ?? vm.EditorFont;
        RichTextEditor.EditorFontSizePref =
            nb is { } n && (n.DefaultFont is not null || System.Math.Abs(n.DefaultFontSize - 15) > 0.01)
                ? n.DefaultFontSize : vm.EditorFontSize;
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
        if (e.PropertyName == nameof(MainViewModel.SelectedNotebook))
        { RehookSections(); ApplyPaperTint(); ApplyEditorPrefs(rebuild: true); TagsPanel.IsVisible = false; }   // chips reference the OLD notebook's pages
        if (e.PropertyName == nameof(MainViewModel.SelectedSection)) RehookPages();

        // Section switch: the repopulated pages list rises in instead of popping.
        if (e.PropertyName == nameof(MainViewModel.SelectedSection))
            Dispatcher.UIThread.Post(() => Motion.RiseIn(PagesList, Motion.Base), DispatcherPriority.Background);

        if (e.PropertyName == nameof(MainViewModel.SelectedPage))
        {
            bool wasPdf = PagePdfViewer.IsVisible;   // capture BEFORE ApplyPdfPage flips the visibilities
            ApplyPdfPage();     // PDF page → embedded viewer; note page → the canvas below
            if (!string.IsNullOrEmpty(Vm?.SelectedPage?.PdfPath))
            {
                // Switching TO a PDF: rise the viewer in like a note page instead of popping. Hide it
                // NOW (synchronously) so it doesn't flash at full opacity for the frame before the
                // Background-posted RiseIn snaps it to 0 — that flash is the "flicker".
                PagePdfViewer.Opacity = 0;
                Dispatcher.UIThread.Post(() => Motion.RiseIn(PagePdfViewer, Motion.Base), DispatcherPriority.Background);
            }
            else if (wasPdf)
            {
                // Leaving the PDF viewer: PageDock was hidden, so fading it out would flash the STALE
                // note doc for a frame first ("pops back to it, THEN animates"). Hide it, swap the doc
                // while hidden, then rise the new page in — a clean reveal with no pop.
                PageDock.Opacity = 0;
                SyncEditorDocument();
            }
            // Note → note: fade the current page out, THEN swap + rise the new one in.
            else if (PageCanvas.Document is not null) Motion.FadeOut(PageDock, Motion.Fast, SyncEditorDocument);
            else SyncEditorDocument();
        }
        else if (e.PropertyName is nameof(MainViewModel.ToolbarPosition) or nameof(MainViewModel.ToolbarScope))
            ApplyToolbarPlacement();
        else if (e.PropertyName is nameof(MainViewModel.ResizablePages) or nameof(MainViewModel.DeletedHistory)
                 or nameof(MainViewModel.AlwaysShowBorders)
                 or nameof(MainViewModel.PageGrid) or nameof(MainViewModel.GridSnap)
                 or nameof(MainViewModel.DoubleClickCreate) or nameof(MainViewModel.RoundedPdfCorners)
                 or nameof(MainViewModel.MindmapTidyLayout))
            ApplyCanvasPrefs();
        else if (e.PropertyName == nameof(MainViewModel.CornerRoundness))
        {
            UpdateCanvasPlateClip();                     // the punched hole follows the page radius
            ApplyEditorPrefs(rebuild: true);             // canvas rebuild re-reads NoteRadiusPref
        }
        else if (e.PropertyName == nameof(MainViewModel.SectionsSidebar))
            ApplySectionsSidebar(animate: true);
        else if (e.PropertyName == nameof(MainViewModel.SingleMode))
            ApplySectionsSidebar();   // show/hide all section UI (the VM already restructured the tree)
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
            Motion.Reveal(PagesPanel, Vm?.PagesPanelWidth ?? 224, Vm?.IsPagesVisible ?? true);
        else if (e.PropertyName == nameof(MainViewModel.PagesPanelWidth))
            ApplyPanels();          // reset-to-defaults (or any programmatic width change) applies live
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
            // GlassWindow / the new paper tokens only after that handler has had a chance to run.
            Dispatcher.UIThread.Post(() =>
            {
                ApplyGlassTint();
                ApplyPdfBackdrop(PagePdfViewer.IsVisible);   // re-tint the PDF backdrop for the new theme
            }, DispatcherPriority.Background);
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
        PageCanvas.ImageRoot = Vm?.SelectedNotebookDir;   // set BEFORE Document so image boxes resolve
        PageCanvas.Document = Vm?.SelectedPage is { } page ? Vm.DocumentFor(page) : null;
        ApplyPageStyles();                     // the new page's effective grid + method guides
        if (PageCanvas.Document is null) { TrashPanel.IsVisible = false; return; }
        if (TrashPanel.IsVisible) RefreshTrashPanel();
        // Page switch: title + canvas rise in instead of the content popping.
        Dispatcher.UIThread.Post(() => Motion.RiseIn(PageDock, Motion.Base), DispatcherPriority.Background);
    }

    // ---- deleted-containers history panel ----

    /// <summary>Rebuild the Tagged-notes list: tag-order groups (colored glyph header), then a chip
    /// per tagged line with its section › page trail; clicking a chip jumps to that page.</summary>
    private void RefreshTagsPanel()
    {
        TagsList.Children.Clear();
        if (Vm is not { } vm) return;
        var muted = (IBrush)this.FindResource("TextMutedBrush")!;
        var lines = vm.CollectTaggedLines();
        if (lines.Count == 0)
        {
            TagsList.Children.Add(new TextBlock
            {
                Text = "Nothing tagged yet. Use the tag button in the toolbar to mark a line.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = muted,
            });
            return;
        }
        foreach (var (key, glyph, color, name) in Editor.TagStyles.All)
        {
            var group = lines.Where(l => l.Tag == key).ToList();
            if (group.Count == 0) continue;
            var head = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 4, 0, 0) };
            head.Children.Add(new TextBlock
            {
                Text = glyph, FontSize = 12, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(color)),
            });
            head.Children.Add(new TextBlock
            {
                Text = name.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = muted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            TagsList.Children.Add(head);
            foreach (var l in group)
                TagsList.Children.Add(BuildTagChip(l, muted));
        }
    }

    private Control BuildTagChip((string Tag, string Text, Section Section, Models.Page Page) l, IBrush muted)
    {
        var preview = l.Text.Replace('\n', ' ').Trim();
        if (preview.Length == 0) preview = "(empty line)";
        else if (preview.Length > 60) preview = preview[..60] + "…";

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = preview, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
        });
        stack.Children.Add(new TextBlock
        {
            Text = l.Section.Name + " › " + l.Page.Title, FontSize = 10.5, Foreground = muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14FFFFFF")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand), Child = stack,
        };
        chip.PointerReleased += (_, e) =>
        {
            if (Vm is not { } vm) return;
            vm.SelectedSection = l.Section;
            vm.SelectedPage = l.Page;
            e.Handled = true;
        };
        return chip;
    }

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
        BeginRenameOverlay("RENAME SECTION", sec.Name, n => { sec.Name = n; Vm?.Save(); }, SectionsList);
    }

    private void BeginRenamePage(Models.Page? pg)
    {
        if (pg is null) return;
        if (Vm is { } vm) vm.SelectedPage = pg;
        BeginRenameOverlay("RENAME PAGE", pg.Title, t => { pg.Title = t; Vm?.Save(); }, PagesList);
    }

    // ---- zoomed rename overlay: blur the whole app, dim it, and float one big name box ----------
    private System.Action<string>? _renameCommit;
    private Control? _renameReturnFocus;
    private Avalonia.Media.BlurEffect? _renameBlur;
    private Avalonia.Threading.DispatcherTimer? _renameBlurTimer;

    private void BeginRenameOverlay(string title, string current, System.Action<string> commit, Control? returnFocus)
    {
        _renameCommit = commit;
        _renameReturnFocus = returnFocus;
        RenameTitle.Text = title;
        RenameBox.Text = current;

        RenameOverlay.IsVisible = true;
        AnimateBlur(to: 16);
        Motion.FadeIn(RenameVeil, Motion.Fast);
        Motion.ScaleIn(RenameCard, 0.92, Motion.Fast);
        Dispatcher.UIThread.Post(() => { RenameBox.Focus(); RenameBox.SelectAll(); }, DispatcherPriority.Background);
    }

    private void EndRenameOverlay(bool commit)
    {
        if (!RenameOverlay.IsVisible) return;
        if (commit)
        {
            var name = (RenameBox.Text ?? "").Trim();
            if (name.Length > 0) _renameCommit?.Invoke(name);   // empty keeps the old name
        }
        _renameCommit = null;

        AnimateBlur(to: 0);
        Motion.FadeOut(RenameCard, Motion.Fast);
        Motion.FadeOut(RenameVeil, Motion.Fast, () =>
        {
            RenameOverlay.IsVisible = false;
            _renameReturnFocus?.Focus();
            _renameReturnFocus = null;
        });
    }

    /// <summary>Ease AppRoot's blur radius toward a target (the same 15ms code-tween style as
    /// Motion — Effect properties have no declarative animation path here). Radius 0 removes the
    /// effect entirely so normal rendering pays nothing.</summary>
    private void AnimateBlur(double to)
    {
        _renameBlurTimer?.Stop();
        _renameBlurTimer = null;
        if (to > 0 && _renameBlur is null)
        {
            _renameBlur = new Avalonia.Media.BlurEffect { Radius = 0.01 };
            AppRoot.Effect = _renameBlur;
        }
        if (!Motion.Enabled || _renameBlur is null)
        {
            if (to <= 0) { AppRoot.Effect = null; _renameBlur = null; }
            else _renameBlur!.Radius = to;
            return;
        }
        double from = _renameBlur.Radius;
        Avalonia.Threading.DispatcherTimer? timer = null;
        timer = Motion.Clock(Motion.Fast, p =>
        {
            double e = Motion.EaseOut(p);
            if (_renameBlur is { } b) b.Radius = System.Math.Max(0.01, Motion.Lerp(from, to, e));
        }, done: () =>
        {
            if (ReferenceEquals(_renameBlurTimer, timer)) _renameBlurTimer = null;
            if (to <= 0) { AppRoot.Effect = null; _renameBlur = null; }
        });
        _renameBlurTimer = timer;
    }

    // ---- right-click delete menus (with confirm) for notebooks, sections, pages ----

    private void OnSectionsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Section sec) return;
        if (Vm is { } vm) vm.SelectedSection = sec;
        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginRenameSection(sec);
        var customize = new MenuItem { Header = "Customize section…" };
        customize.Click += async (_, _) =>
        {
            if (Vm is not { } v || Window is not { } w) return;
            await new CustomizeSheetWindow(v, sec).ShowDialog(w);
            RefreshAfterStyleDialog(null);          // bulk apply may restyle the page on screen
        };
        var delete = new MenuItem { Header = "Delete section" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this section?",
            $"“{Label(sec.Name)}” and all its pages will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeleteSection ?? true,
            () => CollapseThenDelete(SectionsList.ContainerFromItem(sec) as Control, () => Vm?.DeleteSectionCommand.Execute(sec)));
        OpenMenu(e, rename, customize, delete);
    }

    private MenuItem CustomizeMenuItem(Notebook nb)
    {
        var customize = new MenuItem { Header = "Customize notebook…" };
        customize.Click += (_, _) => OpenNotebookWizard(nb);
        return customize;
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
        OpenMenu(e, CustomizeMenuItem(nb), PaperTintMenu(nb), delete);
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
        OpenMenu(e, CustomizeMenuItem(nb), delete);
    }

    private void OnPagesContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Models.Page pg) return;
        if (Vm is { } vm) vm.SelectedPage = pg;

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginRenamePage(pg);

        // The real customization dialog (M9 Part 4) replaced the temporary grid/style submenus.
        var customize = new MenuItem { Header = "Customize page…" };
        customize.Click += async (_, _) =>
        {
            if (Vm is not { } v || Window is not { } w) return;
            await new CustomizeSheetWindow(v, pg).ShowDialog(w);
            RefreshAfterStyleDialog(pg);
        };

        var export = new MenuItem { Header = "Export page…" };
        export.Click += async (_, _) => await ExportPageAsync(pg);

        var delete = new MenuItem { Header = "Delete page" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this page?",
            $"“{Label(pg.Title)}” will be permanently deleted. This can't be undone.",
            Vm?.ConfirmDeletePage ?? true,
            () => CollapseThenDelete(PagesList.ContainerFromItem(pg) as Control, () => Vm?.DeletePageCommand.Execute(pg)));
        OpenMenu(e, rename, customize, export, delete);
    }

    /// <summary>After a customization dialog closes: re-apply the guides and re-push the canvas
    /// document so a freshly stamped starter layout shows without switching pages.</summary>
    private void RefreshAfterStyleDialog(Models.Page? pg)
    {
        ApplyPageStyles();
        if (pg is null || ReferenceEquals(Vm?.SelectedPage, pg))
            PageCanvas.Document = PageCanvas.Document;
    }

    /// <summary>The mind-map toolbar's paint-bucket button (accent-lit while the fill tool is active).</summary>
    private Button? _paintBtn;

    // ---- Ctrl+wheel canvas zoom (session-only viewing posture, not a preference) ----
    private double _canvasZoom = 1.0;

    private void SetCanvasZoom(double zoom)
    {
        zoom = System.Math.Round(System.Math.Clamp(zoom, 0.5, 2.0), 2);
        if (System.Math.Abs(zoom - _canvasZoom) < 0.001) return;
        _canvasZoom = zoom;
        CanvasZoomHost.LayoutTransform = zoom == 1.0 ? null : new ScaleTransform(zoom, zoom);
        PushCanvasViewport();
    }

    /// <summary>Guides/starters think in canvas coordinates — the visible area is the scroll
    /// viewport divided by the zoom.</summary>
    private void PushCanvasViewport()
    {
        var s = new Size(CanvasScroll.Bounds.Width / _canvasZoom, CanvasScroll.Bounds.Height / _canvasZoom);
        PageCanvas.SetViewport(s);
        if (Vm is { } vm) vm.CanvasViewport = (s.Width, s.Height);
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
        var paper = PaperTintMenu(nb);

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

        var customize = CustomizeMenuItem(nb);

        if (nb.CoverPath is not null)
        {
            var removeCover = new MenuItem { Header = "Remove cover image" };
            removeCover.Click += (_, _) => Vm?.ClearNotebookCover(nb);
            OpenMenu(e, open, customize, rename, moveLeft, moveRight, color, paper, cover, removeCover, delete);
        }
        else
        {
            OpenMenu(e, open, customize, rename, moveLeft, moveRight, color, paper, cover, delete);
        }
    }

    /// <summary>Pick an image file and drop it on the current page as an image box (copied into the
    /// notebook's images folder, so the page stays self-contained).</summary>
    private async System.Threading.Tasks.Task InsertImageAsync()
    {
        if (Vm is not { SelectedPage: not null } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Insert image", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Images")
                { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" } },
            },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (vm.ImportPageImage(path) is not { } rel) return;
        // Drop it near the top-left of what's currently in view.
        double x = CanvasScroll.Offset.X / _canvasZoom + 40;
        double y = CanvasScroll.Offset.Y / _canvasZoom + 40;
        PageCanvas.ImageRoot = vm.SelectedNotebookDir;
        PageCanvas.AddImage(rel, x, y);
    }

    /// <summary>Pick any file and drop it on the current page as an attachment chip (copied into
    /// the notebook's assets folder, so the page stays self-contained).</summary>
    private async System.Threading.Tasks.Task InsertAttachmentAsync()
    {
        if (Vm is not { SelectedPage: not null } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Attach file", AllowMultiple = false,
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (vm.ImportPageAsset(path) is not { } rel) return;
        double x = CanvasScroll.Offset.X / _canvasZoom + 40;
        double y = CanvasScroll.Offset.Y / _canvasZoom + 40;
        PageCanvas.ImageRoot = vm.SelectedNotebookDir;
        PageCanvas.AddAttachment(rel, x, y);
    }

    /// <summary>Pick a PDF, attach it to the page (copied into the notebook), and open it straight
    /// away in the in-app viewer/annotator — the discoverable one-step "open a PDF" path.</summary>
    private async System.Threading.Tasks.Task InsertPdfAsync()
    {
        if (Vm is not { SelectedPage: not null } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open a PDF", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } },
            },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (vm.ImportPageAsset(path) is not { } rel) return;
        double x = CanvasScroll.Offset.X / _canvasZoom + 40;
        double y = CanvasScroll.Offset.Y / _canvasZoom + 40;
        PageCanvas.ImageRoot = vm.SelectedNotebookDir;
        PageCanvas.AddAttachment(rel, x, y);
        // Open it right away so the user sees the viewer without hunting for the double-click.
        if (vm.SelectedNotebookDir is { } dir)
        {
            var full = System.IO.Path.Combine(dir, rel);
            var viewer = new PdfViewerWindow(full, vm.DoubleClickCreate);
            if (Window is { } w) viewer.Show(w); else viewer.Show();
        }
    }

    // ---- section / page "+" menus (add · rearrange · open a PDF as page/section) ----
    private void OpenSectionsMenu(Control anchor)
    {
        if (Vm is not { SelectedNotebook: { } nb }) return;
        var menu = new ContextMenu();
        MenuItem Item(string h, System.Action a, bool on = true)
        {
            var m = new MenuItem { Header = h, IsEnabled = on };
            m.Click += (_, _) => a();
            menu.Items.Add(m);
            return m;
        }
        Item("Add section", () => Vm.AddSectionCommand.Execute(null));
        Item("Rearrange sections…", async () =>
        {
            if (Window is { } w)
                await ReorderDialog.Show(w, "Rearrange sections",
                    nb.Sections.Select(s => s.Name).ToList(), (f, t) => nb.Sections.Move(f, t));
            Vm.Save();
        }, nb.Sections.Count > 1);
        menu.Items.Add(new Separator());
        Item("Open a PDF as a section…", async () => await OpenPdfAsSection());
        MenuFx.Attach(menu);
        menu.Open(anchor);
    }

    private void OpenPagesMenu(Control anchor)
    {
        if (Vm is not { SelectedSection: { } sec }) return;
        var menu = new ContextMenu();
        MenuItem Item(string h, System.Action a, bool on = true)
        {
            var m = new MenuItem { Header = h, IsEnabled = on };
            m.Click += (_, _) => a();
            menu.Items.Add(m);
            return m;
        }
        Item("Add page", () => Vm.AddPageCommand.Execute(null));
        Item("Rearrange pages…", async () =>
        {
            if (Window is { } w)
                await ReorderDialog.Show(w, "Rearrange pages",
                    sec.Pages.Select(p => p.Title).ToList(), (f, t) => sec.Pages.Move(f, t));
            Vm.Save();
        }, sec.Pages.Count > 1);
        menu.Items.Add(new Separator());
        Item("Open a PDF as a page…", async () => await OpenPdfAsPage());
        MenuFx.Attach(menu);
        menu.Open(anchor);
    }

    // ---- open a PDF file as a dedicated page/section (the page IS the PDF, edited inline) ----
    private async System.Threading.Tasks.Task<string?> PickAndImportPdf()
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return null;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open a PDF", AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return null;
        return vm.ImportPageAsset(path);        // copies into the notebook's assets, returns the relative path
    }

    private async System.Threading.Tasks.Task OpenPdfAsPage()
    {
        if (Vm is not { SelectedSection: { } sec } vm) return;
        if (await PickAndImportPdf() is not { } rel) return;
        var pg = new Models.Page { Title = System.IO.Path.GetFileNameWithoutExtension(rel), PdfPath = rel };
        sec.Pages.Add(pg);
        vm.SelectedPage = pg;
        vm.Save();
    }

    private async System.Threading.Tasks.Task OpenPdfAsSection()
    {
        if (Vm is not { SelectedNotebook: { } nb } vm) return;
        if (await PickAndImportPdf() is not { } rel) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(rel);
        var sec = new Models.Section { Name = name };
        sec.Pages.Add(new Models.Page { Title = name, PdfPath = rel });
        nb.Sections.Add(sec);
        vm.SelectedSection = sec;
        vm.SelectedPage = sec.Pages[0];
        vm.Save();
    }

    /// <summary>Show the embedded PDF viewer in the page box when the selected page is a PDF, else the
    /// note canvas. Called whenever the selected page changes.</summary>
    private void ApplyPdfPage()
    {
        PagePdfViewer.Flush();      // persist any annotations from the outgoing PDF page
        var rel = Vm?.SelectedPage?.PdfPath;
        bool isPdf = !string.IsNullOrEmpty(rel) && Vm?.SelectedNotebookDir is { };
        PageDock.IsVisible = !isPdf;
        PagePdfViewer.IsVisible = isPdf;
        ApplyPdfBackdrop(isPdf);
        if (isPdf)
        {
            var full = System.IO.Path.Combine(Vm!.SelectedNotebookDir!, rel!);
            PagePdfViewer.Load(full, Vm!.DoubleClickCreate);
        }
    }

    /// <summary>The ONLY theme combo where the PDF viewer needed a backdrop fix is a SOLID theme with
    /// Full-theme OFF: there the page box is a real-glass acrylic hole, so a PDF sat over the bare
    /// desktop. In that one case we recolor the rounded page-box Border to an opaque neutral (following
    /// the theme's light/dark). EVERY other combo — all of Lumen (its glass/frost is intentional), and
    /// solid Full-theme-on — keeps its own PaperBackground untouched, so Lumen looks exactly like it
    /// always has. The rounded Border is what we recolor, so corners stay rounded.</summary>
    private void ApplyPdfBackdrop(bool isPdf)
    {
        bool needsOpaque = isPdf && Vm is { } vm && vm.Theme != "Lumen" && !vm.FullTheme;
        if (!needsOpaque)
        {
            PageBoxSurface.Bind(Border.BackgroundProperty, PageBoxSurface.GetResourceObservable("PaperBackgroundBrush"));
            return;
        }
        var c = Avalonia.Media.Color.Parse(Services.ThemeManager.Current.DarkChrome ? "#171A22" : "#E9EDF3");
        PageBoxSurface.Background = new Avalonia.Media.SolidColorBrush(c);
    }

    /// <summary>Drop a line divider ("h"/"v") on the current page, near the top-left of the view.</summary>
    private void InsertDivider(string orientation)
    {
        if (Vm is not { SelectedPage: not null }) return;
        double x = CanvasScroll.Offset.X / _canvasZoom + 60;
        double y = CanvasScroll.Offset.Y / _canvasZoom + 60;
        PageCanvas.AddDivider(orientation, x, y);
    }

    /// <summary>Drop a rows×cols table on the current page, near the top-left of the view.</summary>
    private void InsertTable(int rows, int cols)
    {
        if (Vm is not { SelectedPage: not null }) return;
        double x = CanvasScroll.Offset.X / _canvasZoom + 50;
        double y = CanvasScroll.Offset.Y / _canvasZoom + 50;
        PageCanvas.AddTable(rows, cols, x, y);
    }

    /// <summary>Export one page to a file the user picks — the format follows the chosen file type
    /// (all eight offered), so it's one Save dialog, no extra prompt.</summary>
    private async System.Threading.Tasks.Task ExportPageAsync(Models.Page pg)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var choices = Services.PageExport.Formats.Select(f =>
            new Avalonia.Platform.Storage.FilePickerFileType(f.Label) { Patterns = new[] { "*" + f.Ext } }).ToArray();
        var file = await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export page",
            SuggestedFileName = Services.MarkdownExport.SafeName(pg.Title),
            DefaultExtension = "pdf",
            FileTypeChoices = choices,
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var fmt = Services.PageExport.Formats.FirstOrDefault(f => f.Ext == ext, Services.PageExport.Formats[4]).Fmt;   // default PDF
        try
        {
            vm.FlushDirtyDocs();
            var bytes = Services.PageExport.Export(fmt, pg.Title, vm.DocumentFor(pg), vm.SelectedNotebookDir);
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
        catch (System.Exception ex)
        {
            if (Window is { } w)
                await ConfirmDialog.Show(w, "Export failed", ex.Message, "OK", "Close", danger: false);
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

    /// <summary>Soft tints that stay readable at the veil's fixed alpha on light AND dark paper.</summary>
    private static readonly (string Name, string? Hex)[] PaperTints =
    {
        ("None", null),
        ("Ivory", "#E8D9A8"), ("Peach", "#EFB98E"), ("Rose", "#EC9EB6"),
        ("Mint", "#9BD3A6"), ("Sky", "#8FC2EC"), ("Lavender", "#B4A2E6"),
        ("Sand", "#CBB98F"), ("Graphite", "#8C939E"),
    };

    /// <summary>The per-notebook "Paper color" submenu (current choice shown bold).</summary>
    private MenuItem PaperTintMenu(Notebook nb)
    {
        var root = new MenuItem { Header = "Paper color" };
        foreach (var (name, hex) in PaperTints)
        {
            var item = new MenuItem
            {
                Header = name,
                Icon = hex is null ? null : Swatch(hex),
                FontWeight = string.Equals(nb.PaperTint, hex, System.StringComparison.OrdinalIgnoreCase)
                    ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var chosen = hex;
            item.Click += (_, _) => { Vm?.SetNotebookPaperTint(nb, chosen); ApplyPaperTint(); };
            root.Items.Add(item);
        }
        return root;
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
        MenuFx.Attach(menu);       // rise-in + real popup acrylic for the Lumen glass variant
        if (e.Source is Control c) { menu.Open(c); e.Handled = true; }
    }

    private async void ConfirmThenDelete(string title, string message, bool ask, System.Action delete)
    {
        if (!ask) { delete(); return; }
        if (Window is not { } w) return;
        if (await ConfirmDialog.Show(w, title, message)) delete();
    }
}
