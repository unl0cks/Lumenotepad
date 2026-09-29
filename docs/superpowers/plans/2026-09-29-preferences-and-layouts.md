# Preferences Cleanup and Layout Choices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preferences reorganized into 8 pages with Lumen-style tabs, and a Layout setting that switches the main window between Classic, One sidebar, Tabs and Focus.

**Architecture:** Preferences keeps its flat heading-and-rows markup. Cards are grouped in code as today, and a pure table (`PrefsTabs`) assigns each card heading to a tab. The new layouts are extra named panels in `MainView.axaml` driven from a partial class file `MainView.Layouts.cs`. They bind to the same view-model selection and reuse the existing right-click menu handlers, a shared page-row template, and the pure `NavRows` builder for the sidebar outline.

**Tech Stack:** Avalonia 12.0.5, .NET 10, CommunityToolkit.Mvvm 8.4.0, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-29-preferences-and-layouts-design.md`

## Global Constraints

- No comments in any code; no mention of AI or assistants anywhere; no em dashes in any file except the greeting text; no new packages.
- Preserve each file's line endings (patch in binary mode).
- Setting key (verbatim): `UiLayout` string, default "Classic"; values "Classic", "Sidebar", "Tabs", "Focus".
- Layout tile names (verbatim): "Classic", "One sidebar", "Tabs", "Focus".
- Never set `IsVisible` locally on `HomeBtn`, `PrefsBtn`, `RailToggle` or `PagesToggle` (their visibility is bound); hide them by not placing them in any host.
- Version 1.4.0.

---

### Task 1: Preferences tab table

**Files:**
- Create: `src/Lumenotepad/Views/PrefsTabs.cs`
- Test: `tests/Lumenotepad.Tests/PrefsTabsTests.cs`

**Interfaces:**
- Produces: `PrefsTabs.For(string page) -> IReadOnlyList<PrefsTab>?`, `PrefsTabs.TabOf(string page, string heading) -> string?`, `record PrefsTab(string Name, string[] Headings)`, `PrefsTabs.Pages`.

- [ ] **Step 1: Tests**

```csharp
using System.Linq;
using Lumenotepad.Views;
using Xunit;

namespace Lumenotepad.Tests;

public class PrefsTabsTests
{
    [Fact]
    public void EveryHeadingBelongsToExactlyOneTabOfItsPage()
    {
        foreach (var page in PrefsTabs.Pages)
        {
            var tabs = PrefsTabs.For(page)!;
            var headings = tabs.SelectMany(t => t.Headings).ToList();
            Assert.Equal(headings.Count, headings.Distinct().Count());
            Assert.Equal(tabs.Count, tabs.Select(t => t.Name).Distinct().Count());
            Assert.All(tabs, t => Assert.NotEmpty(t.Headings));
        }
    }

    [Fact]
    public void TabOf_findsTheTab_andIgnoresCase()
    {
        Assert.Equal("Text", PrefsTabs.TabOf("editor", "TEXT DEFAULTS"));
        Assert.Equal("Home screen", PrefsTabs.TabOf("general", "Gallery"));
        Assert.Null(PrefsTabs.TabOf("editor", "NOPE"));
        Assert.Null(PrefsTabs.For("shortcuts"));
    }
}
```

- [ ] **Step 2: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Views;

public sealed record PrefsTab(string Name, string[] Headings);

public static class PrefsTabs
{
    private static readonly Dictionary<string, PrefsTab[]> Table = new()
    {
        ["general"] = new[]
        {
            new PrefsTab("Startup", new[] { "STARTUP", "SYSTEM TRAY" }),
            new PrefsTab("Home screen", new[] { "HOMEPAGE", "GALLERY" }),
            new PrefsTab("Deleting", new[] { "ASK BEFORE DELETING" }),
        },
        ["appearance"] = new[]
        {
            new PrefsTab("Theme", new[] { "THEME", "ACCENT" }),
            new PrefsTab("Glass & shape", new[] { "GLASS", "SHAPE" }),
            new PrefsTab("Motion", new[] { "MOTION" }),
        },
        ["layout"] = new[]
        {
            new PrefsTab("Panels", new[] { "LAYOUT", "PANELS" }),
            new PrefsTab("Toolbar", new[] { "TOOLBAR" }),
            new PrefsTab("Pages", new[] { "PAGES" }),
            new PrefsTab("Paper", new[] { "PAPER" }),
        },
        ["editor"] = new[]
        {
            new PrefsTab("Typing", new[] { "CARET", "TYPING" }),
            new PrefsTab("Text", new[] { "TEXT DEFAULTS" }),
            new PrefsTab("Spacing", new[] { "SPACING" }),
            new PrefsTab("Lists", new[] { "SMART LISTS", "BULLET COLORS", "NUMBERED LISTS" }),
            new PrefsTab("Colors", new[] { "QUICK HIGHLIGHT", "TOOLBAR PALETTES" }),
        },
        ["data"] = new[]
        {
            new PrefsTab("Saving & backup", new[] { "SAVING", "STORAGE", "AUTOMATIC BACKUP" }),
            new PrefsTab("Import & export", new[] { "IMPORT & EXPORT" }),
            new PrefsTab("Maintenance", new[] { "MAINTENANCE" }),
        },
    };

    public static IEnumerable<string> Pages => Table.Keys;

    public static IReadOnlyList<PrefsTab>? For(string page) => Table.TryGetValue(page, out var t) ? t : null;

    public static string? TabOf(string page, string heading) =>
        For(page)?.FirstOrDefault(t => t.Headings.Any(h => string.Equals(h, heading, StringComparison.OrdinalIgnoreCase)))?.Name;
}
```

