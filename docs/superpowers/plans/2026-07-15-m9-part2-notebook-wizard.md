# M9 Part 2 — Notebook Creation Wizard

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "New notebook" opens a themed two-step wizard (Step 1: name, color, cover, sections;
Step 2: grid + page style with live previews, apply mode, default font/size, pages per section).
Cancel creates nothing; Create builds the whole tree — styled pages stamped — and opens it.

**Architecture:** A pure `NotebookDraft` model that the wizard edits and a TDD'd
`MainViewModel.CreateNotebook(draft)` that turns it into the real tree (save → cover → stamp →
select). The window (`NotebookWizardWindow`) copies the PreferencesWindow shell language (chromeless,
drag bar, resize border, Motion in/out) and swaps two step panels. Page-style previews are tiny live
`GuideLayer` instances. Notebook default font/size finally get CONSUMED: `ApplyEditorPrefs` prefers
the selected notebook's defaults over the globals. Edit mode is Part 3 — the window takes a mode flag
now, but only create-mode is wired.

**Tech Stack:** Avalonia 12.0.4, .NET 10, xUnit. Suite baseline: 175 green.

**Verified context (do not re-derive):**
- `Notebook` has `DefaultGridStyle`(null)/`DefaultPageStyle`("Freeform")/`DefaultPageStyleMode`(0)/
  `DefaultFont`(null)/`DefaultFontSize`(15) — persisted, currently unconsumed. `PageStyles`,
  `PageStyleTemplate`, `GuideLayer` (public `Viewport`, `SetStyles(grid, style, mode)`, `Refresh()`),
  `MainViewModel.StampPageStyle(page)` + `CanvasViewport` exist (M9-1).
- VM: `AddNotebook()` instant-creates (KEEP the command — tests use it; the UI stops calling it).
  `SetNotebookCover(nb, path)` copies+persists a cover (needs `nb.Folder` assigned → `Save()` first).
  `Save()` assigns folders. `SelectedNotebook` cascade + `IsHomeVisible=false` opens a notebook.
- `CoverCropDialog.Show(Window owner, string imagePath)` → `Task<string?>` (temp cropped file; the
  caller deletes it after use — see `PickCover` in MainView.axaml.cs for the exact pattern).
- `MainViewModel.NotebookColors` (6 chips) + `NotebookPalette` (9 families × 5 shades);
  the 45-shade flyout pattern lives in `PreferencesWindow.BuildBulletColorFlyout`.
