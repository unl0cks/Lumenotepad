using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
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
                FontSize = 13, Text = sd.Name, PlaceholderText = "Section name",
            };
            name.TextChanged += (_, _) => sd.Name = name.Text ?? "";
            var remove = new Button
            {
                Theme = (ControlTheme)this.FindResource("IconButton")!,
                Width = 28, Height = 28, FontSize = 12, Content = "",
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