- [ ] **Step 3: Run** `dotnet test tests/Lumenotepad.Tests -c Release --filter PrefsTabsTests` (fails first, then passes). **Commit** "Add the Preferences tab table".

---

### Task 2: Preferences markup reshuffle

**Files:** Modify `src/Lumenotepad/Views/PreferencesWindow.axaml`, `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`

Moves (each "block" is a section heading element plus every element up to the next heading or the end of its panel):

1. General: move the `SAVING` block into `DataPanel`, before `STORAGE`.
2. Appearance: move the `GALLERY` block into `GeneralPanel`, after the `HOMEPAGE` block.
3. Appearance: in the custom accent row (the `Grid` holding `AccentHexBox`), put `<TextBlock Classes="label" Text="Custom color"/>` before its help button.
4. Layout: reorder `LayoutPanel` to `PANELS` then `TOOLBAR` (a `LAYOUT` block is added in front in Task 7). Move the children of `CanvasPanel` (`PAGES`, `PAPER`) to the end of `LayoutPanel` and delete the empty `CanvasPanel` element.
5. Editor: split the `WRITING` heading: rename it to `TYPING` and keep "Date & time format" and "Capitalize the first letter of sentences" under it. Move the "Quick highlight color" row under a new `<TextBlock Classes="section" Text="QUICK HIGHLIGHT"/>` placed right before `TOOLBAR PALETTES`. Move the "Keep the last font I pick" row to the end of `TEXT DEFAULTS`. Rename `SMART INPUT` to `SMART LISTS`. Move the children of `BulletsPanel` (`BULLET COLORS`, `NUMBERED LISTS`) right after `SMART LISTS`, then delete the empty `BulletsPanel`.
6. Nav: delete the `canvas` and `bullets` `ListBoxItem`s. Rename the `editor` item's label to "Writing". Remove the `WORKSPACE` group heading and put `layout` under `GENERAL`, after `appearance`.
7. Code: remove `["canvas"]` and `["bullets"]` from `_panels` and `CategoryNames`; `CategoryNames["editor"] = "Writing"`; `IsGated` becomes `key is "data" or "fonts"`; replace every other reference to `CanvasPanel`/`BulletsPanel` with `LayoutPanel`/`EditorPanel` (for visibility checks) and to the keys "canvas"/"bullets" with "layout"/"editor".

- [ ] Apply with a binary-mode Python script that locates blocks by their heading text and asserts each anchor is found once.
- [ ] Build: `dotnet build src/Lumenotepad -c Release`, 0 warnings. Run all tests.
- [ ] **Commit** "Move Preferences settings to the pages they belong on".

---

### Task 3: Preferences tabs and search headings

**Files:** Modify `src/Lumenotepad/Views/PreferencesWindow.axaml`, `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`

**Interfaces:** Consumes `PrefsTabs` (Task 1).

- [ ] **Step 1: Strip in markup.** Replace the `PageTitle` TextBlock row with:

```xml
<StackPanel Grid.Row="0" Margin="20,6,20,6" Spacing="8">
    <TextBlock x:Name="PageTitle" FontSize="21" FontWeight="SemiBold"
               Foreground="{DynamicResource TextPrimaryBrush}"/>
    <StackPanel x:Name="PageTabs" Orientation="Horizontal" Spacing="18" IsVisible="False"/>
</StackPanel>
```

and add styles:

```xml
<Style Selector="Border.prefstab">
    <Setter Property="Padding" Value="0,2,0,6"/>
    <Setter Property="BorderThickness" Value="0,0,0,2"/>
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Cursor" Value="Hand"/>
</Style>
<Style Selector="Border.prefstab > TextBlock">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
</Style>
<Style Selector="Border.prefstab:pointerover > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
</Style>
<Style Selector="Border.prefstab.current">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
</Style>
<Style Selector="Border.prefstab.current > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
</Style>
```

- [ ] **Step 2: Headings.** Replace `IsSectionHeader` and add a heading reader:

```csharp
private static bool IsSectionHeader(Control c) => HeadingOf(c) is not null;

private static string? HeadingOf(Control c) => c switch
{
    TextBlock tb when tb.Classes.Contains("section") => tb.Text,
    StackPanel { Orientation: Orientation.Horizontal } sp when sp.Children.Count > 0
        && sp.Children[0] is TextBlock t0 && t0.Classes.Contains("section") => t0.Text,
    _ => null,
};
```

- [ ] **Step 3: Tab bookkeeping.** Fields and helpers:

```csharp
private readonly Dictionary<Border, string> _cardTab = new();
private readonly Dictionary<string, string> _currentTab = new();

private IEnumerable<Border> CardsOf(string key) =>
    _panels.TryGetValue(key, out var p) && p is Panel panel
        ? panel.Children.OfType<Border>().Where(b => Equals(b.Tag, CardTag))
        : Enumerable.Empty<Border>();

private void IndexTabs()
{
    foreach (var (key, _) in _panels)
        foreach (var card in CardsOf(key))
            if (card.Child is StackPanel sp && sp.Children.Count > 0 && HeadingOf(sp.Children[0]) is { } h
                && PrefsTabs.TabOf(key, h) is { } tab)
                _cardTab[card] = tab;
}

private void ApplyTab(string key, bool animate)
{
    PageTabs.Children.Clear();
    if (PrefsTabs.For(key) is not { } tabs || _searching) { PageTabs.IsVisible = false; return; }
    string current = _currentTab.GetValueOrDefault(key) ?? tabs[0].Name;
    _currentTab[key] = current;
    PageTabs.IsVisible = true;
    foreach (var t in tabs)
    {
        var tab = new Border { Child = new TextBlock { Text = t.Name } };
        tab.Classes.Add("prefstab");
        if (t.Name == current) tab.Classes.Add("current");
        string name = t.Name;
        tab.PointerReleased += (_, _) => { if (_currentTab.GetValueOrDefault(key) != name) { _currentTab[key] = name; ApplyTab(key, true); } };
        PageTabs.Children.Add(tab);
    }
    int i = 0;
    foreach (var card in CardsOf(key))
    {
        bool show = !_cardTab.TryGetValue(card, out var t) || t == current;
        card.IsVisible = show;
        if (show && animate)
        {
            var c = card;
            int delay = 28 * i++;
            if (delay == 0) Motion.RiseIn(c, Motion.Fast);
            else { c.Opacity = 0; DispatcherTimer.RunOnce(() => Motion.RiseIn(c, Motion.Fast), TimeSpan.FromMilliseconds(delay)); }
        }
    }
}
```

In `ShowPanel` add `ApplyTab(key, animate: false);` after setting the title. In the constructor call `IndexTabs();` right after `GroupIntoCards();`.

- [ ] **Step 4: Search headings.** Replace `SetupSettingsSearch` so tabbed pages get one hidden `searchcat` heading per tab, inserted before that tab's first card, reading "Page › Tab"; untabbed pages keep one heading at index 0. Replace `_searchIndex` with `List<(string Key, Panel Panel)>` and rewrite `FilterPanel(Panel panel, string q)` to walk the children in order: a `searchcat` TextBlock starts a group; cards and loose rows count hits into the current group; at each new group and at the end, set the previous heading's visibility to `hits > 0`. The panel is visible when any group has hits. In `ApplySearch`, hide `PageTabs` while searching, and when the query empties call `ShowPanel(curKey)` (which re-applies the tab).

