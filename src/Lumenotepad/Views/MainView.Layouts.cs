using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Lumenotepad.Models;

namespace Lumenotepad.Views;

public partial class MainView
{
    private readonly HashSet<string> _foldedSections = new();
    private readonly List<Action> _outlineUnhooks = new();
    private bool _outlineSyncing;

    private string Layout => Vm?.UiLayout ?? "Classic";
    private bool IsClassic => Layout == "Classic";

    private void WireLayouts()
    {
        var pageRow = (IDataTemplate)this.FindResource("PageRow")!;
        var sectionRow = (IDataTemplate)this.FindResource("SectionRowTemplate")!;
        OutlineList.ItemTemplate = new FuncDataTemplate<object>(
            (item, _) => item is SectionRow ? sectionRow.Build(item)! : pageRow.Build(item)!, supportsRecycling: false);

        OutlineList.SelectionChanged += OnOutlineSelectionChanged;
        OutlineList.ContextRequested += OnOutlineContextRequested;
        TabsPagesList.ContextRequested += OnPagesContextRequested;
        SectionTabsBar.ContextRequested += OnSectionsContextRequested;
        SidebarNotebookBtn.Click += (_, _) => NotebookMenu(SidebarNotebookBtn);
        TitleNotebookBtn.Click += (_, _) => NotebookMenu(TitleNotebookBtn);
        FocusNotebookBtn.Click += (_, _) => NotebookMenu(FocusNotebookBtn);
        FocusSectionBtn.Click += (_, _) => OpenListMenu(FocusSectionBtn, FocusSectionItems());
        FocusPageBtn.Click += (_, _) => OpenListMenu(FocusPageBtn, FocusPageItems());
        OutlineNewPageBtn.Click += (_, _) => Vm?.AddPageCommand.Execute(null);
        OutlineNewSectionBtn.Click += (_, _) => Vm?.AddSectionCommand.Execute(null);
        TabsNewPageBtn.Click += (_, _) => Vm?.AddPageCommand.Execute(null);
        SectionTabsAddBtn.Click += (_, _) => Vm?.AddSectionCommand.Execute(null);
        PageCanvas.ActiveEditorChanged += _ => ApplyFocusToolbar();
    }

    private void ApplyUiLayout(bool animate = false)
    {
        if (Vm is not { } vm) return;
        string l = Layout;
        bool classic = l == "Classic";
        RailPanel.IsVisible = classic;
        PagesPanel.IsVisible = classic;
        if (classic) ApplySectionsSidebar();
        else SectionsSidebar.IsVisible = false;

        OutlinePanel.IsVisible = l == "Sidebar";
        TabsPagesPanel.IsVisible = l == "Tabs";
        foreach (var side in new Control[] { OutlinePanel, TabsPagesPanel })
        {
            Motion.Stop(side);
            side.Width = vm.IsPagesVisible ? vm.PagesPanelWidth : 0;
            side.Opacity = vm.IsPagesVisible ? 1 : 0;
        }

        SectionTabsRow.IsVisible = l == "Tabs" && !vm.SingleMode;
        FocusPath.IsVisible = l == "Focus";
        FocusSectionBtn.IsVisible = !vm.SingleMode;
        FocusSectionSep.IsVisible = !vm.SingleMode;
        LayoutTopBar.IsVisible = SectionTabsRow.IsVisible || FocusPath.IsVisible;
        PageBoxSurface.Margin = LayoutTopBar.IsVisible ? new Thickness(14, 8, 14, 14) : new Thickness(14);

        Toolbar.SetSimple(!classic);
        ApplyFocusToolbar();

        PanelBubble.HorizontalAlignment = l == "Tabs" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        PanelBubble.Margin = l == "Tabs" ? new Thickness(0, 0, 26, 46) : new Thickness(26, 0, 0, 46);

        RebuildOutline();
        ApplyButtonPlacement();
        if (animate) Motion.FadeIn(BodyDock, Motion.Base);
    }

    private void ApplyFocusToolbar()
    {
        bool show = Layout != "Focus" || PageCanvas.ActiveEditor is not null;
        Toolbar.IsHitTestVisible = show;
        if (show && Toolbar.Opacity < 1) Motion.FadeIn(Toolbar, Motion.Fast);
        else if (!show && Toolbar.Opacity > 0) Motion.FadeOut(Toolbar, Motion.Fast);
    }

    private void RevealSidePanel(bool show)
    {
        if (Vm is not { } vm) return;
        if (IsClassic) Motion.Reveal(PagesPanel, vm.PagesPanelWidth, show);
        else if (Layout == "Sidebar") Motion.Reveal(OutlinePanel, vm.PagesPanelWidth, show);
        else if (Layout == "Tabs") Motion.Reveal(TabsPagesPanel, vm.PagesPanelWidth, show);
    }

