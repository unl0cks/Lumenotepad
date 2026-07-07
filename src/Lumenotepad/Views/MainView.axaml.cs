using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        SectionsList.ContextRequested += OnSectionsContextRequested;
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
        }
    }

    /// <summary>Push the (future preferences window's) canvas toggles onto the canvas.</summary>
    private void ApplyCanvasPrefs()
    {
        if (Vm is not { } vm) return;
        PageCanvas.CanResize = vm.ResizablePages;
        PageCanvas.HistoryEnabled = vm.DeletedHistory;
        if (!vm.DeletedHistory) TrashPanel.IsVisible = false;
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

    private void OnSectionsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not Section sec) return;
        if (Vm is { } vm) vm.SelectedSection = sec;
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginRenameSection(sec);
        var delete = new MenuItem { Header = "Delete section" };
        delete.Click += (_, _) => Vm?.DeleteSectionCommand.Execute(sec);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        if (e.Source is Control c) { menu.Open(c); e.Handled = true; }
    }
}