- New-notebook UI call sites: `MainView.axaml` line ~253 (`NewNotebookBtn`, home page) and ~404 (the
  rail's + button, unnamed, `Command="{Binding AddNotebookCommand}"`).
- Window shell patterns to copy: `PreferencesWindow.axaml(.cs)` — `WindowDecorations="None"`,
  `controls:WindowResizeBorder` overlay, title-bar `BeginMoveDrag`, `Opened → WinChrome.RoundCorners
  + Motion.ScaleIn(root, 0.96, 180)`, `Closing`-intercept CollapseOut, Escape closes,
  `Background="{DynamicResource WindowBackgroundBrush}"`, section/label/hint TextBlock styles.
- `AppFonts.ListNames(bool extended)` = font candidates; prefs' `RefreshEditorFontList` shows the
  "(Default)" + names combo idiom.
- Editor font consumption today: `MainView.ApplyEditorPrefs` pushes `vm.EditorFont`/`vm.EditorFontSize`
  into `RichTextEditor.EditorFontPref/EditorFontSizePref`; note containers read them at construction;
  `PageCanvas.Document = PageCanvas.Document` rebuilds.
- Build gotchas: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test;
  `cd /e/CLAUDE/Lumenotepad` per Bash call; NEVER launch the GUI from a subagent. Direct commits to
  master. Popup/menu content CornerRadius stays 8 (PF3 lesson) if any is added.

---

### Task 1: NotebookDraft + CreateNotebook (TDD)

**Files:**
- Create: `src/Lumenotepad/ViewModels/NotebookDraft.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Failing tests** — append to MainViewModelTests:

```csharp
    [Fact]
    public void CreateNotebook_buildsTree_stampsStyledPages_andOpens()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var draft = NotebookDraft.New();
            draft.Name = "Biology";
            draft.Color = "#3E9C6B";
            draft.DefaultPageStyle = "Cornell";
            draft.DefaultFont = "Caveat";
            draft.DefaultFontSize = 18;
            draft.Sections.Clear();
            draft.Sections.Add(new SectionDraft { Name = "Cells", PageTitles = { "Structure", "Mitosis" } });
            draft.Sections.Add(new SectionDraft { Name = "Genetics", PageTitles = { "Mendel" } });

            var nb = vm.CreateNotebook(draft);

            Assert.Equal("Biology", nb.Name);
            Assert.Equal("#3E9C6B", nb.Color);
            Assert.Equal("Cornell", nb.DefaultPageStyle);
            Assert.Equal("Caveat", nb.DefaultFont);
            Assert.Equal(18, nb.DefaultFontSize);
            Assert.Equal(2, nb.Sections.Count);
            Assert.Equal(new[] { "Structure", "Mitosis" }, nb.Sections[0].Pages.Select(p => p.Title).ToArray());
            Assert.Single(nb.Sections[1].Pages);
            Assert.Equal(3, vm.DocumentFor(nb.Sections[0].Pages[0]).Boxes.Count);   // Cornell starters
            Assert.Same(nb, vm.SelectedNotebook);
            Assert.False(vm.IsHomeVisible);

            var reloaded = new MainViewModel(new WorkspaceStore(dir), dir);         // persisted
            var rnb = reloaded.Notebooks.First(n => n.Name == "Biology");
            Assert.Equal("Cornell", rnb.DefaultPageStyle);
            Assert.Equal(2, rnb.Sections.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CreateNotebook_guardsBlanks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var draft = NotebookDraft.New();
            draft.Name = "  ";
            draft.Sections.Clear();                                     // no sections at all

            var nb = vm.CreateNotebook(draft);

            Assert.Equal("New notebook", nb.Name);
            var sec = Assert.Single(nb.Sections);                       // seeded fallback section
            Assert.Equal("Notes", sec.Name);
            Assert.Empty(vm.DocumentFor(sec.Pages[0]).Boxes);           // Freeform default: no starters
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run filter — compile-fail.** `dotnet test --filter "CreateNotebook" …`

- [ ] **Step 3: Implement**

Create `src/Lumenotepad/ViewModels/NotebookDraft.cs`:

```csharp
using System.Collections.Generic;

namespace Lumenotepad.ViewModels;

/// <summary>Everything the notebook wizard collects before anything real is created — plain data,
/// so Cancel is free and CreateNotebook is unit-testable. One draft = one notebook.</summary>
public sealed class NotebookDraft
{
    public string Name = "";
    public string Color = MainViewModel.NotebookColors[0].Hex;
    /// <summary>A CROPPED temp image path (CoverCropDialog output) — consumed then deleted.</summary>
    public string? CoverSourcePath;
    public List<SectionDraft> Sections { get; } = new();

    public string? DefaultGridStyle;                 // null = inherit the global grid pref
    public string DefaultPageStyle = Editor.PageStyles.Freeform;
    public int DefaultPageStyleMode = Editor.PageStyles.ModeGuides;
    public string? DefaultFont;                      // null = the app default
    public double DefaultFontSize = 15;

    /// <summary>A fresh draft: one "Notes" section holding one "Untitled page".</summary>
    public static NotebookDraft New()
    {
        var d = new NotebookDraft();
        d.Sections.Add(new SectionDraft { Name = "Notes", PageTitles = { "Untitled page" } });
        return d;
    }
}

/// <summary>One planned section: a name and its planned page titles (0+ allowed).</summary>
public sealed class SectionDraft
{
    public string Name = "";
    public List<string> PageTitles { get; } = new();
}
```

In `MainViewModel`, after `CreateNotebook`'s natural neighbors (`AddNotebook`), add:

```csharp
    /// <summary>Materialize a wizard draft: build the tree, persist (folder assignment first — the
    /// cover copy and page docs need it), apply the cover, stamp each page's starter template per
    /// the notebook defaults, and open the notebook. Blank names fall back like AddNotebook's.</summary>
    public Notebook CreateNotebook(NotebookDraft draft)
    {
        var nb = new Notebook
        {
            Name = string.IsNullOrWhiteSpace(draft.Name) ? "New notebook" : draft.Name.Trim(),
            Color = draft.Color,
            DefaultGridStyle = draft.DefaultGridStyle,
            DefaultPageStyle = draft.DefaultPageStyle,
            DefaultPageStyleMode = draft.DefaultPageStyleMode,
            DefaultFont = draft.DefaultFont,
            DefaultFontSize = draft.DefaultFontSize,
        };
        if (draft.Sections.Count == 0)
            draft.Sections.Add(new SectionDraft { Name = "Notes", PageTitles = { "Untitled page" } });
        foreach (var sd in draft.Sections)
        {
            var sec = new Section { Name = string.IsNullOrWhiteSpace(sd.Name) ? "Section" : sd.Name.Trim() };
            foreach (var title in sd.PageTitles)
                sec.Pages.Add(new Page { Title = string.IsNullOrWhiteSpace(title) ? "Untitled page" : title.Trim() });
            if (sec.Pages.Count == 0 && draft.Sections.Count == 1 && sd.PageTitles.Count == 0)
                sec.Pages.Add(new Page { Title = "Untitled page" });     // never open into a pageless notebook
            nb.Sections.Add(sec);
        }
        Notebooks.Add(nb);
        Save();                                                          // assigns nb.Folder
        if (draft.CoverSourcePath is { } cover) SetNotebookCover(nb, cover);
        foreach (var page in nb.Sections.SelectMany(s => s.Pages))
            StampPageStyle(page);                                        // no-op for Freeform
        SelectedNotebook = nb;
        IsHomeVisible = false;
        return nb;
    }
```

(NOTE the pageless guard: only the single-section-zero-pages case seeds a page, so multi-section
drafts keep intentionally page-free sections; `StampPageStyle` resolves owner via `FindOwner` —
pages are attached before stamping, so it works.)

- [ ] **Step 4: Full suite green.** - [ ] **Step 5: Commit** `feat(m9): NotebookDraft + CreateNotebook — the wizard's testable core`

---

### Task 2: Wizard window — shell + Step 1

**Files:**
- Create: `src/Lumenotepad/Views/NotebookWizardWindow.axaml`
- Create: `src/Lumenotepad/Views/NotebookWizardWindow.axaml.cs`

No new unit tests (window UI); suite stays green. The window must BUILD complete with both step
panels' containers, but only Step 1 is populated in this task (Step 2 controls land in Task 3 —
leave `Step2Panel` an empty StackPanel with a placeholder comment).

- [ ] **Step 1: XAML** — `NotebookWizardWindow.axaml`, copying the Preferences shell language:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:Lumenotepad.Controls"
        x:Class="Lumenotepad.Views.NotebookWizardWindow"
        Title="New notebook"
        Width="720" Height="560" MinWidth="600" MinHeight="480"
        WindowDecorations="None" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        FontFamily="{StaticResource UiFont}"
        Foreground="{DynamicResource TextPrimaryBrush}"
        TextOptions.TextRenderingMode="Antialias"
        Background="{DynamicResource WindowBackgroundBrush}">

    <Window.Styles>
        <Style Selector="TextBlock.section">
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="Margin" Value="0,14,0,4"/>
        </Style>
        <Style Selector="TextBlock.hint">
            <Setter Property="FontSize" Value="11.5"/>
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
            <Setter Property="TextWrapping" Value="Wrap"/>
        </Style>
        <Style Selector="TextBlock.label">
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
    </Window.Styles>

    <Grid>
    <DockPanel>
        <Grid DockPanel.Dock="Top" Height="36" ColumnDefinitions="*,Auto" x:Name="WizTitleBar" Background="Transparent">
            <TextBlock x:Name="WizTitle" Text="New notebook" FontSize="13" FontWeight="SemiBold"
                       VerticalAlignment="Center" Margin="16,0,0,0"/>
            <Button x:Name="CloseBtn" Grid.Column="1" Theme="{StaticResource CloseCaptionButton}" Content="&#xE8BB;"/>
        </Grid>

        <!-- footer: step indicator left, actions right -->
        <Grid DockPanel.Dock="Bottom" ColumnDefinitions="*,Auto" Margin="20,10,20,16">
            <TextBlock x:Name="StepLabel" Classes="hint" VerticalAlignment="Center" Text="Step 1 of 2 — Notebook"/>
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                <Button x:Name="CancelBtn" Content="Cancel" FontSize="13" Padding="14,7"/>
                <Button x:Name="BackBtn" Content="Back" FontSize="13" Padding="14,7" IsVisible="False"/>
                <Button x:Name="NextBtn" Theme="{StaticResource LumenButton}" Content="Next" FontSize="13"/>
                <Button x:Name="CreateBtn" Theme="{StaticResource LumenButton}" Content="Create" FontSize="13" IsVisible="False"/>
            </StackPanel>
        </Grid>

        <ScrollViewer x:Name="WizScroll" HorizontalScrollBarVisibility="Disabled">
            <Panel Margin="20,4,20,8">

                <StackPanel x:Name="Step1Panel" Spacing="6">
                    <TextBlock Classes="section" Text="NAME" Margin="0,4,0,4"/>
                    <TextBox x:Name="NameBox" Theme="{StaticResource RoundedFieldTextBox}" FontSize="14"
                             PlaceholderText="My notebook"/>

                    <TextBlock Classes="section" Text="COLOR"/>
                    <WrapPanel x:Name="ColorSwatches"/>

                    <TextBlock Classes="section" Text="COVER IMAGE"/>
                    <StackPanel Orientation="Horizontal" Spacing="10">
                        <Border x:Name="CoverPreview" Width="96" Height="64" CornerRadius="10"
                                BorderBrush="{DynamicResource FrameBorderBrush}" BorderThickness="1"/>
                        <StackPanel Spacing="6" VerticalAlignment="Center">
                            <Button x:Name="CoverPickBtn" Content="Choose image…" FontSize="12.5"/>
                            <Button x:Name="CoverClearBtn" Content="No cover" FontSize="12.5"/>
                        </StackPanel>
                    </StackPanel>
                    <TextBlock Classes="hint" Text="Without an image the cover shows the color above."/>

                    <TextBlock Classes="section" Text="SECTIONS"/>
                    <TextBlock Classes="hint" Text="The tabs inside the notebook. Add as many as you like — pages come next."/>
                    <StackPanel x:Name="SectionRows" Spacing="6" Margin="0,4,0,0"/>
                    <Button x:Name="AddSectionBtn" Content="Add section" FontSize="12.5" HorizontalAlignment="Left"/>
                </StackPanel>

                <StackPanel x:Name="Step2Panel" Spacing="6" IsVisible="False">
                    <!-- Task 3 populates: grid style, page style previews, apply mode, font, size, pages editor -->
                </StackPanel>

            </Panel>
        </ScrollViewer>
    </DockPanel>
    <controls:WindowResizeBorder/>
    </Grid>
</Window>
```

- [ ] **Step 2: Code-behind** — `NotebookWizardWindow.axaml.cs`:

```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumenotepad.Platform;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

/// <summary>The two-step notebook creation wizard (M9): Step 1 = identity (name/color/cover/
/// sections), Step 2 = pages (styles, defaults, page titles per section). Everything edits a
/// NotebookDraft — nothing real exists until Create. Edit mode arrives in M9 Part 3.</summary>
public partial class NotebookWizardWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly NotebookDraft _draft = NotebookDraft.New();
    private int _step;                       // 0 or 1
    private string? _tempCover;              // cropped temp file; deleted on close if unused

    public NotebookWizardWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Opened += (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);
            NameBox.Focus();
        };
        bool closing = false;
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
            else Close();
        };
        Closed += (_, _) => CleanupTempCover();
        WizTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        CloseBtn.Click += (_, _) => Close();
        CancelBtn.Click += (_, _) => Close();

        NameBox.TextChanged += (_, _) => _draft.Name = NameBox.Text ?? "";
        BuildColorSwatches();
        CoverPickBtn.Click += async (_, _) => await PickCover();
        CoverClearBtn.Click += (_, _) => { CleanupTempCover(); _draft.CoverSourcePath = null; RefreshCoverPreview(); };
        AddSectionBtn.Click += (_, _) =>
        {
            _draft.Sections.Add(new SectionDraft { Name = $"Section {_draft.Sections.Count + 1}" });
            BuildSectionRows();
        };
        BuildSectionRows();
        RefreshCoverPreview();

        NextBtn.Click += (_, _) => ShowStep(1);
        BackBtn.Click += (_, _) => ShowStep(0);
        CreateBtn.Click += (_, _) => CreateAndClose();
        BuildStep2();                        // no-op until Task 3 fills it in
    }

    // ---- step switching ----

    private void ShowStep(int step)
    {
        _step = step;
        Step1Panel.IsVisible = step == 0;
        Step2Panel.IsVisible = step == 1;
        BackBtn.IsVisible = step == 1;
        NextBtn.IsVisible = step == 0;
        CreateBtn.IsVisible = step == 1;
        StepLabel.Text = step == 0 ? "Step 1 of 2 — Notebook" : "Step 2 of 2 — Pages";
        if (step == 1) SyncStep2();          // section list may have changed since last visit
        Motion.RiseIn(step == 0 ? Step1Panel : Step2Panel, Motion.Fast);
    }

    private void CreateAndClose()
    {
        _vm.CreateNotebook(_draft);
        _tempCover = null;                   // consumed by SetNotebookCover — nothing to clean
        Close();
    }

    // ---- step 1: color ----

    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        foreach (var (hex, name) in MainViewModel.NotebookColors)
            ColorSwatches.Children.Add(MakeSwatch(hex, name));
        foreach (var (family, shades) in MainViewModel.NotebookPalette)
            ColorSwatches.Children.Add(MakeSwatch(shades[2].Hex, family, small: true));
    }

    private Control MakeSwatch(string hex, string tip, bool small = false)
    {
        bool active = string.Equals(_draft.Color, hex, StringComparison.OrdinalIgnoreCase);
        var b = new Border
        {
            Width = small ? 18 : 26, Height = small ? 18 : 26,
            CornerRadius = new CornerRadius(small ? 9 : 13),
            Margin = new Avalonia.Thickness(0, 2, 8, 4),
            Background = new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = active ? (this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White)
                                 : new SolidColorBrush(Color.Parse("#66808080")),
            BorderThickness = new Avalonia.Thickness(active ? 2 : 1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tip);
        b.PointerPressed += (_, _) => { _draft.Color = hex; BuildColorSwatches(); RefreshCoverPreview(); };
        return b;
    }

    // ---- step 1: cover ----

    private async System.Threading.Tasks.Task PickCover()
    {
        if (StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a cover image", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" } },
            },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        var cropped = await CoverCropDialog.Show(this, path);
        if (cropped is null) return;
        CleanupTempCover();
        _tempCover = cropped;
        _draft.CoverSourcePath = cropped;
        RefreshCoverPreview();
    }

    private void RefreshCoverPreview()
    {
        if (_draft.CoverSourcePath is { } p)
        {
            try
            {
                CoverPreview.Background = new ImageBrush(new Avalonia.Media.Imaging.Bitmap(p)) { Stretch = Stretch.UniformToFill };
                return;
            }
            catch { /* unreadable temp — fall through to the color */ }
        }
        CoverPreview.Background = new SolidColorBrush(Color.Parse(_draft.Color));
    }

    private void CleanupTempCover()
    {
        if (_tempCover is { } t) { try { System.IO.File.Delete(t); } catch { } }
        _tempCover = null;
    }

    // ---- step 1: sections editor ----

    private void BuildSectionRows()
    {
        SectionRows.Children.Clear();
        foreach (var sd in _draft.Sections.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var name = new TextBox
            {
                Theme = (ControlTheme)this.FindResource("RoundedFieldTextBox")!,
                FontSize = 13, Text = sd.Name, Watermark = "Section name",
            };
            name.TextChanged += (_, _) => sd.Name = name.Text ?? "";
            var remove = new Button
            {
                Theme = (ControlTheme)this.FindResource("IconButton")!,
                Width = 28, Height = 28, FontSize = 12, Content = "",
                FontFamily = (FontFamily)this.FindResource("IconFont")!,
                IsEnabled = _draft.Sections.Count > 1,       // at least one section stays
            };
            ToolTip.SetTip(remove, _draft.Sections.Count > 1 ? "Remove section" : "A notebook needs at least one section");
            remove.Click += (_, _) => { _draft.Sections.Remove(sd); BuildSectionRows(); };
            Grid.SetColumn(remove, 1);
            row.Children.Add(name);
            row.Children.Add(remove);
            SectionRows.Children.Add(row);
        }
    }

    // ---- step 2 (populated in the next task) ----

    private void BuildStep2() { }
    private void SyncStep2() { }
}
```

(`Watermark` vs `PlaceholderText`: this Avalonia 12 build renamed it — use `PlaceholderText` if
`Watermark` doesn't compile; the repo's own TextBoxes use `PlaceholderText`. Use that.)

- [ ] **Step 3: Build + suite green.** The window compiles unused (Task 3 wires the open).
- [ ] **Step 4: Commit** `feat(m9): notebook wizard shell + Step 1 (name, color, cover, sections)`

---

### Task 3: Step 2 + Create flow + call-site rewire + notebook font consumption

**Files:**
- Modify: `src/Lumenotepad/Views/NotebookWizardWindow.axaml(.cs)`
- Modify: `src/Lumenotepad/Views/MainView.axaml(.cs)`

- [ ] **Step 1: Step 2 XAML** — replace the `Step2Panel` placeholder:

```xml
                <StackPanel x:Name="Step2Panel" Spacing="6" IsVisible="False">
                    <TextBlock Classes="section" Text="PAGE STYLE" Margin="0,4,0,4"/>
                    <TextBlock Classes="hint" Text="How new pages in this notebook are laid out. Every page can pick its own later."/>
                    <WrapPanel x:Name="StyleChips" Margin="0,4,0,0"/>

                    <TextBlock Classes="section" Text="APPLY AS"/>
                    <StackPanel x:Name="ModeRadios" Spacing="4">
                        <RadioButton x:Name="ModeGuides" GroupName="mode" FontSize="12.5" IsChecked="True"
                                     Content="Guides + starter notes — lines on the paper plus labelled note boxes"/>
                        <RadioButton x:Name="ModeStarters" GroupName="mode" FontSize="12.5"
                                     Content="Starter notes only — just the labelled boxes, no lines"/>
                        <RadioButton x:Name="ModeRigid" GroupName="mode" FontSize="12.5"
                                     Content="Rigid — the boxes are locked in place and can't be moved"/>
                    </StackPanel>

                    <TextBlock Classes="section" Text="GRID STYLE"/>
                    <ComboBox x:Name="GridBox" Width="180"/>

                    <TextBlock Classes="section" Text="TEXT DEFAULTS"/>
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock Classes="label" Text="Default font"/>
                        <ComboBox x:Name="FontBox" Grid.Column="1" Width="180"/>
                    </Grid>
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock Classes="label" Text="Default text size"/>
                        <TextBlock x:Name="SizeValue" Grid.Column="1" Classes="label" Text="15"/>
                    </Grid>
                    <Slider x:Name="SizeSlider" Minimum="11" Maximum="24" TickFrequency="1" Value="15"/>

                    <TextBlock Classes="section" Text="PAGES"/>
                    <TextBlock Classes="hint" Text="Name the pages each section starts with."/>
                    <StackPanel x:Name="PagesEditors" Spacing="10" Margin="0,4,0,0"/>
                </StackPanel>
