using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        // Section inline rename: double-click a tab, the rename button, or right-click → Rename.
        RenameSectionBtn.Click += (_, _) => BeginRenameSection(Vm?.SelectedSection);
        SectionsList.DoubleTapped += (_, e) => BeginRenameSection((e.Source as StyledElement)?.DataContext as Section);
        SectionsList.AddHandler(LostFocusEvent, OnSectionEditLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        SectionsList.AddHandler(KeyDownEvent, OnSectionEditKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        SectionsList.ContextRequested += OnSectionsContextRequested;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;
    private Window? Window => TopLevel.GetTopLevel(this) as Window;

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