- [ ] **Step 5:** Build, run all tests. **Commit** "Tabs inside the long Preferences pages, and search by page and tab".

---

### Task 4: Layout setting

**Files:** Modify `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`; Test `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Tests** (append):

```csharp
    [Fact]
    public void UiLayout_defaultsToClassic_andRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal("Classic", new AppSettings().UiLayout);
            new AppSettings { UiLayout = "Focus" }.Save(dir);
            Assert.Equal("Focus", AppSettings.Load(dir).UiLayout);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

```csharp
    [Fact]
    public void UiLayout_persists_andResetRestoresClassic()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.UiLayout = "Tabs";
            Assert.Equal("Tabs", AppSettings.Load(dir).UiLayout);
            vm.ResetSettingsToDefaults();
            Assert.Equal("Classic", vm.UiLayout);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Implement:** `public string UiLayout { get; set; } = "Classic";` next to `ButtonPlacement`; `[ObservableProperty] private string _uiLayout = "Classic";` with load, `OnUiLayoutChanged` save, and reset lines in the `ButtonPlacement` pattern.
- [ ] Run tests. **Commit** "Add the layout setting".

---

### Task 5: Sidebar rows

**Files:** Create `src/Lumenotepad/Models/NavRows.cs`; Test `tests/Lumenotepad.Tests/NavRowsTests.cs`

**Interfaces:** Produces `record SectionRow(Section Section, bool Folded)` with `IndentWidth` (0) and `IsFoldedAway` (false); `NavRows.Build(Notebook?, bool singleMode, ISet<string> foldedSectionIds) -> List<object>`; `NavRows.SectionOf(Notebook, Page) -> Section?`.

- [ ] **Tests:**

```csharp
using System.Collections.Generic;
using System.Linq;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class NavRowsTests
{
    private static Notebook Nb()
    {
        var nb = new Notebook { Name = "Bio" };
        var a = new Section { Name = "Notes" };
        a.Pages.Add(new Page { Title = "P1" });
        a.Pages.Add(new Page { Title = "P1a", Level = 1 });
        var b = new Section { Name = "Lab" };
        b.Pages.Add(new Page { Title = "L1" });
        nb.Sections.Add(a);
        nb.Sections.Add(b);
        return nb;
    }

    private static string Shape(List<object> rows) => string.Join(" ", rows.Select(r => r switch
    {
        SectionRow s => (s.Folded ? "+" : "-") + s.Section.Name,
        Page p => p.Title,
        _ => "?",
    }));

    [Fact]
    public void SectionsThenTheirPages() =>
        Assert.Equal("-Notes P1 P1a -Lab L1", Shape(NavRows.Build(Nb(), false, new HashSet<string>())));

    [Fact]
    public void FoldedSectionsHideTheirPages()
    {
        var nb = Nb();
        Assert.Equal("+Notes -Lab L1", Shape(NavRows.Build(nb, false, new HashSet<string> { nb.Sections[0].Id })));
    }

    [Fact]
    public void SingleModeListsPagesOnly() =>
        Assert.Equal("P1 P1a L1", Shape(NavRows.Build(Nb(), true, new HashSet<string>())));

    [Fact]
    public void BuildRefreshesThreadFlags_andEmptyIsEmpty()
    {
        var nb = Nb();
        NavRows.Build(nb, false, new HashSet<string>());
        Assert.True(nb.Sections[0].Pages[0].HasSubPages);
        Assert.Empty(NavRows.Build(null, false, new HashSet<string>()));
        Assert.Same(nb.Sections[1], NavRows.SectionOf(nb, nb.Sections[1].Pages[0]));
    }
}
```

- [ ] **Implement:**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Lumenotepad.Models;

public sealed record SectionRow(Section Section, bool Folded)
{
    public double IndentWidth => 0;
    public bool IsFoldedAway => false;
}

public static class NavRows
{
    public static List<object> Build(Notebook? nb, bool singleMode, ISet<string> foldedSectionIds)
    {
        var rows = new List<object>();
        if (nb is null) return rows;
        foreach (var s in nb.Sections)
        {
            PageTree.Refresh(s.Pages);
            bool folded = !singleMode && foldedSectionIds.Contains(s.Id);
            if (!singleMode) rows.Add(new SectionRow(s, folded));
            if (!folded) rows.AddRange(s.Pages);
        }
        return rows;
    }

    public static Section? SectionOf(Notebook nb, Page page) => nb.Sections.FirstOrDefault(s => s.Pages.Contains(page));
}
```

