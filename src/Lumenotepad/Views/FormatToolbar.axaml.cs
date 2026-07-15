using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lumenotepad.Editor;

namespace Lumenotepad.Views;

/// <summary>The formatting toolbar: bold/italic/underline/strike, highlight + text-color swatches,
/// font size, a font picker, and the dock menu (top/left/right/bottom). Drives a target
/// <see cref="RichTextEditor"/> and mirrors its caret format via SelectionChanged.</summary>
public partial class FormatToolbar : UserControl
{
    private static readonly (string? Hex, string Name)[] Highlights =
    {
        (null, "None"),
        ("#66FFD666", "Yellow"), ("#6699E28A", "Green"), ("#66FF8FAB", "Pink"),
        ("#664DA6FF", "Blue"), ("#66C9A0FF", "Purple"),
    };

    private static readonly (string? Hex, string Name)[] TextColors =
    {
        (null, "Default"),
        ("#FF8FAB", "Pink"), ("#FFD666", "Yellow"), ("#8AE29B", "Green"),
        ("#4DA6FF", "Blue"), ("#C9A0FF", "Purple"), ("#FF6B6B", "Red"),
    };

    /// <summary>The built-in palettes (the "(none)" entry excluded) — prefs seeds edits from these.
    /// MUST be declared after <see cref="Highlights"/>/<see cref="TextColors"/> — static field
    /// initializers run in declaration order, so an earlier declaration would read null arrays.</summary>
    public static readonly string[] BuiltInHighlights =
        Highlights.Where(h => h.Hex is not null).Select(h => h.Hex!).ToArray();
    public static readonly string[] BuiltInTextColors =
        TextColors.Where(c => c.Hex is not null).Select(c => c.Hex!).ToArray();

    private System.Collections.Generic.IReadOnlyList<string>? _customHighlights, _customTextColors;

    private RichTextEditor? _target;
    private bool _syncing;

    /// <summary>Raised when the user picks a dock side ("Top"/"Left"/"Right"/"Bottom").</summary>
    public event Action<string>? DockRequested;

    /// <summary>Raised when the user picks a dock scope: "Window" (toolbar hugs the window edge)
    /// or "Page" (toolbar lives inside the page box).</summary>
    public event Action<string>? ScopeRequested;

    /// <summary>Raised by the far-end Customize button — MainView opens the notebook wizard in
    /// edit mode for the selected notebook.</summary>
    public event Action? CustomizeRequested;

    /// <summary>Raised by the image button — MainView picks a file and drops an image box on the page.</summary>
    public event Action? InsertImageRequested;