```

- [ ] **Step 2: Step 2 code-behind** — replace the `BuildStep2`/`SyncStep2` stubs:

```csharp
    // ---- step 2: style previews, defaults, pages ----

    private void BuildStep2()
    {
        foreach (var style in Editor.PageStyles.Styles)
            StyleChips.Children.Add(MakeStyleChip(style));
        ModeGuides.IsCheckedChanged += (_, _) => { if (ModeGuides.IsChecked == true) _draft.DefaultPageStyleMode = Editor.PageStyles.ModeGuides; };
        ModeStarters.IsCheckedChanged += (_, _) => { if (ModeStarters.IsChecked == true) _draft.DefaultPageStyleMode = Editor.PageStyles.ModeStartersOnly; };
        ModeRigid.IsCheckedChanged += (_, _) => { if (ModeRigid.IsChecked == true) _draft.DefaultPageStyleMode = Editor.PageStyles.ModeRigid; };

        GridBox.ItemsSource = new[] { "Use my app setting", "Blank", "Ruled", "Grid", "Dots" };
        GridBox.SelectedIndex = 0;
        GridBox.SelectionChanged += (_, _) =>
            _draft.DefaultGridStyle = GridBox.SelectedIndex <= 0 ? null : (string?)GridBox.SelectedItem;

        FontBox.ItemsSource = new[] { "(App default)" }
            .Concat(Services.AppFonts.ListNames(_vm.ExtendedFonts)).ToArray();
        FontBox.SelectedIndex = 0;
        FontBox.SelectionChanged += (_, _) =>
            _draft.DefaultFont = FontBox.SelectedIndex <= 0 ? null : FontBox.SelectedItem as string;

        SizeSlider.ValueChanged += (_, e) =>
        {
            double v = System.Math.Round(e.NewValue);
            _draft.DefaultFontSize = v;
            SizeValue.Text = v.ToString("0");
        };
        MenuFxSafeAttach();                  // nothing yet — placeholder removed; combos animate app-wide
    }

    private void MenuFxSafeAttach() { }      // combos already animate via App styles; kept minimal

    /// <summary>One selectable page-style chip: a live mini GuideLayer preview + the style name.</summary>
    private Control MakeStyleChip(string style)
    {
        var preview = new Editor.GuideLayer { Width = 84, Height = 56, Viewport = new Avalonia.Size(84, 56) };
        preview.SetStyles(Editor.PageStyles.Blank, style, Editor.PageStyles.ModeGuides);
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Avalonia.Thickness(6),
            BorderThickness = new Avalonia.Thickness(string.Equals(_draft.DefaultPageStyle, style, StringComparison.Ordinal) ? 2 : 1),
            BorderBrush = string.Equals(_draft.DefaultPageStyle, style, StringComparison.Ordinal)
                ? (this.FindResource("AccentBrush") as IBrush ?? Brushes.White)
                : (this.FindResource("FrameBorderBrush") as IBrush ?? Brushes.Gray),
            Margin = new Avalonia.Thickness(0, 0, 8, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Width = 84, Height = 56, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                        Background = (this.FindResource("PaperBackgroundBrush") as IBrush ?? Brushes.Black),
                        Child = preview,
                    },
                    new TextBlock
                    {
                        Text = style, FontSize = 11.5,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    },
                },
            },
        };
        chip.PointerPressed += (_, _) =>
        {
            _draft.DefaultPageStyle = style;
            StyleChips.Children.Clear();
            foreach (var s in Editor.PageStyles.Styles) StyleChips.Children.Add(MakeStyleChip(s));
        };
        return chip;
    }

    /// <summary>Re-sync the per-section pages editors with Step 1's current section list.</summary>
    private void SyncStep2()
    {
        PagesEditors.Children.Clear();
        foreach (var sd in _draft.Sections)
        {
            var header = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sd.Name) ? "Section" : sd.Name,
                FontSize = 12.5, FontWeight = FontWeight.SemiBold,
            };
            var rows = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
            void Rebuild()
            {
                rows.Children.Clear();
                for (int i = 0; i < sd.PageTitles.Count; i++)
                {
                    int idx = i;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    var title = new TextBox
                    {
                        Theme = (ControlTheme)this.FindResource("RoundedFieldTextBox")!,
                        FontSize = 12.5, Text = sd.PageTitles[idx], PlaceholderText = "Page title",
                    };
                    title.TextChanged += (_, _) => sd.PageTitles[idx] = title.Text ?? "";
                    var remove = new Button
                    {
                        Theme = (ControlTheme)this.FindResource("IconButton")!,
                        Width = 26, Height = 26, FontSize = 11, Content = "",
                        FontFamily = (FontFamily)this.FindResource("IconFont")!,
                    };
                    ToolTip.SetTip(remove, "Remove page");
                    remove.Click += (_, _) => { sd.PageTitles.RemoveAt(idx); Rebuild(); };
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(title);
                    row.Children.Add(remove);
                    rows.Children.Add(row);
                }
                var add = new Button { Content = "Add page", FontSize = 12 };
                add.Click += (_, _) => { sd.PageTitles.Add($"Page {sd.PageTitles.Count + 1}"); Rebuild(); };
                rows.Children.Add(add);
            }
            Rebuild();
            var block = new StackPanel { Children = { header, rows } };
            PagesEditors.Children.Add(block);
        }
    }