    private void RebuildOutline()
    {
        foreach (var unhook in _outlineUnhooks) unhook();
        _outlineUnhooks.Clear();
        if (Vm is not { } vm || Layout != "Sidebar")
        {
            OutlineList.ItemsSource = null;
            return;
        }
        if (vm.SelectedNotebook is { } nb)
        {
            void Changed(object? s, NotifyCollectionChangedEventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildOutline);
            nb.Sections.CollectionChanged += Changed;
            _outlineUnhooks.Add(() => nb.Sections.CollectionChanged -= Changed);
            foreach (var sec in nb.Sections)
            {
                var pages = sec.Pages;
                pages.CollectionChanged += Changed;
                _outlineUnhooks.Add(() => pages.CollectionChanged -= Changed);
            }
        }
        _outlineSyncing = true;
        OutlineList.ItemsSource = NavRows.Build(vm.SelectedNotebook, vm.SingleMode, _foldedSections);
        OutlineList.SelectedItem = vm.SelectedPage;
        _outlineSyncing = false;
    }

    private void SyncOutlineSelection()
    {
        if (Layout != "Sidebar" || Vm is not { } vm) return;
        _outlineSyncing = true;
        OutlineList.SelectedItem = vm.SelectedPage;
        _outlineSyncing = false;
    }

    private void OnOutlineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_outlineSyncing || Vm is not { SelectedNotebook: { } nb } vm) return;
        if (OutlineList.SelectedItem is not Models.Page pg) return;
        if (NavRows.SectionOf(nb, pg) is { } sec && !ReferenceEquals(vm.SelectedSection, sec)) vm.SelectedSection = sec;
        vm.SelectedPage = pg;
    }

    private void OnSectionRowClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SectionRow row) return;
        if (!_foldedSections.Remove(row.Section.Id)) _foldedSections.Add(row.Section.Id);
        RebuildOutline();
        e.Handled = true;
    }

    private void OnOutlineContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        switch ((e.Source as StyledElement)?.DataContext)
        {
            case Models.Page:
                OnPagesContextRequested(sender, e);
                break;
            case SectionRow row:
                if (Vm is { } vm) vm.SelectedSection = row.Section;
                var items = SectionsMenuItems(row.Section);
                if (items.Count > 0) OpenMenu(e, items.ToArray());
                break;
            default:
                if (Vm is { } v2)
                    OpenMenu(e, Act("New page", () => v2.AddPageCommand.Execute(null)),
                                Act("New section", () => v2.AddSectionCommand.Execute(null)));
                break;
        }
    }

    private void NotebookMenu(Control anchor)
    {
        if (Vm is not { } vm) return;
        var items = new List<Control>();
        foreach (var nb in vm.Notebooks)
        {
            var target = nb;
            var item = Act(string.IsNullOrWhiteSpace(nb.Name) ? "Untitled notebook" : nb.Name,
                           () => vm.OpenNotebookCommand.Execute(target));
            item.Icon = Swatch(nb.Color);
            if (ReferenceEquals(nb, vm.SelectedNotebook)) item.FontWeight = FontWeight.SemiBold;
            items.Add(item);
        }
        items.Add(new Separator());
        items.Add(Act("All notebooks", () => vm.GoHomeCommand.Execute(null)));
        items.Add(Act("New notebook", () => OpenNotebookWizard()));
        OpenListMenu(anchor, items);
    }

    private static void OpenListMenu(Control anchor, IEnumerable<Control> items)
    {
        var menu = new ContextMenu();
        foreach (var i in items) menu.Items.Add(i);
        MenuFx.Attach(menu);
        menu.Open(anchor);
    }

    private IEnumerable<Control> FocusSectionItems()
    {
        if (Vm is not { SelectedNotebook: { } nb } vm) yield break;
        foreach (var s in nb.Sections)
        {
            var target = s;
            var item = Act(string.IsNullOrWhiteSpace(s.Name) ? "Untitled section" : s.Name, () => vm.SelectedSection = target);
            if (ReferenceEquals(s, vm.SelectedSection)) item.FontWeight = FontWeight.SemiBold;
            yield return item;
        }
        yield return new Separator();
        yield return Act("New section", () => vm.AddSectionCommand.Execute(null));
    }

    private IEnumerable<Control> FocusPageItems()
    {
        if (Vm is not { SelectedSection: { } sec } vm) yield break;
        foreach (var p in sec.Pages)
        {
            var target = p;
            string title = string.IsNullOrWhiteSpace(p.Title) ? "Untitled page" : p.Title;
            var item = Act(title, () => vm.SelectedPage = target);
            item.Padding = new Thickness(12 + p.Level * 16, item.Padding.Top, item.Padding.Right, item.Padding.Bottom);
            if (ReferenceEquals(p, vm.SelectedPage)) item.FontWeight = FontWeight.SemiBold;
            yield return item;
        }
        yield return new Separator();
        yield return Act("New page", () => vm.AddPageCommand.Execute(null));
        if (vm.SelectedPage is { } cur && PageTree.CanAddSubPage(sec.Pages, sec.Pages.IndexOf(cur)))
            yield return Act("New sub-page", () => vm.NewSubPage(cur));
    }
}