- [ ] Run tests. **Commit** "Build the one-sidebar outline rows".

---

### Task 6: Simple toolbar

**Files:** Modify `src/Lumenotepad/Views/FormatToolbar.axaml`, `src/Lumenotepad/Views/FormatToolbar.axaml.cs`

**Interfaces:** Produces `FormatToolbar.SetSimple(bool simple)`.

- [ ] **Markup:** at the end of the `Panel` WrapPanel add
`<ToggleButton x:Name="MoreBtn" Classes="toolmore" Height="30" Padding="10,0" FontSize="12.5" Content="More" IsVisible="False" ToolTip.Tip="Show more formatting tools"/>` with a style giving it the icon-button look (transparent background, `ControlHoverBrush` on pointerover, `AccentSoftBrush` background and accent foreground when checked, corner radius 8).
- [ ] **Code:**

```csharp
private bool _simple;

public void SetSimple(bool simple)
{
    _simple = simple;
    MoreBtn.IsVisible = simple;
    if (!simple) MoreBtn.IsChecked = false;
    ApplySimple();
}

private Control[] Extras => new Control[]
{
    StrikeBtn, SuperBtn, SubBtn, TypeBtn, AlignBtn, InsertBtn, TableBtn, TagBtn, CustomizeBtn, DockBtn,
};

private void ApplySimple()
{
    bool showExtras = !_simple || MoreBtn.IsChecked == true;
    foreach (var c in Extras) c.IsVisible = showExtras;
    var kids = Panel.Children.OfType<Control>().ToList();
    for (int i = 0; i < kids.Count; i++)
    {
        if (kids[i] is not Border sep || !sep.Classes.Contains("toolsep")) continue;
        bool before = false, after = false;
        for (int j = i - 1; j >= 0 && !(kids[j] is Border b1 && b1.Classes.Contains("toolsep")); j--)
            if (kids[j].IsVisible && !ReferenceEquals(kids[j], MoreBtn)) before = true;
        for (int j = i + 1; j < kids.Count && !(kids[j] is Border b2 && b2.Classes.Contains("toolsep")); j++)
            if (kids[j].IsVisible && !ReferenceEquals(kids[j], MoreBtn)) after = true;
        sep.IsVisible = before && after;
    }
}
```

Wire `MoreBtn.IsCheckedChanged += (_, _) => ApplySimple();` in the constructor. `SetCompact` (PDF viewer) is untouched.

- [ ] Build. **Commit** "A simple toolbar with a More button".

---

### Task 7: The layouts in the main window

**Files:** Modify `src/Lumenotepad/Views/MainView.axaml`, `src/Lumenotepad/Views/MainView.axaml.cs`, `src/Lumenotepad/Views/PreferencesWindow.axaml`, `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`; Create `src/Lumenotepad/Views/MainView.Layouts.cs`

**Interfaces:** Consumes Tasks 4 to 6. Produces `MainView.ApplyUiLayout()`, `MainView.NotebookMenu(Control anchor)`.