    public RichTextEditor? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.SelectionChanged -= UpdateFromEditor;
            _target = value;
            if (_target is not null) _target.SelectionChanged += UpdateFromEditor;
            UpdateFromEditor();
        }
    }

    public FormatToolbar()
    {
        InitializeComponent();

        // Picker flyouts (bullets, highlight, color, font, text type, alignment) rise in like the menus do.
        foreach (var btn in new[] { BulletBtn, HighlightBtn, ColorBtn, FontBtn, TypeBtn, AlignBtn })
            if (btn.Flyout is { } f) MenuFx.AttachFlyout(f);

        // The font list glides like every other list (its ScrollViewer only exists once opened).
        bool fontScrollSmoothed = false;
        if (FontBtn.Flyout is { } fontFly) fontFly.Opened += (_, _) =>
        {
            if (fontScrollSmoothed) return;
            if (FontList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
            { SmoothScroll.Attach(sv); fontScrollSmoothed = true; }
        };

        BoldBtn.Click += (_, _) => Do(e => e.ToggleBold());
        ItalicBtn.Click += (_, _) => Do(e => e.ToggleItalic());
        UnderBtn.Click += (_, _) => Do(e => e.ToggleUnderline());
        StrikeBtn.Click += (_, _) => Do(e => e.ToggleStrike());
        SuperBtn.Click += (_, _) => Do(e => e.ToggleSuper());
        SubBtn.Click += (_, _) => Do(e => e.ToggleSub());
        LinkBtn.Click += async (_, _) => await AddLinkAsync();
        ImageBtn.Click += (_, _) => InsertImageRequested?.Invoke();
        BuildAlignChoices();
        BuildTypeChoices();
        SizeMinus.Click += (_, _) => NudgeSize(-1);
        SizePlus.Click += (_, _) => NudgeSize(+1);
        SizeBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            ApplyTypedSize();
            _target?.Focus();
            e.Handled = true;
        };
        SizeBox.LostFocus += (_, _) => ApplyTypedSize();

        CustomizeBtn.Click += (_, _) => CustomizeRequested?.Invoke();

        BuildSwatches(HighlightSwatches, Highlights, hex => Do(e => e.ApplyHighlight(hex)), HighlightBtn);
        BuildSwatches(ColorSwatches, TextColors, hex => Do(e => e.ApplyColor(hex)), ColorBtn);
        BuildBulletChoices();
        BuildNumStyleRow();
        BuildFontList();
        BuildDockMenu();
    }

    /// <summary>Reflow for the dock side + scope: orientation, breathing-room margins, and flyouts
    /// opening away from the docked edge.</summary>
    public void SetPlacement(Dock dock, bool pageScope)
    {
        Classes.Set("onpaper", pageScope);
        bool vertical = dock is Dock.Left or Dock.Right;
        Panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        SizeGroup.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        // Keep the "..." overflow + customize buttons at the opposite end of the strip for every dock.
        DockPanel.SetDock(DockBtn, vertical ? Dock.Bottom : Dock.Right);
        DockPanel.SetDock(CustomizeBtn, vertical ? Dock.Bottom : Dock.Right);
        Chrome.Padding = vertical ? new Thickness(2, 6) : new Thickness(6, 4);

        var placement = dock switch
        {
            Dock.Left => PlacementMode.Right,
            Dock.Right => PlacementMode.Left,
            Dock.Bottom => PlacementMode.Top,
            _ => PlacementMode.Bottom,
        };
        foreach (var b in new[] { BulletBtn, HighlightBtn, ColorBtn, FontBtn, TypeBtn, AlignBtn })
            if (b.Flyout is PopupFlyoutBase pf) pf.Placement = placement;
        if (DockBtn.Flyout is PopupFlyoutBase df) df.Placement = placement;

        // Inside the page box the strip needs an inset from the rounded edge + a gap toward the content;
        // on the window edges the vertical strips need air toward the neighboring panel.
        Margin = (pageScope, dock) switch
        {
            (true, Dock.Top) => new Thickness(14, 12, 14, 0),
            (true, Dock.Bottom) => new Thickness(14, 0, 14, 12),
            (true, Dock.Left) => new Thickness(7, 14, 0, 14),
            (true, Dock.Right) => new Thickness(0, 14, 7, 14),
            (false, Dock.Left) => new Thickness(3, 4, 0, 4),
            (false, Dock.Right) => new Thickness(0, 4, 3, 4),
            _ => new Thickness(0),
        };

        // Docked to the window, the strip is frame furniture: frame fill + a hairline toward the
        // content, so its icons sit on the frame color on every theme. Inside the page it stays bare.
        // The resource bindings MUST be disposed on scope change — a live binding survives
        // ClearValue and re-paints the old chrome on the next theme switch.
        _chromeBgSub?.Dispose();
        _chromeBorderSub?.Dispose();
        _chromeBgSub = _chromeBorderSub = null;
        if (pageScope)
        {
            Chrome.ClearValue(Border.BackgroundProperty);
            Chrome.ClearValue(Border.BorderBrushProperty);
            Chrome.BorderThickness = new Thickness(0);
        }
        else
        {
            _chromeBgSub = Chrome.Bind(Border.BackgroundProperty, this.GetResourceObservable("FrameBackgroundBrush"));
            _chromeBorderSub = Chrome.Bind(Border.BorderBrushProperty, this.GetResourceObservable("FrameBorderBrush"));
            Chrome.BorderThickness = dock switch
            {
                Dock.Left => new Thickness(0, 0, 1, 0),
                Dock.Right => new Thickness(1, 0, 0, 0),
                Dock.Bottom => new Thickness(0, 1, 0, 0),
                _ => new Thickness(0, 0, 0, 1),
            };
            Margin = new Thickness(0);   // flush with the window edge, like the panels
        }
    }

    private System.IDisposable? _chromeBgSub, _chromeBorderSub;

    private void Do(Action<RichTextEditor> action)
    {
        if (_target is null) return;
        action(_target);
        _target.Focus();          // formatting shouldn't steal the caret
    }

    private void NudgeSize(int delta)
    {
        if (_target is null) return;
        double cur = _target.CurrentFormat.Size ?? _target.FontSize;
        SetSize(cur + delta, focusEditor: true);
    }

    /// <summary>Apply whatever number is typed in the size box (invalid input just re-syncs the display).</summary>
    private void ApplyTypedSize()
    {
        if (_target is null) return;
        if (double.TryParse(SizeBox.Text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.CurrentCulture, out var v))
            SetSize(v, focusEditor: false);
        else
            UpdateFromEditor();
    }

    private void SetSize(double size, bool focusEditor)
    {
        if (_target is null) return;
        size = Math.Clamp(size, 8, 72);
        double? value = Math.Abs(size - _target.FontSize) < 0.01 ? null : size;
        _target.ApplySize(value);
        if (focusEditor) _target.Focus();
        UpdateFromEditor();
    }

    private void BuildSwatches(StackPanel host, (string? Hex, string Name)[] items, Action<string?> apply, Button owner)
    {
        foreach (var (hex, name) in items)
        {
            var b = new Button
            {
                // SwatchButton: the default theme's hover repaints Background gray, hiding the color.
                // App-level lookup — the toolbar isn't in the tree yet when its ctor builds these.
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("SwatchButton")!,
                Classes = { "swatch" },
                Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
                Content = hex is null ? new TextBlock { Text = "∅", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center } : null,
            };
            ToolTip.SetTip(b, name);
            b.Click += (_, _) => { apply(hex); owner.Flyout?.Hide(); };
            host.Children.Add(b);
        }
    }

    /// <summary>Palette prefs: rebuild the highlight/text-color swatch rows from custom lists (names
    /// become the hex strings for custom colors — tooltips only). Empty lists never reach here — the
    /// VM's PaletteFor seeds from the built-ins — but the "same" guard still skips redundant rebuilds.</summary>
    public void SetPalettes(System.Collections.Generic.IReadOnlyList<string> highlights,
                            System.Collections.Generic.IReadOnlyList<string> textColors)
    {
        bool same = _customHighlights is not null && _customHighlights.SequenceEqual(highlights)
                 && _customTextColors is not null && _customTextColors.SequenceEqual(textColors);
        if (same) return;
        _customHighlights = highlights.ToList();
        _customTextColors = textColors.ToList();
        HighlightSwatches.Children.Clear();
        ColorSwatches.Children.Clear();
        BuildSwatches(HighlightSwatches,
            new[] { ((string?)null, "None") }.Concat(highlights.Select(h => ((string?)h, h))).ToArray(),
            hex => Do(e => e.ApplyHighlight(hex)), HighlightBtn);
        BuildSwatches(ColorSwatches,
            new[] { ((string?)null, "Default") }.Concat(textColors.Select(c => ((string?)c, c))).ToArray(),
            hex => Do(e => e.ApplyColor(hex)), ColorBtn);
    }

    private static readonly (string? Key, string Glyph, string Name)[] Bullets =
    {
        (null, "∅", "None"),
        ("dot", "●", "Bullet"), ("arrow", "➤", "Arrow"), ("star", "★", "Star"),
        ("heart", "♥", "Heart"), ("flower", "✿", "Flower"), ("spark", "✦", "Spark"),
        ("num", "1.", "Numbered list"), ("check", "☑", "Checklist"),
    };

    private void BuildBulletChoices()
    {
        foreach (var (key, glyph, name) in Bullets)
        {
            var b = new Button
            {
                Width = 30, Height = 30, FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI Emoji, Segoe UI"),
                Content = glyph,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(b, name);
            b.Click += (_, _) => { Do(e => e.ApplyBullet(key)); BulletBtn.Flyout?.Hide(); };
            BulletChoices.Children.Add(b);
        }
    }

    // ---- M10: alignment, text type, link ----

    // Icon-font glyph per alignment (Left/Center/Right are real Segoe glyphs; Justify has none, so
    // its row leads with a bars symbol from the symbol font instead).
    private static readonly (TextAlign Align, string Glyph, string Name)[] Aligns =
    {
        (TextAlign.Left, "", "Left"), (TextAlign.Center, "", "Center"),
        (TextAlign.Right, "", "Right"), (TextAlign.Justify, "≡", "Justify"),
    };

    private void BuildAlignChoices()
    {
        var iconFont = (FontFamily)Application.Current!.FindResource("IconFont")!;
        var symbolFont = new FontFamily("Segoe UI Symbol, Segoe UI");
        foreach (var (align, glyph, name) in Aligns)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = glyph, FontSize = 14,
                FontFamily = align == TextAlign.Justify ? symbolFont : iconFont,
                Width = 18, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("LumenButtonGray")!,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 5), Content = row,
            };
            b.Click += (_, _) => { Do(e => e.SetAlignment(align)); AlignBtn.Flyout?.Hide(); };
            AlignChoices.Children.Add(b);
        }
    }

    private static readonly (ParaStyle Style, string Name)[] TextTypes =
    {
        (ParaStyle.Body, "Body"), (ParaStyle.Title, "Title"), (ParaStyle.Subtitle, "Subtitle"),
        (ParaStyle.Heading1, "Heading 1"), (ParaStyle.Heading2, "Heading 2"), (ParaStyle.Heading3, "Heading 3"),
    };

    private void BuildTypeChoices()
    {
        foreach (var (style, name) in TextTypes)
        {
            // Preview the type at its own size/weight so the menu reads like a style gallery.
            double size = style switch
            {
                ParaStyle.Title => 20, ParaStyle.Subtitle => 15.5, ParaStyle.Heading1 => 18,
                ParaStyle.Heading2 => 16, ParaStyle.Heading3 => 14.5, _ => 13,
            };
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("LumenButtonGray")!,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 0, 0),
                Content = new TextBlock
                {
                    Text = name, FontSize = size,
                    FontWeight = RichTextEditor.BaseWeightFor(style),
                },
            };
            b.Click += (_, _) => { Do(e => e.SetTextType(style)); TypeBtn.Flyout?.Hide(); };
            TypeChoices.Children.Add(b);
        }
    }

    /// <summary>Add a hyperlink: link the selection (prompt for the URL only), or — with nothing
    /// selected — prompt for both the text to show and the URL, then insert it.</summary>
    private async System.Threading.Tasks.Task AddLinkAsync()
    {
        if (_target is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        string? existing = _target.CurrentLink;
        bool hasSelection = _target.HasSelection;

        if (hasSelection)
        {
            var r = await InputDialog.Show(owner, existing is null ? "Add link" : "Edit link",
                new[] { ("Address", "https://example.com", existing ?? "") }, "Apply");
            if (r is null) return;
            _target.ApplyLink(string.IsNullOrWhiteSpace(r[0]) ? null : r[0]);
        }
        else
        {
            var r = await InputDialog.Show(owner, "Add link",
                new[] { ("Text to show", "Link text", ""), ("Address", "https://example.com", "") }, "Add");
            if (r is null || string.IsNullOrWhiteSpace(r[1])) return;
            var text = string.IsNullOrWhiteSpace(r[0]) ? r[1] : r[0];
            _target.InsertLink(text, r[1]);
        }
        _target.Focus();
        UpdateFromEditor();
    }

    private Button? _numB, _numI, _numU, _numS;

    /// <summary>The per-list number-style row: label + B/I/U/S toggles + "match text" reset. Lives in
    /// the bullet flyout, visible only when the caret sits in a numbered list; the flyout stays open
    /// so several flags can be flipped in a row.</summary>
    private void BuildNumStyleRow()
    {
        var label = new TextBlock
        {
            Text = "Numbers:", FontSize = 11.5, Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0),
        };
        NumStylePanel.Children.Add(label);

        Button Make(string text, char flag, string tip, FontWeight weight = FontWeight.Normal,
                    FontStyle style = FontStyle.Normal, TextDecorationCollection? deco = null)
        {
            var b = new Button
            {
                Width = 30, Height = 30, FontSize = 13, Theme = BoldBtn.Theme,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = text, FontWeight = weight, FontStyle = style, TextDecorations = deco,
                },
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => Do(e => e.ToggleNumStyle(flag));
            NumStylePanel.Children.Add(b);
            return b;
        }
        _numB = Make("B", 'b', "Bold numbers", weight: FontWeight.Bold);
        _numI = Make("I", 'i', "Italic numbers", style: FontStyle.Italic);
        _numU = Make("U", 'u', "Underlined numbers", deco: TextDecorations.Underline);
        _numS = Make("S", 's', "Struck-through numbers", deco: TextDecorations.Strikethrough);

        var reset = new Button
        {
            Height = 30, FontSize = 11.5, Theme = BoldBtn.Theme, Padding = new Thickness(8, 0),
            Content = "Match text", VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(reset, "Numbers follow their line's own formatting again");
        reset.Click += (_, _) => Do(e => e.ClearNumStyle());
        NumStylePanel.Children.Add(reset);
    }

    private bool _extendedFonts;
    private System.Collections.Generic.IReadOnlyCollection<string>? _disabledFonts;

    /// <summary>Fonts prefs: the full installed list vs the curated shortlist, minus the
    /// curation blocklist. Rebuilds the menu only when something actually changed.</summary>
    public void SetFontPrefs(bool extended, System.Collections.Generic.IReadOnlyCollection<string> disabled)
    {
        bool same = _extendedFonts == extended && _disabledFonts is not null
            && _disabledFonts.Count == disabled.Count
            && _disabledFonts.SequenceEqual(disabled);
        if (same && FontList.ItemsSource is not null) return;
        _extendedFonts = extended;
        _disabledFonts = disabled.ToList();               // snapshot — the VM list mutates in place
        RefreshFontList();
    }

    private void BuildFontList()
    {
        // Each entry previews in its own face (bundled fonts resolve via the embedded collection).
        FontList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string?>((name, _) =>
            new TextBlock
            {
                Text = name,
                FontFamily = string.IsNullOrEmpty(name) || name == "(Default)"
                    ? new FontFamily("Segoe UI Variable Text, Segoe UI")
                    : Services.AppFonts.Family(name),
            });
        RefreshFontList();
        FontList.SelectionChanged += (_, _) =>
        {
            if (_syncing || FontList.SelectedItem is not string name) return;
            Do(e => e.ApplyFont(name == "(Default)" ? null : name));
            FontBtn.Flyout?.Hide();
        };
    }

    private void RefreshFontList()
    {
        var names = new System.Collections.Generic.List<string> { "(Default)" };
        names.AddRange(Services.AppFonts.ListNames(_extendedFonts, _disabledFonts));
        FontList.ItemsSource = names;
    }

    private void BuildDockMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var pos in new[] { "Top", "Left", "Right", "Bottom" })
        {
            var item = new MenuItem { Header = $"Dock {pos.ToLowerInvariant()}" };
            item.Click += (_, _) => DockRequested?.Invoke(pos);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var winScope = new MenuItem { Header = "Attach to window" };
        winScope.Click += (_, _) => ScopeRequested?.Invoke("Window");
        var pageScope = new MenuItem { Header = "Attach to page" };
        pageScope.Click += (_, _) => ScopeRequested?.Invoke("Page");
        flyout.Items.Add(winScope);
        flyout.Items.Add(pageScope);
        MenuFx.AttachFlyout(flyout);
        DockBtn.Flyout = flyout;
        DockBtn.Click += (_, _) => flyout.ShowAt(DockBtn);
    }

    private void UpdateFromEditor()
    {
        if (_target is null) return;
        _syncing = true;
        try
        {
            var f = _target.CurrentFormat;
            BoldBtn.Classes.Set("on", f.Bold);
            ItalicBtn.Classes.Set("on", f.Italic);
            UnderBtn.Classes.Set("on", f.Underline);
            StrikeBtn.Classes.Set("on", f.Strike);
            HighlightBtn.Classes.Set("on", f.Highlight is not null);
            ColorBtn.Classes.Set("on", f.Color is not null);
            SuperBtn.Classes.Set("on", f.Baseline == Baseline.Super);
            SubBtn.Classes.Set("on", f.Baseline == Baseline.Sub);
            LinkBtn.Classes.Set("on", f.Link is not null);
            AlignBtn.Classes.Set("on", _target.CurrentAlign != TextAlign.Left);
            TypeBtn.Classes.Set("on", _target.CurrentTextType != ParaStyle.Body);
            BulletBtn.Classes.Set("on", _target.CurrentBullet is not null);
            NumStylePanel.IsVisible = _target.CurrentBullet == "num";
            if (_target.CurrentNumStyle is { } ns)
            {
                _numB?.Classes.Set("on", ns.Bold);
                _numI?.Classes.Set("on", ns.Italic);
                _numU?.Classes.Set("on", ns.Underline);
                _numS?.Classes.Set("on", ns.Strike);
            }
            if (!SizeBox.IsFocused)                       // don't clobber a number mid-typing
                SizeBox.Text = (f.Size ?? _target.FontSize).ToString("0");
            FontList.SelectedItem = f.Font ?? "(Default)";
        }
        finally { _syncing = false; }
    }
}