```

Also add `using Avalonia;` / adjust for `Size`/`Thickness` etc. as the compiler requires, and
`using System.Linq;` is already in the header from Task 2.

- [ ] **Step 3: Rewire "New notebook" to the wizard**

In `MainView.axaml`: remove `Command="{Binding AddNotebookCommand}"` from BOTH call sites (the
`NewNotebookBtn` home button and the rail's + button — give the rail button `x:Name="RailAddBtn"`).
In `MainView.axaml.cs` ctor:

```csharp
        // "New notebook" opens the wizard (M9) — the instant-create command remains for tests only.
        NewNotebookBtn.Click += (_, _) => OpenNotebookWizard();
        RailAddBtn.Click += (_, _) => OpenNotebookWizard();
```

and the method (near `OpenPreferences`):

```csharp
    private void OpenNotebookWizard()
    {
        if (Vm is not { } vm || Window is not { } w) return;
        new NotebookWizardWindow(vm).ShowDialog(w);
    }
```

- [ ] **Step 4: Notebook default font/size finally consumed**

In `MainView.ApplyEditorPrefs`, the two push lines change from the globals to
notebook-default-first (notebook `DefaultFont` null = inherit; `DefaultFontSize` participates only
when a font-size differs from the global — keep it simple: notebook size wins when its notebook
font is set OR its size differs from 15):

```csharp
        var nb = vm.SelectedNotebook;
        RichTextEditor.EditorFontPref = nb?.DefaultFont ?? vm.EditorFont;
        RichTextEditor.EditorFontSizePref =
            nb is { } n && (n.DefaultFont is not null || System.Math.Abs(n.DefaultFontSize - 15) > 0.01)
                ? n.DefaultFontSize : vm.EditorFontSize;
