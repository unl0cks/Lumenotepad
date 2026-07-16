using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumenotepad.Platform;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>The Font Browser (M11): scroll and search the full Google Fonts catalog with LIVE
/// previews rendered in each face, a custom preview string, bold/italic/underline/strike toggles,
/// and friendly category chips. Previews download lazily per visible row (virtualized list) and
/// render via SkiaSharp — installing reuses the same download path as the inline quick-search.</summary>
public partial class FontBrowserWindow : Window
{
    private const string DefaultPreview = "The quick brown fox jumps over the lazy dog";

    private IReadOnlyList<FontCatalog.CatalogFont> _catalog = Array.Empty<FontCatalog.CatalogFont>();
    private List<FontRow> _rows = new();
    private string _category = "all";
    private DispatcherTimer? _searchDebounce, _previewDebounce;
    private uint _textColorArgb = 0xFFECECEC;

    public FontBrowserWindow()
    {
        InitializeComponent();

        PreviewBox.Text = DefaultPreview;
        if (Application.Current?.FindResource("TextPrimaryBrush") is ISolidColorBrush b)
            _textColorArgb = b.Color.ToUInt32();

        BuildRowTemplate();
        BuildCategoryChips();

        FontList.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is FontRow r) _ = EnsureRow(r);
        };

        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(new Observer(() => Debounce(ref _searchDebounce, ApplyFilter)));
        PreviewBox.GetObservable(TextBox.TextProperty).Subscribe(new Observer(() => Debounce(ref _previewDebounce, ReRenderVisible)));
        foreach (var t in new[] { BoldToggle, ItalicToggle, UnderToggle, StrikeToggle })
            t.IsCheckedChanged += (_, _) => ReRenderVisible();

        Opened += async (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);
            // The ListBox's inner ScrollViewer only exists once templated — attach smooth scroll then.
            Dispatcher.UIThread.Post(() =>
            {
                if (FontList.FindDescendantOfType<ScrollViewer>() is { } sv) SmoothScroll.Attach(sv);
            }, DispatcherPriority.Background);
            await LoadCatalog();
        };
        bool closing = false;
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true;
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
            else Close();
        };
        BrowserTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        CloseBtn.Click += (_, _) => Close();
    }

    private async Task LoadCatalog()
    {
        SetStatus("Loading the font catalog…");
        _catalog = await FontCatalog.LoadAsync();
        if (_catalog.Count == 0)
        {
            SetStatus("Couldn't reach Google Fonts. Check your connection and reopen this window.");
            return;
        }
        SetStatus(null);
        ApplyFilter();
    }

    private void BuildCategoryChips()
    {
        foreach (var (key, label) in FontCatalog.Categories)
        {
            var chip = new ToggleButton { Content = label, IsChecked = key == "all" };
            chip.Classes.Add("chip");
            chip.Margin = new Thickness(0, 0, 8, 8);
            chip.Click += (_, _) =>
            {
                _category = key;
                foreach (var other in CategoryChips.Children.OfType<ToggleButton>())
                    other.IsChecked = ReferenceEquals(other, chip);
                ApplyFilter();
            };
            CategoryChips.Children.Add(chip);
        }
    }

    private void ApplyFilter()
    {
        if (_catalog.Count == 0) return;
        var filtered = FontCatalog.Filter(_catalog, _category, SearchBox.Text);
        _rows = filtered.Select(f => new FontRow(f)).ToList();
        FontList.ItemsSource = _rows;
        CountLabel.Text = _rows.Count == 0 ? "" : $"{_rows.Count} fonts";
        SetStatus(_rows.Count == 0 ? "No fonts match. Try another word or category." : null);
        // Scroll back to the top so a new filter starts at its first result.
        if (_rows.Count > 0) FontList.ScrollIntoView(0);
    }

    /// <summary>Load a row's font bytes (once) and render its preview with the current settings.</summary>
    private async Task EnsureRow(FontRow row)
    {
        if (row.Bytes is null)
        {
            if (row.Loading) return;
            row.Loading = true;
            row.Bytes = await FontPreviewRenderer.GetBytesAsync(row.Font.Name);
            row.Loading = false;
            if (row.Bytes is null) { row.MarkPreviewFailed(); return; }
        }
        RenderRow(row);
    }

    private void RenderRow(FontRow row)
    {
        if (row.Bytes is null) return;
        row.Preview = FontPreviewRenderer.Render(
            row.Bytes, PreviewText(), BoldToggle.IsChecked == true, ItalicToggle.IsChecked == true,
            UnderToggle.IsChecked == true, StrikeToggle.IsChecked == true, _textColorArgb);
    }

    /// <summary>Re-render every REALIZED (on-screen) row with the current preview text/style — bytes
    /// are cached, so this is a cheap Skia redraw, no re-download. Off-screen rows re-render with the
    /// live settings when they scroll back in.</summary>
    private void ReRenderVisible()
    {
        foreach (var c in FontList.GetRealizedContainers())
            if (c.DataContext is FontRow { Bytes: not null } r) RenderRow(r);
    }

    private string PreviewText()
    {
        var t = PreviewBox.Text;
        return string.IsNullOrWhiteSpace(t) ? DefaultPreview : t;
    }

    private void BuildRowTemplate()
    {
        FontList.ItemTemplate = new FuncDataTemplate<FontRow>((row, _) =>
        {
            if (row is null) return new Control();

            var name = new TextBlock
            {
                Text = row.Font.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold,
                Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
            };
            var category = new TextBlock
            {
                Text = row.Font.Category, FontSize = 10.5,
                Foreground = this.FindResource("TextMutedBrush") as IBrush,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            header.Children.Add(name);
            header.Children.Add(category);

            var preview = new Image { Height = 34, Stretch = Stretch.Uniform, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            preview.Bind(Image.SourceProperty, new Avalonia.Data.Binding(nameof(FontRow.Preview)));
            var loading = new TextBlock
            {
                Text = "…", FontSize = 13, Foreground = this.FindResource("TextMutedBrush") as IBrush,
                Height = 34, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            loading.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding(nameof(FontRow.ShowFallback)));

            var left = new StackPanel { Spacing = 3 };
            left.Children.Add(header);
            var previewHost = new Panel();
            previewHost.Children.Add(loading);
            previewHost.Children.Add(preview);
            left.Children.Add(previewHost);

            var install = new Button
            {
                Theme = this.FindResource("LumenButton") as Avalonia.Styling.ControlTheme,
                FontSize = 12.5, Padding = new Thickness(14, 6), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            install.Bind(ContentControl.ContentProperty, new Avalonia.Data.Binding(nameof(FontRow.InstallLabel)));
            install.Bind(InputElement.IsEnabledProperty, new Avalonia.Data.Binding(nameof(FontRow.InstallEnabled)));
            install.Click += async (_, _) => await InstallRow(row);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(left);
            Grid.SetColumn(install, 1);
            grid.Children.Add(install);

            return new Border
            {
                Padding = new Thickness(12, 9), Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(9),
                Background = this.FindResource("ControlHoverBrush") as IBrush,
                Child = grid,
            };
        });
    }

    private async Task InstallRow(FontRow row)
    {
        if (!row.InstallEnabled) return;
        row.InstallLabel = "Installing…";
        row.InstallEnabled = false;
        try
        {
            int files = await FontInstaller.InstallAsync(new FontInstaller.Hit(row.Font.Name, "Google Fonts", row.Font.Name));
            if (files > 0)
            {
                AppFonts.RegisterInstalled();
                row.InstallLabel = "Installed";
            }
            else { row.InstallLabel = "Install"; row.InstallEnabled = true; }
        }
        catch { row.InstallLabel = "Install"; row.InstallEnabled = true; }
    }

    private void SetStatus(string? text)
    {
        StatusLabel.Text = text ?? "";
        StatusLabel.IsVisible = text is not null;
    }

    private void Debounce(ref DispatcherTimer? timer, Action action)
    {
        timer?.Stop();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        var t = timer;
        timer.Tick += (_, _) => { t.Stop(); action(); };
        timer.Start();
    }

    /// <summary>A minimal IObserver so we can react to a TextBox's Text without pulling in Rx.</summary>
    private sealed class Observer : IObserver<string?>
    {
        private readonly Action _onNext;
        private bool _primed;
        public Observer(Action onNext) => _onNext = onNext;
        public void OnNext(string? value) { if (_primed) _onNext(); else _primed = true; }  // skip the initial push
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    /// <summary>One catalog row's live state: cached font bytes, the rendered preview, and the
    /// install-button label/enabled flags. Notifies so the virtualized template updates in place.</summary>
    private sealed class FontRow : INotifyPropertyChanged
    {
        public FontCatalog.CatalogFont Font { get; }
        public byte[]? Bytes;
        public bool Loading;

        public FontRow(FontCatalog.CatalogFont f) => Font = f;

        private Bitmap? _preview;
        public Bitmap? Preview
        {
            get => _preview;
            set { _preview = value; Raise(nameof(Preview)); Raise(nameof(ShowFallback)); }
        }
        public bool ShowFallback => _preview is null;

        public void MarkPreviewFailed() { Raise(nameof(ShowFallback)); }

        private string _installLabel = "Install";
        public string InstallLabel { get => _installLabel; set { _installLabel = value; Raise(nameof(InstallLabel)); } }

        private bool _installEnabled = true;
        public bool InstallEnabled { get => _installEnabled; set { _installEnabled = value; Raise(nameof(InstallEnabled)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
