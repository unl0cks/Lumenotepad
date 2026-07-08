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
        PageCanvas.ConfirmDelete = () => ConfirmDialog.Show(Window!,
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

        // Keep the canvas plate's punched hole aligned with the page box (margin 14, radius 14).
        CanvasPlate.SizeChanged += (_, _) => UpdateCanvasPlateClip();
    }

    // ---- gallery rearrange mode ----

    private bool _rearranging;
    private Notebook? _dragNotebook;

    private void SetRearranging(bool on)
    {
        _rearranging = on;
        _dragNotebook = null;
        HomeCards.Classes.Set("rearrange", on);
        RearrangeBtn.Classes.Set("on", on);
    }

    private Border? _grabbedCard;

    private void OnRearrangePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_rearranging) return;
        if ((e.Source as StyledElement)?.DataContext is not Notebook nb) return;
        _dragNotebook = nb;
        // "Snap up" like grabbing it (Apple-style): the card pops bigger while held.
        _grabbedCard = (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true) is { } b
            && b.Classes.Contains("nbcard") ? b
            : (e.Source as Visual)?.GetVisualAncestors().OfType<Border>().FirstOrDefault(x => x.Classes.Contains("nbcard"));
        if (_grabbedCard is not null)
            _grabbedCard.RenderTransform = TransformOperations.Parse("scale(1.09)");
        e.Pointer.Capture(HomeCards);
        e.Handled = true;
    }

    private void OnRearrangeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragNotebook is null || Vm is null) return;
        foreach (var container in HomeCards.GetRealizedContainers())
        {
            var p = e.GetPosition(container);
            if (p.X < 0 || p.Y < 0 || p.X > container.Bounds.Width || p.Y > container.Bounds.Height) continue;
            int target = HomeCards.IndexFromContainer(container);
            if (target >= 0) Vm.MoveNotebookTo(_dragNotebook, target, save: false);
            break;
        }
    }

    private void OnRearrangeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragNotebook is null) return;
        _dragNotebook = null;
        if (_grabbedCard is not null)
        {
            _grabbedCard.RenderTransform = TransformOperations.Parse("scale(1)");
            _grabbedCard = null;
        }
        e.Pointer.Capture(null);
        Vm?.Save();
    }

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
            SyncEditorDocument();
            ApplyToolbarPlacement();
            ApplyCanvasPrefs();
            ApplyGlossyAccents();
            Toolbar.SetExtendedFonts(_hookedVm.ExtendedFonts);
        }
    }

    /// <summary>Push the preferences-window canvas toggles onto the canvas.</summary>
    private void ApplyCanvasPrefs()
    {
        if (Vm is not { } vm) return;
        PageCanvas.CanResize = vm.ResizablePages;
        PageCanvas.HistoryEnabled = vm.DeletedHistory;
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

    /// <summary>"Glossy accents": gloss on the recents chips + accent-gradient selected pills.</summary>
    private void ApplyGlossyAccents()
    {
        bool glossy = Vm?.GlossyAccents ?? true;
        RecentList.Classes.Set("glossy", glossy);
        SectionsList.Classes.Set("glossy", glossy);
        PagesList.Classes.Set("glossy", glossy);
        NotebooksList.Classes.Set("glossy", glossy);
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
    }

    // Debounced autosave: flush dirty page docs after ~0.9s of typing idle.
    private void OnDocsDirtied()
    {
        if (_autosave is null)
        {
            _autosave = new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(900) };
            _autosave.Tick += (_, _) => { _autosave!.Stop(); Vm?.FlushDirtyDocs(); };
        }
        _autosave.Stop();
        _autosave.Start();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage)) SyncEditorDocument();
        else if (e.PropertyName is nameof(MainViewModel.ToolbarPosition) or nameof(MainViewModel.ToolbarScope))
            ApplyToolbarPlacement();
        else if (e.PropertyName is nameof(MainViewModel.ResizablePages) or nameof(MainViewModel.DeletedHistory))
            ApplyCanvasPrefs();
        else if (e.PropertyName == nameof(MainViewModel.FlatCovers))
            ApplyFlatCovers();
        else if (e.PropertyName == nameof(MainViewModel.GlossyAccents))
            ApplyGlossyAccents();
        else if (e.PropertyName == nameof(MainViewModel.ExtendedFonts))
            Toolbar.SetExtendedFonts(Vm?.ExtendedFonts ?? false);
        else if (e.PropertyName == nameof(MainViewModel.IsHomeVisible) && _rearranging)
            SetRearranging(false);                 // leaving home always exits rearrange mode
        else if (e.PropertyName is nameof(MainViewModel.Theme)
                 or nameof(MainViewModel.FullTheme) or nameof(MainViewModel.PaperLight))
        {
            // Note containers read their paper-region brushes at construction — rebuild them.
            PageCanvas.Document = PageCanvas.Document;
            if (TrashPanel.IsVisible) RefreshTrashPanel();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsHomeVisible) && Vm is { IsHomeVisible: true } vm)
        {
            // Card subtitles (section/page counts) are computed by a converter, so re-realize the
            // cards when coming home — counts changed while editing.
            HomeCards.ItemsSource = null;
            HomeCards.ItemsSource = vm.Notebooks;
        }
    }

    private void SyncEditorDocument()
    {
        Vm?.FlushDirtyDocs();                      // the page being left saves immediately
        PageCanvas.Document = Vm?.SelectedPage is { } page ? Vm.DocumentFor(page) : null;
        if (PageCanvas.Document is null) TrashPanel.IsVisible = false;
        else if (TrashPanel.IsVisible) RefreshTrashPanel();
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
            () => Vm?.DeleteSectionCommand.Execute(sec));
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
            () => Vm?.DeleteNotebookCommand.Execute(nb));
        OpenMenu(e, delete);
    }

    private void OnNotebookNameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm?.SelectedNotebook is not { } nb) return;
        var delete = new MenuItem { Header = "Delete notebook" };
        delete.Click += (_, _) => ConfirmThenDelete(
            "Delete this notebook?",
            $"“{Label(nb.Name)}” and all its sections and pages will be permanently deleted. This can't be undone.",
            () => Vm?.DeleteNotebookCommand.Execute(nb));
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
            () => Vm?.DeletePageCommand.Execute(pg));
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
            () => Vm?.DeleteNotebookCommand.Execute(nb));

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

    private async void ConfirmThenDelete(string title, string message, System.Action delete)
    {
        if (Window is not { } w) return;
        if (await ConfirmDialog.Show(w, title, message)) delete();
    }
}