- [ ] **Step 1: Shared page row template.** Move the `PagesList` item template into `UserControl.Resources` as `<DataTemplate x:Key="PageRow" x:DataType="models:Page">` (content unchanged) and set `ItemTemplate="{StaticResource PageRow}"` on `PagesList`.
- [ ] **Step 2: New markup.**
  - Body grid columns become `Auto,Auto,Auto,*,Auto`.
  - Column 0, after `RailPanel`: `OutlinePanel`, a Border (hidden, frame background, right border), holding a DockPanel with:
    - top: `SidebarNotebookBtn`, a Button showing a 14px color chip bound to `SelectedNotebook.Color` and the notebook name, plus a chevron;
    - bottom: `OutlineNewPageBtn` "New page" and `OutlineNewSectionBtn` "New section" (text buttons);
    - fill: `OutlineList`, a ListBox with `Classes="pages"` and two DataTemplates: `models:Page` using the same content as `PageRow`, and `models:SectionRow` as a full-width Button (`Classes="sectionrow"`, `Click="OnSectionRowClick"`) with the section name in 11px semibold muted uppercase plus a fold chevron (`E70D`, rotated -90 when folded).
  - Column 4: `TabsPagesPanel`, a Border (hidden, frame background, left border), holding a DockPanel with a top row (a "Pages" label and `TabsNewPageBtn` "+") and `TabsPagesList`: `Classes="pages"`, ItemsSource `SelectedSection.Pages`, SelectedItem `SelectedPage` TwoWay, `ItemTemplate="{StaticResource PageRow}"`.
  - Canvas column: wrap `PageBoxSurface` in `<DockPanel x:Name="CanvasColumnDock">`. Before it, add `LayoutTopBar`, a Border docked top with margin `14,10,14,0`, hidden, holding a Panel with:
    - `SectionTabsBar`: a ListBox, `Classes="sectiontabs"`, horizontal StackPanel items, ItemsSource `SelectedNotebook.Sections`, SelectedItem `SelectedSection` TwoWay, item template the section name, followed by `SectionTabsAddBtn` "+";
    - `FocusPath`: a horizontal StackPanel of `FocusNotebookBtn`, a "›" TextBlock, `FocusSectionBtn`, a second "›" (`FocusSectionSep`) and `FocusPageBtn`. Each is a text button with a chevron, bound to `SelectedNotebook.Name`, `SelectedSection.Name` and `SelectedPage.Title`.
  - Title bar: `TitleNotebookBtn`, the same look as `SidebarNotebookBtn`, declared inside `TitleRightHost` so it has a parent; `ApplyButtonPlacement` moves it.
  - Styles: `ListBox.sectiontabs ListBoxItem` with padding `12,6`, corner radius 8 and margin `0,0,4,0`; selected uses the existing accent pill look. `Button.sectionrow` is transparent, left-aligned, padding `4,8,4,2`.
- [ ] **Step 3: `MainView.Layouts.cs`** (partial class) with:

```csharp
private readonly HashSet<string> _foldedSections = new();
private bool _outlineSyncing;
private string Layout => Vm?.UiLayout ?? "Classic";
private bool IsClassic => Layout == "Classic";

private void ApplyUiLayout(bool animate = false)
{
    if (Vm is not { } vm) return;
    string l = Layout;
    bool classic = l == "Classic";
    RailPanel.IsVisible = classic;
    PagesPanel.IsVisible = classic;
    if (!classic) SectionsSidebar.IsVisible = false; else ApplySectionsSidebar();
    OutlinePanel.IsVisible = l == "Sidebar";
    TabsPagesPanel.IsVisible = l == "Tabs";
    OutlinePanel.Width = vm.IsPagesVisible ? vm.PagesPanelWidth : 0;
    OutlinePanel.Opacity = vm.IsPagesVisible ? 1 : 0;
    TabsPagesPanel.Width = vm.IsPagesVisible ? vm.PagesPanelWidth : 0;
    TabsPagesPanel.Opacity = vm.IsPagesVisible ? 1 : 0;
    SectionTabsBar.IsVisible = l == "Tabs" && !vm.SingleMode;
    SectionTabsAddBtn.IsVisible = SectionTabsBar.IsVisible;
    FocusPath.IsVisible = l == "Focus";
    FocusSectionBtn.IsVisible = FocusSectionSep.IsVisible = !vm.SingleMode;
    LayoutTopBar.IsVisible = SectionTabsBar.IsVisible || FocusPath.IsVisible;
    PageBoxSurface.Margin = LayoutTopBar.IsVisible ? new Thickness(14, 8, 14, 14) : new Thickness(14);
    Toolbar.SetSimple(!classic);
    ApplyFocusToolbar();
    PanelBubble.HorizontalAlignment = l == "Tabs" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    PanelBubble.Margin = l == "Tabs" ? new Thickness(0, 0, 26, 46) : new Thickness(26, 0, 0, 46);
    if (l == "Sidebar") RebuildOutline();
    ApplyButtonPlacement();
    if (animate) Motion.FadeIn(BodyDock, Motion.Base);
}

private void ApplyFocusToolbar()
{
    bool focus = Layout == "Focus";
    bool show = !focus || PageCanvas.ActiveEditor is not null;
    Toolbar.IsHitTestVisible = show;
    if (show && Toolbar.Opacity < 1) Motion.FadeIn(Toolbar, Motion.Fast);
    else if (!show && Toolbar.Opacity > 0) Motion.FadeOut(Toolbar, Motion.Fast);
}

private void RebuildOutline()
{
    if (Vm is not { } vm || Layout != "Sidebar") return;
    _outlineSyncing = true;
    OutlineList.ItemsSource = NavRows.Build(vm.SelectedNotebook, vm.SingleMode, _foldedSections);
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

private void NotebookMenu(Control anchor)
{
    if (Vm is not { } vm) return;
    var menu = new ContextMenu();
    foreach (var nb in vm.Notebooks)
    {
        var item = new MenuItem { Header = string.IsNullOrWhiteSpace(nb.Name) ? "Untitled notebook" : nb.Name, Icon = Swatch(nb.Color) };
        var target = nb;
        item.Click += (_, _) => vm.OpenNotebookCommand.Execute(target);
        if (ReferenceEquals(nb, vm.SelectedNotebook)) item.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(item);
    }
    menu.Items.Add(new Separator());
    menu.Items.Add(Act("All notebooks", () => vm.GoHomeCommand.Execute(null)));
    menu.Items.Add(Act("New notebook", () => OpenNotebookWizard()));
    MenuFx.Attach(menu);
    menu.Open(anchor);
}

private void OpenListMenu(Control anchor, IEnumerable<Control> items)
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
        var item = Act(new string(' ', p.Level * 4) + title, () => vm.SelectedPage = target);
        if (ReferenceEquals(p, vm.SelectedPage)) item.FontWeight = FontWeight.SemiBold;
        yield return item;
    }
    yield return new Separator();
    yield return Act("New page", () => vm.AddPageCommand.Execute(null));
    if (vm.SelectedPage is { } cur && PageTree.CanAddSubPage(sec.Pages, sec.Pages.IndexOf(cur)))
        yield return Act("New sub-page", () => vm.NewSubPage(cur));
}

private void WireLayouts()
{
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
```

