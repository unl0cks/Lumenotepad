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

        SmoothScroll.Attach(WizScroll);      // same wheel easing as the preferences window
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
        CleanupTempCover();                  // SetNotebookCover COPIED the crop — delete our temp now
        Close();
    }

    // ---- step 1: color ----

    /// <summary>The picked FAMILY (big circles = the 9 family base colors); its 5 shades show as
    /// small circles underneath, like the right-click Color menu (owner request).</summary>
    private int _familyIx = 6;               // Blue — the family of the draft's initial color

    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        for (int i = 0; i < MainViewModel.NotebookPalette.Length; i++)
        {
            int ix = i;
            var (family, shades) = MainViewModel.NotebookPalette[i];
            bool familyActive = ix == _familyIx;
            var b = MakeSwatch(shades[2].Hex, family, size: 26, ringed: familyActive);
            b.PointerPressed += (_, _) =>
            {
                _familyIx = ix;
                _draft.Color = MainViewModel.NotebookPalette[ix].Shades[2].Hex;   // base shade selected
                BuildColorSwatches();
                RefreshCoverPreview();
            };
            ColorSwatches.Children.Add(b);
        }
        ShadeSwatches.Children.Clear();
        foreach (var (shadeName, hex) in MainViewModel.NotebookPalette[_familyIx].Shades)
        {
            var chosen = hex;
            var b = MakeSwatch(hex, shadeName, size: 18,
                ringed: string.Equals(_draft.Color, hex, StringComparison.OrdinalIgnoreCase));
            b.PointerPressed += (_, _) =>
            {
                _draft.Color = chosen;
                BuildColorSwatches();
                RefreshCoverPreview();
            };
            ShadeSwatches.Children.Add(b);
        }
        Motion.FadeIn(ShadeSwatches, Motion.Fast);   // the shade row follows the family pick
    }

    private Border MakeSwatch(string hex, string tip, double size, bool ringed)
    {
        var b = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Margin = new Avalonia.Thickness(0, 2, 8, 4),
            Background = new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = ringed ? (this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White)
                                 : new SolidColorBrush(Color.Parse("#66808080")),
            BorderThickness = new Avalonia.Thickness(ringed ? 2 : 1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tip);
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

        MenuFx.AttachDropDown(GridBox);      // rise-in + rounded/blurred popup + eased list scroll
        MenuFx.AttachDropDown(FontBox);
        foreach (var radio in new[] { ModeGuides, ModeStarters, ModeRigid })
            radio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true } r) Motion.ScaleIn(r, 0.96, Motion.Fast);
            };
    }

    /// <summary>One selectable page-style chip: a live mini GuideLayer preview + the style name.</summary>
    private Control MakeStyleChip(string style)
    {
        var preview = new Editor.GuideLayer { Width = 120, Height = 72, Viewport = new Avalonia.Size(120, 72) };
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
                        Width = 120, Height = 72, CornerRadius = new CornerRadius(6), ClipToBounds = true,
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
            // pop the freshly selected chip (the rebuilt row loses the pressed element)
            if (StyleChips.Children.OfType<Border>().FirstOrDefault(c => Equals(c.Tag, style)) is { } picked)
                Motion.ScaleIn(picked, 0.94, Motion.Fast);
        };
        chip.Tag = style;
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
                        Width = 26, Height = 26, FontSize = 11, Content = "",
                        FontFamily = (FontFamily)this.FindResource("IconFont")!,
                    };
                    ToolTip.SetTip(remove, "Remove page");
                    remove.Click += (_, _) => { sd.PageTitles.RemoveAt(idx); Rebuild(); };
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(title);
                    row.Children.Add(remove);
                    rows.Children.Add(row);
                }
                var add = new Button
                {
                    Theme = (ControlTheme)this.FindResource("LumenButton")!,
                    Content = "Add page", FontSize = 12,
                    Padding = new Avalonia.Thickness(12, 5),
                };
                add.Click += (_, _) => { sd.PageTitles.Add($"Page {sd.PageTitles.Count + 1}"); Rebuild(); };
                rows.Children.Add(add);
            }
            Rebuild();
            var block = new StackPanel { Children = { header, rows } };
            PagesEditors.Children.Add(block);
        }
    }
}