```

and in `OnVmPropertyChanged`, the `SelectedNotebook` line gains a re-push so switching notebooks
re-applies fonts: `{ RehookSections(); ApplyPaperTint(); ApplyEditorPrefs(rebuild: true); }`.

- [ ] **Step 5: Build + full suite green** (177 from Task 1's two tests; no new tests here).
- [ ] **Step 6: Commit** `feat(m9): wizard Step 2 (styles, defaults, pages), wizard replaces instant create, notebook fonts consumed`

---

### Task 4: Final integration review + relaunch + checklist

- [ ] Opus review over the Part 2 diff vs plan + spec: CreateNotebook ordering (Save-before-cover,
  stamp-after-attach, pageless guard), temp-cover lifecycle (picked→replaced→cancelled→created),
  wizard step switching + section-list sync into Step 2 (rename/remove between visits), GuideLayer
  preview instances (no interference with the main canvas layer — separate instances, no statics),
  the AddNotebookCommand detachment (command still exists; no UI caller left), and the editor-font
  consumption seam (notebook switch re-pushes + rebuilds; no ctor-load hazard). Fix Important+
  inline; suite green.
- [ ] Rebuild + relaunch; memory update; owner checklist:
  1. "New notebook" (home button or rail +) → the wizard opens instead of instantly creating.
     Cancel/Escape → nothing appears anywhere.
  2. Step 1: type a name, pick colors (chip ring follows), choose + crop a cover (preview shows),
     add/rename/remove sections (last one can't be removed).
  3. Next → Step 2: style chips show live mini previews (Cornell's dividers, Boxing's boxes…);
     pick Cornell + Rigid; set a font + size; add/rename pages under each section. Back keeps
     everything; renaming a section then returning re-labels its pages block.
  4. Create → the notebook opens: sections/pages exist, styled pages have their (locked) starters,
     new containers use the notebook font/size, the cover shows on the home card.
  5. Restart: everything persists.