`Swatch(hex)` already exists in `MainView` (used by the colour menus); if its signature differs, adapt the call.
- [ ] **Step 4: Hook up.** In the `MainView` constructor call `WireLayouts();`. In `HookVm` call `ApplyUiLayout();` after `ApplyButtonPlacement();`. In `OnVmPropertyChanged`:
  - `UiLayout` → `ApplyUiLayout(animate: true)`;
  - `SingleMode` → also `ApplyUiLayout()`;
  - `SelectedNotebook`, `SelectedSection`, `SelectedPage` → `RebuildOutline()` (the selection part only needs `OutlineList.SelectedItem = vm.SelectedPage` under `_outlineSyncing`);
  - `IsPagesVisible` → when `IsClassic` keep the existing `Motion.Reveal(PagesPanel, ...)`, otherwise `Motion.Reveal(Layout == "Sidebar" ? OutlinePanel : TabsPagesPanel, vm.PagesPanelWidth, vm.IsPagesVisible)`; then `ApplyButtonPlacement()`.

  Also:
  - `ApplySectionsSidebar` returns early (after hiding `SectionsSidebar`) when `!IsClassic`.
  - Rebuild the outline when any section's `Pages` collection or the notebook's `Sections` collection changes: subscribe in `RebuildOutline` and keep the subscriptions in a list to detach on the next rebuild.
  - Page renames update through bindings.
- [ ] **Step 5: Buttons.** In `ApplyButtonPlacement`, before the Classic switch, handle non-Classic:

```csharp
if (!IsClassic)
{
    foreach (var b in new Control[] { HomeBtn, RailToggle, PagesToggle, PrefsBtn, TitleNotebookBtn })
        (b.Parent as Panel)?.Children.Remove(b);
    TitleNavHost.Children.Add(HomeBtn);
    if (Layout == "Tabs") TitleNavHost.Children.Add(TitleNotebookBtn);
    if (Layout != "Focus") TitleNavHost.Children.Add(PagesToggle);
    TitleRightHost.Children.Add(PrefsBtn);
    ToolTip.SetTip(PagesToggle, Layout == "Sidebar" ? "Show / hide sidebar" : "Show / hide pages");
    ToolTip.SetTip(BubblePagesBtn, Layout == "Sidebar" ? "Show sidebar" : "Show pages");
    foreach (var b in new[] { HomeBtn, PagesToggle, PrefsBtn }) { b.Width = b.Height = 34; b.FontSize = 15; }
    RailTopDivider.IsVisible = RailBottomDivider.IsVisible = false;
    BubbleRailBtn.IsVisible = false;
    BubblePagesBtn.IsVisible = Layout != "Focus" && !vm.IsPagesVisible;
    BubbleDivider.IsVisible = false;
    ShowBubble(BubblePagesBtn.IsVisible);
    return;
}
```

Move the existing bubble show/hide block into `private void ShowBubble(bool show)` and call it from the Classic path too. For Classic, put `TitleNotebookBtn` back into `TitleRightHost` with `IsVisible = false` (it has no binding), and set `ToolTip.SetTip(PagesToggle, "Show / hide pages")` and `ToolTip.SetTip(BubblePagesBtn, "Show pages")`.
- [ ] **Step 6: Preferences LAYOUT card.** The `LAYOUT` block (first in `LayoutPanel`): a heading with help text "How the window is arranged. Classic is the original; the others are simpler ways to get around the same notebooks." and a `ListBox x:Name="LayoutTiles"` (WrapPanel, `Classes="layouttiles"`). It holds four `ListBoxItem`s tagged "Classic", "Sidebar", "Tabs" and "Focus". Each item is a vertical StackPanel: a 104×64 `Border` drawing the layout in muted strokes (Classic: three thin columns and a page; One sidebar: one column and a page; Tabs: a page with a tab row and a right column; Focus: a page with a thin path bar), then the name ("Classic", "One sidebar", "Tabs", "Focus"). `SelectionChanged` sets `vm.UiLayout` to the tag; `SyncFromVm` selects the item matching `vm.UiLayout`. Name the Classic-only rows `ButtonPlacementRow`, `ShowRailRow` and `SectionsSidebarRow`. Add `SyncLayoutRows()`, which sets their `IsVisible` to `vm.UiLayout == "Classic"` (and records the value in `_origVisible` if present, as `SyncThemeRows` does). Call it from `SyncFromVm` and whenever `UiLayout` changes (subscribe to the view model's `PropertyChanged`).
- [ ] Build, run all tests. **Commit** "Four layouts: Classic, One sidebar, Tabs and Focus".

---

### Task 8: Probes, review, fixes

- [ ] **Preferences probe** (temporary `tools/CardRepro/Program.cs`, restore afterwards):
  1. Open `PreferencesWindow` over a fresh view model.
  2. For every page and every tab, select it and check that at least one card is visible and every visible card has a visible row; save a PNG.
  3. For every label TextBlock (`Classes="label"`) in the window, type its text into `SearchBox` and check that the row containing it is visible, and that the nearest visible `searchcat` heading above it reads "Page › Tab" for tabbed pages.
  4. Click each Layout tile and check that `vm.UiLayout` follows.
- [ ] **Layouts probe:** host `MainView` at 1280×800. For each layout:
  1. Set `vm.UiLayout`.
  2. Select the second page through that layout's navigator (outline row click, pages list click, or the page menu item).
  3. Add a page and a sub-page.
  4. Raise `ContextRequested` on the navigator and check a menu opened (`Handled`).
  5. Toggle `IsPagesVisible` twice and check the bubble.
  6. Check `Toolbar` More visibility matches the layout.
  7. For Focus, check the toolbar is hidden before clicking a text box and shown after.
  8. Save PNGs in Lumen, Dark and Light.
- [ ] Re-run the saved 29-check round probe (Classic).
- [ ] Look at every PNG; fix what looks wrong; commit fixes.

---

### Task 9: Release 1.4.0

- [ ] `<Version>1.4.0</Version>`. Write `docs/release-notes-1.4.0.md` with a summary line, the Preferences changes, the four layouts, and the standard notes footer.
- [ ] Build with 0 warnings and the full suite green. Merge to master with `--no-ff`. Publish by the recorded flow (Windows, macOS, manifest, installer in the background, push, both `gh release create` with the full SHA). Verify the live manifest, all seven URLs and the published mac hash.
