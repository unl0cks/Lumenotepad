using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumenotepad.Editor;
using Lumenotepad.Models;
using Lumenotepad.Services;

namespace Lumenotepad.ViewModels;

public sealed record RecentPage(Notebook Notebook, Section Section, Page Page, string Ago)
{
    public string Title => string.IsNullOrWhiteSpace(Page.Title) ? "Untitled" : Page.Title;
    public string Detail => $"{Notebook.Name} · {Ago}";
    public string Color => Notebook.Color;
}

public partial class MainViewModel : ObservableObject
{

    public static readonly (string Hex, string Name)[] NotebookColors =
    {
        ("#4DA6FF", "Blue"), ("#3E9C6B", "Green"), ("#E27BA6", "Pink"),
        ("#E0BD4D", "Gold"), ("#9B7BE2", "Purple"), ("#3FAEA6", "Teal"),
    };

    public static readonly (string Family, (string Name, string Hex)[] Shades)[] NotebookPalette =
    {
        ("Red",    new[] { ("Pastel", "#F2A6A6"), ("Salmon", "#ED7E7E"),     ("Red", "#E05252"),    ("Crimson", "#C22F45"),   ("Dark red", "#8F2430") }),
        ("Orange", new[] { ("Pastel", "#F7C59F"), ("Peach", "#F2A56B"),      ("Orange", "#E88743"), ("Burnt orange", "#C96A2B"), ("Rust", "#9A4E1F") }),
        ("Yellow", new[] { ("Pastel", "#F5E3A3"), ("Sand", "#EDD37A"),       ("Gold", "#E0BD4D"),   ("Amber", "#C9A035"),     ("Bronze", "#97772A") }),
        ("Green",  new[] { ("Pastel", "#B8DDB6"), ("Mint", "#8CCB93"),       ("Green", "#3E9C6B"),  ("Forest", "#2E7D53"),    ("Evergreen", "#1F5A3C") }),
        ("Teal",   new[] { ("Pastel", "#B0DCD6"), ("Aqua", "#7CC8BE"),       ("Teal", "#3FAEA6"),   ("Deep teal", "#2E8680"), ("Pine", "#1F5F5B") }),
        ("Cyan",   new[] { ("Pastel", "#B5DDF2"), ("Sky", "#82C4EC"),        ("Cyan", "#52A9DD"),   ("Cerulean", "#3684BC"),  ("Deep cyan", "#275F88") }),
        ("Blue",   new[] { ("Pastel", "#AECBF5"), ("Cornflower", "#7FAEF0"), ("Blue", "#4DA6FF"),   ("Royal", "#2F6FD6"),     ("Navy", "#1F4A8F") }),
        ("Purple", new[] { ("Pastel", "#CBBAED"), ("Lavender", "#AE93E6"),   ("Purple", "#9B7BE2"), ("Violet", "#7A55C7"),    ("Deep purple", "#57398F") }),
        ("Pink",   new[] { ("Pastel", "#F5B8CE"), ("Rose", "#EF8FB2"),       ("Pink", "#E27BA6"),   ("Magenta", "#C7538A"),   ("Berry", "#93375F") }),
    };

    public static readonly (string Name, string Hex)[] GrayscaleShades =
    {
        ("White", "#FFFFFF"), ("Light gray", "#C6CBD2"), ("Gray", "#8B9099"),
        ("Dark gray", "#464B54"), ("Black", "#111418"),
    };

    private readonly WorkspaceStore _store;
    private readonly Workspace _workspace;

    public ObservableCollection<Notebook> Notebooks => _workspace.Notebooks;

    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Section? _selectedSection;
    [ObservableProperty] private Page? _selectedPage;
    [ObservableProperty] private bool _isRailVisible = true;
    [ObservableProperty] private bool _isPagesVisible = true;
    [ObservableProperty] private bool _isHomeVisible = true;
    [ObservableProperty] private string _toolbarPosition = "Top";
    [ObservableProperty] private string _toolbarScope = "Window";
    [ObservableProperty] private bool _resizablePages = true;
    [ObservableProperty] private bool _alwaysShowBorders;
    [ObservableProperty] private bool _deletedHistory = true;
    [ObservableProperty] private string _mindmapTidyLayout = "Radial";
    [ObservableProperty] private string _theme = "Lumen";
    [ObservableProperty] private bool _fullTheme;
    [ObservableProperty] private bool _paperLight;
    [ObservableProperty] private bool _paperDark;
    [ObservableProperty] private bool _whiteAccentText;
    [ObservableProperty] private bool _neutralDark;
    [ObservableProperty] private bool _flatCovers;
    [ObservableProperty] private bool _glossyAccents = true;
    [ObservableProperty] private bool _extendedFonts;
    [ObservableProperty] private bool _startRailVisible = true;
    [ObservableProperty] private bool _startPagesVisible = true;
    [ObservableProperty] private bool _advancedUnlocked;
    [ObservableProperty] private string? _customAccent;
    [ObservableProperty] private double _glassTint;
    [ObservableProperty] private int _macGlassMaterial;
    [ObservableProperty] private bool _autoCheckUpdates = true;
    [ObservableProperty] private double _cornerRoundness = 1.0;
    [ObservableProperty] private bool _sectionsSidebar;
    [ObservableProperty] private bool _singleMode;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] private string _motionSpeed = "Normal";
    [ObservableProperty] private bool? _numBoldDefault;
    [ObservableProperty] private bool? _numItalicDefault;
    [ObservableProperty] private bool? _numUnderlineDefault;
    [ObservableProperty] private bool? _numStrikeDefault;

    [ObservableProperty] private int _bulletPrefsVersion;

    [ObservableProperty] private int _fontPrefsVersion;

    [ObservableProperty] private string _launchTarget = "Home";
    [ObservableProperty] private int _autosaveMs = 900;
    [ObservableProperty] private bool _confirmDeleteNotebook = true;
    [ObservableProperty] private bool _confirmDeleteSection = true;
    [ObservableProperty] private bool _confirmDeletePage = true;
    [ObservableProperty] private bool _confirmDeleteContainer = true;
    [ObservableProperty] private int _recentCount = 5;
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private string? _caretColor;
    [ObservableProperty] private double _caretWidth = 1.6;
    [ObservableProperty] private bool _caretBlink = true;
    [ObservableProperty] private string _defaultHighlight = "#66FFD666";
    [ObservableProperty] private string _dateFormat = "yyyy-MM-dd";
    [ObservableProperty] private double _newNoteWidth = 360;
    [ObservableProperty] private bool _accentFollowsNotebook;
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private bool _showHomeStats = true;
    [ObservableProperty] private string _cardSize = "Medium";
    [ObservableProperty] private string? _editorFont;
    [ObservableProperty] private double _editorFontSize = 15;
    [ObservableProperty] private double _lineSpacingScale = 1.0;
    [ObservableProperty] private double _paragraphSpacingScale = 1.0;
    [ObservableProperty] private double _indentScale = 1.0;
    [ObservableProperty] private bool _smartLists = true;
    [ObservableProperty] private string _pageGrid = "None";
    [ObservableProperty] private bool _gridSnap;
    [ObservableProperty] private string? _backupFolder;
    [ObservableProperty] private int _backupEveryDays;
    [ObservableProperty] private int _backupKeep = 5;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _summonHotkey;
    [ObservableProperty] private double _pagesPanelWidth = 224;
    [ObservableProperty] private bool _doubleClickCreate;
    [ObservableProperty] private bool _roundedPdfCorners = true;

    [ObservableProperty] private int _palettePrefsVersion;

    public bool PaperLightVisible => Theme == "Lumen" ? !FullTheme : Theme == "Dark" && FullTheme;

    public bool PaperDarkVisible => IsLightTheme && FullTheme;

    public bool WhiteTextVisible => IsLightTheme;

    private bool IsLightTheme => Theme is "Light" or "Pink" or "Light blue";

    [ObservableProperty] private string _greeting = BuildGreeting("");
    [ObservableProperty] private string _homeSubtitle = "Pick up where you left off, or start something new.";
    [ObservableProperty] private bool _hasRecents;
    public ObservableCollection<RecentPage> RecentPages { get; } = new();

    private static string BuildGreeting(string name)
    {
        var now = DateTime.Now;
        var word = now.Hour < 5 ? "Up late" : now.Hour < 12 ? "Good morning"
                 : now.Hour < 18 ? "Good afternoon" : "Good evening";
        var who = string.IsNullOrWhiteSpace(name) ? "" : $", {name.Trim()}";
        return $"{word}{who} — it's {now:dddd, MMMM d}";
    }

    public void RefreshHome()
    {
        Greeting = BuildGreeting(UserName);
        RecentPages.Clear();
        var recents = Notebooks
            .SelectMany(nb => nb.Sections.SelectMany(s => s.Pages.Select(p => (nb, s, p, t: _store.PageDocTime(nb, p.Id)))))
            .Where(x => x.t is not null)
            .OrderByDescending(x => x.t)
            .Take(Math.Clamp(RecentCount, 0, 10));
        var now = DateTime.UtcNow;
        foreach (var (nb, s, p, t) in recents)
            RecentPages.Add(new RecentPage(nb, s, p, AgoLabel(now - t!.Value)));
        HasRecents = RecentPages.Count > 0;

        int pages = Notebooks.Sum(n => n.Sections.Sum(sec => sec.Pages.Count));
        HomeSubtitle = ShowHomeStats
            ? $"{Notebooks.Count} {(Notebooks.Count == 1 ? "notebook" : "notebooks")} · " +
              $"{pages} {(pages == 1 ? "page" : "pages")} — pick up where you left off."
            : "Pick up where you left off, or start something new.";
    }

    private static string AgoLabel(TimeSpan d) =>
        d.TotalMinutes < 1 ? "just now" :
        d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes}m ago" :
        d.TotalHours < 24 ? $"{(int)d.TotalHours}h ago" :
        d.TotalDays < 2 ? "yesterday" : $"{(int)d.TotalDays}d ago";

    [RelayCommand]
    private void OpenRecent(RecentPage r)
    {
        SelectedNotebook = r.Notebook;
        SelectedSection = r.Section;
        SelectedPage = r.Page;
        IsHomeVisible = false;
    }

    partial void OnIsHomeVisibleChanged(bool value)
    {
        if (value) RefreshHome();
    }

    private readonly AppSettings? _settings;
    private readonly string? _settingsDir;

    public MainViewModel() : this(new WorkspaceStore(AppSettings.DefaultDir), AppSettings.DefaultDir) { }

    public MainViewModel(WorkspaceStore store, string? settingsDir = null)
    {
        _store = store;
        _settingsDir = settingsDir;
        if (settingsDir is not null)
        {

            Services.FontInstaller.FontsDir = System.IO.Path.Combine(settingsDir, "fonts");
            _settings = AppSettings.Load(settingsDir);
            ToolbarPosition = _settings.ToolbarPosition;
            ToolbarScope = _settings.ToolbarScope;
            ResizablePages = _settings.ResizablePages;
            AlwaysShowBorders = _settings.AlwaysShowBorders;
            DeletedHistory = _settings.DeletedHistory;
            MindmapTidyLayout = _settings.MindmapTidyLayout;
            Theme = _settings.Theme;
            FullTheme = _settings.FullTheme;
            PaperLight = _settings.PaperLight;
            PaperDark = _settings.PaperDark;
            WhiteAccentText = _settings.WhiteAccentText;
            NeutralDark = _settings.NeutralDark;
            FlatCovers = _settings.FlatCovers;
            GlossyAccents = _settings.GlossyAccents;
            ExtendedFonts = _settings.ExtendedFonts;
            StartRailVisible = _settings.StartRailVisible;
            StartPagesVisible = _settings.StartPagesVisible;
            AdvancedUnlocked = _settings.AdvancedUnlocked;
            CustomAccent = _settings.CustomAccent;
            GlassTint = _settings.GlassTint;
            MacGlassMaterial = _settings.MacGlassMaterial;
            AutoCheckUpdates = _settings.AutoCheckUpdates;

            Platform.MacVibrancy.Material = MacMaterialOf(MacGlassMaterial);
            CornerRoundness = _settings.CornerRoundness;
            SectionsSidebar = _settings.SectionsSidebar;
            _singleMode = _settings.SingleMode;
            Services.Keymap.SetOverrides(_settings.KeyOverrides);
            ReduceMotion = _settings.ReduceMotion;
            MotionSpeed = _settings.MotionSpeed;
            NumBoldDefault = _settings.NumBoldDefault;
            NumItalicDefault = _settings.NumItalicDefault;
            NumUnderlineDefault = _settings.NumUnderlineDefault;
            NumStrikeDefault = _settings.NumStrikeDefault;
            IsRailVisible = _settings.StartRailVisible;
            IsPagesVisible = _settings.StartPagesVisible;
            LaunchTarget = _settings.LaunchTarget;
            AutosaveMs = _settings.AutosaveMs;
            ConfirmDeleteNotebook = _settings.ConfirmDeleteNotebook;
            ConfirmDeleteSection = _settings.ConfirmDeleteSection;
            ConfirmDeletePage = _settings.ConfirmDeletePage;
            ConfirmDeleteContainer = _settings.ConfirmDeleteContainer;
            RecentCount = _settings.RecentCount;
            AlwaysOnTop = _settings.AlwaysOnTop;
            CaretColor = _settings.CaretColor;
            CaretWidth = _settings.CaretWidth;
            CaretBlink = _settings.CaretBlink;
            DefaultHighlight = _settings.DefaultHighlight;
            DateFormat = _settings.DateFormat;
            NewNoteWidth = _settings.NewNoteWidth;
            AccentFollowsNotebook = _settings.AccentFollowsNotebook;
            UserName = _settings.UserName;
            ShowHomeStats = _settings.ShowHomeStats;
            CardSize = _settings.CardSize;
            EditorFont = _settings.EditorFont;
            EditorFontSize = _settings.EditorFontSize;
            LineSpacingScale = _settings.LineSpacingScale;
            ParagraphSpacingScale = _settings.ParagraphSpacingScale;
            IndentScale = _settings.IndentScale;
            SmartLists = _settings.SmartLists;
            PageGrid = _settings.PageGrid;
            GridSnap = _settings.GridSnap;
            BackupFolder = _settings.BackupFolder;
            BackupEveryDays = _settings.BackupEveryDays;
            BackupKeep = _settings.BackupKeep;
            CloseToTray = _settings.CloseToTray;
            MinimizeToTray = _settings.MinimizeToTray;
            SummonHotkey = _settings.SummonHotkey;
            PagesPanelWidth = _settings.PagesPanelWidth;
            DoubleClickCreate = _settings.DoubleClickCreate;
            RoundedPdfCorners = _settings.RoundedPdfCorners;
        }
        _workspace = store.LoadOrSeed();

        var lastPageId = _settings is { LaunchTarget: "LastPage" } ? _settings.LastPageId : null;
        SelectedNotebook = Notebooks.FirstOrDefault();
        if (!string.IsNullOrEmpty(lastPageId))
        {
            var hit = Notebooks
                .SelectMany(nb => nb.Sections.SelectMany(s => s.Pages.Select(p => (nb, s, p))))
                .FirstOrDefault(x => x.p.Id == lastPageId);
            if (hit.p is not null)
            {
                SelectedNotebook = hit.nb;
                SelectedSection = hit.s;
                SelectedPage = hit.p;
                IsHomeVisible = false;
            }
        }
        RefreshHome();

        if (BackupDue())
        {
            FlushDirtyDocs();
            System.Threading.Tasks.Task.Run(RunBackupNow);
        }
    }

    partial void OnToolbarPositionChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ToolbarPosition = value;
        _settings.Save(_settingsDir);
    }

    partial void OnToolbarScopeChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ToolbarScope = value;
        _settings.Save(_settingsDir);
    }

    partial void OnResizablePagesChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ResizablePages = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAlwaysShowBordersChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AlwaysShowBorders = value;
        _settings.Save(_settingsDir);
    }

    partial void OnDeletedHistoryChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.DeletedHistory = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMindmapTidyLayoutChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.MindmapTidyLayout = value;
        _settings.Save(_settingsDir);
    }

    partial void OnThemeChanged(string value)
    {
        OnPropertyChanged(nameof(PaperLightVisible));
        OnPropertyChanged(nameof(PaperDarkVisible));
        OnPropertyChanged(nameof(WhiteTextVisible));
        if (_settings is null || _settingsDir is null) return;
        _settings.Theme = value;
        _settings.Save(_settingsDir);
    }

    partial void OnFullThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(PaperLightVisible));
        OnPropertyChanged(nameof(PaperDarkVisible));
        if (_settings is null || _settingsDir is null) return;
        _settings.FullTheme = value;
        _settings.Save(_settingsDir);
    }

    partial void OnPaperLightChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PaperLight = value;
        _settings.Save(_settingsDir);
    }

    partial void OnPaperDarkChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PaperDark = value;
        _settings.Save(_settingsDir);
    }

    partial void OnWhiteAccentTextChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.WhiteAccentText = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNeutralDarkChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NeutralDark = value;
        _settings.Save(_settingsDir);
    }

    partial void OnFlatCoversChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.FlatCovers = value;
        _settings.Save(_settingsDir);
    }

    partial void OnGlossyAccentsChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.GlossyAccents = value;
        _settings.Save(_settingsDir);
    }

    partial void OnExtendedFontsChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ExtendedFonts = value;
        _settings.Save(_settingsDir);
    }

    partial void OnStartRailVisibleChanged(bool value)
    {
        IsRailVisible = value;
        if (_settings is null || _settingsDir is null) return;
        _settings.StartRailVisible = value;
        _settings.Save(_settingsDir);
    }

    partial void OnStartPagesVisibleChanged(bool value)
    {
        IsPagesVisible = value;
        if (_settings is null || _settingsDir is null) return;
        _settings.StartPagesVisible = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAdvancedUnlockedChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AdvancedUnlocked = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCustomAccentChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CustomAccent = value;
        _settings.Save(_settingsDir);
    }

    partial void OnGlassTintChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.GlassTint = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AutoCheckUpdates = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMacGlassMaterialChanged(int value)
    {
        Platform.MacVibrancy.Material = MacMaterialOf(value);
        if (_settings is null || _settingsDir is null) return;
        _settings.MacGlassMaterial = value;
        _settings.Save(_settingsDir);
    }

    internal static nint MacMaterialOf(int choice) => choice switch
    {
        1 => 21,
        2 => 7,
        3 => 6,
        4 => 0,
        _ => 13,
    };

    partial void OnSectionsSidebarChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.SectionsSidebar = value;
        _settings.Save(_settingsDir);
    }

    partial void OnSingleModeChanged(bool value)
    {
        if (_settings is not null) _settings.SingleMode = value;
        RestructureForSingleMode(value);
        if (_settingsDir is not null) Save();
    }

    private void RestructureForSingleMode(bool single)
    {
        foreach (var nb in Notebooks)
        {
            if (single)
            {
                if (nb.Sections.Count == 0) { nb.Sections.Add(new Section { Name = "Pages" }); }
                var first = nb.Sections[0];
                for (int i = nb.Sections.Count - 1; i >= 1; i--)
                {
                    foreach (var p in nb.Sections[i].Pages.ToList()) first.Pages.Add(p);
                    nb.Sections.RemoveAt(i);
                }
                if (first.Pages.Count == 0) first.Pages.Add(new Page { Title = "Untitled page" });
            }
            else
            {
                var pages = nb.Sections.SelectMany(s => s.Pages).ToList();
                nb.Sections.Clear();
                if (pages.Count == 0)
                {
                    var s = new Section { Name = "New section" };
                    s.Pages.Add(new Page { Title = "Untitled page" });
                    nb.Sections.Add(s);
                }
                else
                    foreach (var p in pages)
                    {
                        var s = new Section { Name = string.IsNullOrWhiteSpace(p.Title) ? "New section" : p.Title };
                        s.Pages.Add(p);
                        nb.Sections.Add(s);
                    }
            }
        }
        SelectedSection = SelectedNotebook?.Sections.FirstOrDefault();
        SelectedPage = SelectedSection?.Pages.FirstOrDefault();
    }

    partial void OnCornerRoundnessChanged(double value)
    {

        Editor.NoteCanvas.NoteRadiusPref = 9 * value;
        if (Avalonia.Application.Current is { } app) Services.ThemeManager.PushRoundness(app, value);
        if (_settings is null || _settingsDir is null) return;
        _settings.CornerRoundness = value;
        _settings.Save(_settingsDir);
    }

    public void SetKeyBinding(string action, string? gesture)
    {
        if (_settings is null || _settingsDir is null) return;
        if (gesture is null) _settings.KeyOverrides.Remove(action);
        else _settings.KeyOverrides[action] = gesture;
        _settings.Save(_settingsDir);
        Services.Keymap.SetOverrides(_settings.KeyOverrides);
    }

    public void ResetKeyBindings()
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.KeyOverrides.Clear();
        _settings.Save(_settingsDir);
        Services.Keymap.SetOverrides(null);
    }

    partial void OnReduceMotionChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ReduceMotion = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMotionSpeedChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.MotionSpeed = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumBoldDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumBoldDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumItalicDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumItalicDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumUnderlineDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumUnderlineDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNumStrikeDefaultChanged(bool? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NumStrikeDefault = value;
        _settings.Save(_settingsDir);
    }

    partial void OnLaunchTargetChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.LaunchTarget = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAutosaveMsChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AutosaveMs = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteNotebookChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteNotebook = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteSectionChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteSection = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeletePageChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeletePage = value;
        _settings.Save(_settingsDir);
    }

    partial void OnConfirmDeleteContainerChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ConfirmDeleteContainer = value;
        _settings.Save(_settingsDir);
    }

    partial void OnRecentCountChanged(int value)
    {

        if (_workspace is null) return;
        RefreshHome();
        if (_settings is null || _settingsDir is null) return;
        _settings.RecentCount = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AlwaysOnTop = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCaretColorChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CaretColor = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCaretWidthChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CaretWidth = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCaretBlinkChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CaretBlink = value;
        _settings.Save(_settingsDir);
    }

    partial void OnDefaultHighlightChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.DefaultHighlight = value;
        _settings.Save(_settingsDir);
    }

    partial void OnDateFormatChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.DateFormat = value;
        _settings.Save(_settingsDir);
    }

    partial void OnNewNoteWidthChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.NewNoteWidth = value;
        _settings.Save(_settingsDir);
    }

    partial void OnAccentFollowsNotebookChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.AccentFollowsNotebook = value;
        _settings.Save(_settingsDir);
    }

    partial void OnUserNameChanged(string value)
    {
        if (_workspace is not null) Greeting = BuildGreeting(value);
        if (_settings is null || _settingsDir is null) return;
        _settings.UserName = value;
        _settings.Save(_settingsDir);
    }

    partial void OnShowHomeStatsChanged(bool value)
    {
        if (_workspace is not null) RefreshHome();
        if (_settings is null || _settingsDir is null) return;
        _settings.ShowHomeStats = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCardSizeChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CardSize = value;
        _settings.Save(_settingsDir);
    }

    partial void OnEditorFontChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.EditorFont = value;
        _settings.Save(_settingsDir);
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.EditorFontSize = value;
        _settings.Save(_settingsDir);
    }

    partial void OnLineSpacingScaleChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.LineSpacingScale = value;
        _settings.Save(_settingsDir);
    }

    partial void OnParagraphSpacingScaleChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.ParagraphSpacingScale = value;
        _settings.Save(_settingsDir);
    }

    partial void OnIndentScaleChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.IndentScale = value;
        _settings.Save(_settingsDir);
    }

    partial void OnSmartListsChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.SmartLists = value;
        _settings.Save(_settingsDir);
    }

    partial void OnPageGridChanged(string value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PageGrid = value;
        _settings.Save(_settingsDir);
    }

    partial void OnGridSnapChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.GridSnap = value;
        _settings.Save(_settingsDir);
    }

    partial void OnBackupFolderChanged(string? value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupFolder = value;
        _settings.Save(_settingsDir);
    }

    partial void OnBackupEveryDaysChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupEveryDays = value;
        _settings.Save(_settingsDir);
    }

    partial void OnBackupKeepChanged(int value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.BackupKeep = value;
        _settings.Save(_settingsDir);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CloseToTray = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.MinimizeToTray = value;
        _settings.Save(_settingsDir);
    }

    partial void OnSummonHotkeyChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.SummonHotkey = value;
        _settings.Save(_settingsDir);
    }

    partial void OnPagesPanelWidthChanged(double value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.PagesPanelWidth = value;
        _settings.Save(_settingsDir);
    }

    partial void OnDoubleClickCreateChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.DoubleClickCreate = value;
        _settings.Save(_settingsDir);
    }

    partial void OnRoundedPdfCornersChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.RoundedPdfCorners = value;
        _settings.Save(_settingsDir);
    }

    public void SortNotebooksByName() =>
        Reorder(Notebooks.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList());

    public void SortNotebooksByRecent() =>
        Reorder(Notebooks.OrderByDescending(LatestEdit).ToList());

    public void MoveNotebook(Notebook nb, int delta)
    {
        int i = Notebooks.IndexOf(nb);
        if (i < 0) return;
        int j = Math.Clamp(i + delta, 0, Notebooks.Count - 1);
        if (i == j) return;
        Notebooks.Move(i, j);
        Save();
    }

    public void MoveNotebookTo(Notebook nb, int index, bool save)
    {
        int i = Notebooks.IndexOf(nb);
        index = Math.Clamp(index, 0, Notebooks.Count - 1);
        if (i >= 0 && i != index) Notebooks.Move(i, index);
        if (save) Save();
    }

    private DateTime LatestEdit(Notebook nb) =>
        nb.Sections.SelectMany(s => s.Pages)
          .Select(p => _store.PageDocTime(nb, p.Id) ?? DateTime.MinValue)
          .DefaultIfEmpty(DateTime.MinValue).Max();

    private void Reorder(List<Notebook> order)
    {
        for (int i = 0; i < order.Count; i++)
        {
            int cur = Notebooks.IndexOf(order[i]);
            if (cur != i) Notebooks.Move(cur, i);
        }
        Save();
    }

    public void Save() => _store.Save(_workspace);

    public string? SettingsDir => _settingsDir;

    public (double W, double H) CanvasViewport { get; set; } = (900, 600);

    public string? BulletColorFor(string style) =>
        _settings is not null && _settings.BulletColors.TryGetValue(style, out var hex) ? hex : null;

    public void SetBulletColor(string style, string? hex)
    {
        if (_settings is null || _settingsDir is null) return;
        if (hex is null) _settings.BulletColors.Remove(style);
        else _settings.BulletColors[style] = hex;
        _settings.Save(_settingsDir);
        BulletPrefsVersion++;
    }

    public IReadOnlyList<string> PaletteFor(bool highlight, IReadOnlyList<string> builtIns) =>
        _settings is { } s && (highlight ? s.HighlightPalette : s.TextPalette) is { Count: > 0 } list
            ? list : builtIns;

    public void AddPaletteColor(bool highlight, string hex, IReadOnlyList<string> builtIns)
    {
        if (_settings is null || _settingsDir is null) return;
        var list = highlight ? _settings.HighlightPalette : _settings.TextPalette;
        if (list.Count == 0) list.AddRange(builtIns);
        if (!list.Contains(hex, StringComparer.OrdinalIgnoreCase)) list.Add(hex);
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }

    public void RemovePaletteColor(bool highlight, string hex, IReadOnlyList<string> builtIns)
    {
        if (_settings is null || _settingsDir is null) return;
        var list = highlight ? _settings.HighlightPalette : _settings.TextPalette;
        if (list.Count == 0) list.AddRange(builtIns);
        if (list.Count <= 1) return;
        list.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }

    public void ResetPalette(bool highlight)
    {
        if (_settings is null || _settingsDir is null) return;
        (highlight ? _settings.HighlightPalette : _settings.TextPalette).Clear();
        _settings.Save(_settingsDir);
        PalettePrefsVersion++;
    }

    public IReadOnlyCollection<string> DisabledFontsList =>
        (IReadOnlyCollection<string>?)_settings?.DisabledFonts ?? System.Array.Empty<string>();

    public bool IsFontEnabled(string name) =>
        _settings is null ||
        !_settings.DisabledFonts.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void SetFontEnabled(string name, bool enabled)
    {
        if (_settings is null || _settingsDir is null) return;
        if (enabled) _settings.DisabledFonts.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        else if (IsFontEnabled(name)) _settings.DisabledFonts.Add(name);
        _settings.Save(_settingsDir);
        FontPrefsVersion++;
    }

    public void ResetSettingsToDefaults()
    {
        var d = new AppSettings();
        Theme = d.Theme; FullTheme = d.FullTheme; PaperLight = d.PaperLight;
        PaperDark = d.PaperDark; WhiteAccentText = d.WhiteAccentText; NeutralDark = d.NeutralDark;
        FlatCovers = d.FlatCovers; GlossyAccents = d.GlossyAccents; ExtendedFonts = d.ExtendedFonts;
        ToolbarPosition = d.ToolbarPosition; ToolbarScope = d.ToolbarScope;
        ResizablePages = d.ResizablePages; DeletedHistory = d.DeletedHistory;
        AlwaysShowBorders = d.AlwaysShowBorders;
        MindmapTidyLayout = d.MindmapTidyLayout;
        StartRailVisible = d.StartRailVisible; StartPagesVisible = d.StartPagesVisible;
        AdvancedUnlocked = d.AdvancedUnlocked;
        CustomAccent = d.CustomAccent;
        GlassTint = d.GlassTint;
        MacGlassMaterial = d.MacGlassMaterial;
        AutoCheckUpdates = d.AutoCheckUpdates;
        CornerRoundness = d.CornerRoundness;
        SectionsSidebar = d.SectionsSidebar;
        ReduceMotion = d.ReduceMotion; MotionSpeed = d.MotionSpeed;
        NumBoldDefault = d.NumBoldDefault; NumItalicDefault = d.NumItalicDefault;
        NumUnderlineDefault = d.NumUnderlineDefault; NumStrikeDefault = d.NumStrikeDefault;
        LaunchTarget = d.LaunchTarget; AutosaveMs = d.AutosaveMs;
        ConfirmDeleteNotebook = d.ConfirmDeleteNotebook; ConfirmDeleteSection = d.ConfirmDeleteSection;
        ConfirmDeletePage = d.ConfirmDeletePage; ConfirmDeleteContainer = d.ConfirmDeleteContainer;
        RecentCount = d.RecentCount; AlwaysOnTop = d.AlwaysOnTop;
        CaretColor = d.CaretColor; CaretWidth = d.CaretWidth; CaretBlink = d.CaretBlink;
        DefaultHighlight = d.DefaultHighlight; DateFormat = d.DateFormat;
        NewNoteWidth = d.NewNoteWidth; AccentFollowsNotebook = d.AccentFollowsNotebook;
        UserName = d.UserName; ShowHomeStats = d.ShowHomeStats; CardSize = d.CardSize;
        EditorFont = d.EditorFont; EditorFontSize = d.EditorFontSize;
        LineSpacingScale = d.LineSpacingScale; ParagraphSpacingScale = d.ParagraphSpacingScale;
        IndentScale = d.IndentScale; SmartLists = d.SmartLists;
        PageGrid = d.PageGrid; GridSnap = d.GridSnap;
        BackupFolder = d.BackupFolder; BackupEveryDays = d.BackupEveryDays; BackupKeep = d.BackupKeep;
        CloseToTray = d.CloseToTray; MinimizeToTray = d.MinimizeToTray; SummonHotkey = d.SummonHotkey;
        PagesPanelWidth = d.PagesPanelWidth; DoubleClickCreate = d.DoubleClickCreate;
        RoundedPdfCorners = d.RoundedPdfCorners;
        if (_settings is not null && _settingsDir is not null && _settings.BulletColors.Count > 0)
        {
            _settings.BulletColors.Clear();
            _settings.Save(_settingsDir);
            BulletPrefsVersion++;
        }
        if (_settings is not null && _settingsDir is not null && _settings.DisabledFonts.Count > 0)
        {
            _settings.DisabledFonts.Clear();
            _settings.Save(_settingsDir);
            FontPrefsVersion++;
        }
        if (_settings is not null && _settingsDir is not null
            && (_settings.HighlightPalette.Count > 0 || _settings.TextPalette.Count > 0))
        {
            _settings.HighlightPalette.Clear();
            _settings.TextPalette.Clear();
            _settings.Save(_settingsDir);
            PalettePrefsVersion++;
        }
        ResetKeyBindings();
    }

    private readonly Dictionary<string, (CanvasDocument Doc, Notebook Owner)> _docs = new();
    private readonly HashSet<string> _dirty = new();

    public event Action? DocsDirtied;

    public CanvasDocument DocumentFor(Page page)
    {
        if (_docs.TryGetValue(page.Id, out var entry)) return entry.Doc;
        var owner = FindOwner(page) ?? SelectedNotebook ?? Notebooks.First();
        var doc = _store.LoadPageDoc(owner, page.Id) ?? new CanvasDocument();
        doc.Changed += () => { _dirty.Add(page.Id); DocsDirtied?.Invoke(); };
        _docs[page.Id] = (doc, owner);
        return doc;
    }

    public System.Collections.Generic.List<(string Tag, string Text, Section Section, Page Page)> CollectTaggedLines()
    {
        var result = new System.Collections.Generic.List<(string, string, Section, Page)>();
        if (SelectedNotebook is not { } nb) return result;
        foreach (var sec in nb.Sections)
            foreach (var pg in sec.Pages)
                foreach (var box in DocumentFor(pg).Boxes)
                    foreach (var p in box.Doc.Paragraphs)
                        if (p.Tag is { Length: > 0 } t)
                            result.Add((t, p.Text, sec, pg));
        return result;
    }

    public void FlushDirtyDocs()
    {
        foreach (var id in _dirty.ToList())
        {
            if (_docs.TryGetValue(id, out var entry))
                _store.SavePageDoc(entry.Owner, id, entry.Doc);
        }
        _dirty.Clear();
    }

    private Notebook? FindOwner(Page page) =>
        Notebooks.FirstOrDefault(nb => nb.Sections.Any(s => s.Pages.Contains(page)));

    private void ForgetPageDoc(Page page, bool deleteFile)
    {
        if (_docs.TryGetValue(page.Id, out var entry) && deleteFile)
            _store.DeletePageDoc(entry.Owner, page.Id);
        else if (deleteFile && FindOwner(page) is { } owner)
            _store.DeletePageDoc(owner, page.Id);
        _docs.Remove(page.Id);
        _dirty.Remove(page.Id);
    }

    partial void OnSelectedNotebookChanged(Notebook? value) => SelectedSection = value?.Sections.FirstOrDefault();

    partial void OnSelectedSectionChanged(Section? value)
    {
        if (value is null && SelectedNotebook is { Sections.Count: > 0 } nb)
        {
            SelectedSection = nb.Sections[0];
            return;
        }
        SelectedPage = value?.Pages.FirstOrDefault();
    }

    partial void OnSelectedPageChanged(Page? value)
    {
        if (value is null && SelectedSection is { Pages.Count: > 0 } sec)
            SelectedPage = sec.Pages[0];

        if (value is not null && _settings is not null && _settingsDir is not null)
        {
            _settings.LastPageId = value.Id;
            _settings.Save(_settingsDir);
        }
    }

    [RelayCommand]
    private void AddNotebook()
    {
        var nb = new Notebook { Name = "New notebook", Color = NotebookColors[Notebooks.Count % NotebookColors.Length].Hex };
        var sec = new Section { Name = "Notes" };
        sec.Pages.Add(new Page { Title = "Untitled page" });
        nb.Sections.Add(sec);
        Notebooks.Add(nb);
        SelectedNotebook = nb;
        IsHomeVisible = false;
        Save();
    }

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
                sec.Pages.Add(new Page { Title = "Untitled page" });
            nb.Sections.Add(sec);
        }
        Notebooks.Add(nb);
        Save();
        if (draft.CoverSourcePath is { } cover) SetNotebookCover(nb, cover);
        foreach (var page in nb.Sections.SelectMany(s => s.Pages))
            StampPageStyle(page);
        SelectedNotebook = nb;
        IsHomeVisible = false;
        return nb;
    }

    public void ApplyNotebookCustomization(Notebook nb, NotebookDraft draft)
    {
        FlushDirtyDocs();

        if (!string.IsNullOrWhiteSpace(draft.Name)) nb.Name = draft.Name.Trim();
        nb.Color = draft.Color;
        nb.DefaultGridStyle = draft.DefaultGridStyle;
        nb.DefaultPageStyle = draft.DefaultPageStyle;
        nb.DefaultPageStyleMode = draft.DefaultPageStyleMode;
        nb.DefaultFont = draft.DefaultFont;
        nb.DefaultFontSize = draft.DefaultFontSize;

        if (draft.Sections.Count == 0)
            draft.Sections.Add(new SectionDraft { Name = "Notes", PageTitles = { "Untitled page" } });

        var target = new List<Section>();
        var born = new List<Page>();
        foreach (var sd in draft.Sections)
        {
            var sec = sd.Source ?? new Section();
            if (!string.IsNullOrWhiteSpace(sd.Name)) sec.Name = sd.Name.Trim();
            else if (sd.Source is null) sec.Name = "Section";

            var pages = new List<Page>();
            for (int i = 0; i < sd.PageTitles.Count; i++)
            {
                var pg = sd.SourceAt(i);
                if (pg is null) { pg = new Page(); born.Add(pg); }
                var title = sd.PageTitles[i];
                if (!string.IsNullOrWhiteSpace(title)) pg.Title = title.Trim();
                else if (string.IsNullOrWhiteSpace(pg.Title)) pg.Title = "Untitled page";
                pages.Add(pg);
            }
            if (pages.Count == 0 && draft.Sections.Count == 1)
            {
                var pg = new Page { Title = "Untitled page" };
                born.Add(pg);
                pages.Add(pg);
            }

            foreach (var gone in sec.Pages.Where(p => !pages.Contains(p)).ToList())
                ForgetPageDoc(gone, deleteFile: true);
            if (!sec.Pages.SequenceEqual(pages))
            {
                sec.Pages.Clear();
                foreach (var p in pages) sec.Pages.Add(p);
            }
            target.Add(sec);
        }

        foreach (var gone in nb.Sections.Where(s => !target.Contains(s)).ToList())
            foreach (var page in gone.Pages) ForgetPageDoc(page, deleteFile: true);
        if (!nb.Sections.SequenceEqual(target))
        {
            nb.Sections.Clear();
            foreach (var s in target) nb.Sections.Add(s);
        }

        if (ReferenceEquals(SelectedNotebook, nb))
        {
            if (SelectedSection is null || !nb.Sections.Contains(SelectedSection))
                SelectedSection = nb.Sections.FirstOrDefault();
            if (SelectedPage is null || SelectedSection is null || !SelectedSection.Pages.Contains(SelectedPage))
                SelectedPage = SelectedSection?.Pages.FirstOrDefault();
        }

        Save();
        foreach (var pg in born) StampPageStyle(pg);

        if (draft.CoverSourcePath is null)
        {
            if (!string.IsNullOrEmpty(nb.Cover)) ClearNotebookCover(nb);
        }
        else if (!string.Equals(draft.CoverSourcePath, nb.CoverPath, StringComparison.OrdinalIgnoreCase))
        {
            SetNotebookCover(nb, draft.CoverSourcePath);
        }
    }

    public void ApplySectionStyle(Section sec, bool setStyle, string? style, int mode, bool setGrid, string? grid)
    {
        if (!setStyle && !setGrid) { Save(); return; }
        foreach (var pg in sec.Pages)
        {
            if (setStyle)
            {
                pg.PageStyle = style;
                if (style is not null) pg.PageStyleMode = mode;
            }
            if (setGrid) pg.GridStyle = grid;
        }
        Save();
        if (setStyle && style is not null && style != Editor.PageStyles.Freeform)
            foreach (var pg in sec.Pages)
                if (DocumentFor(pg).Boxes.Count == 0)
                    StampPageStyle(pg);
    }

    [RelayCommand]
    private void OpenNotebook(Notebook nb)
    {
        SelectedNotebook = nb;
        IsHomeVisible = false;
    }

    [RelayCommand]
    private void GoHome()
    {
        FlushDirtyDocs();
        IsHomeVisible = true;
    }

    public void SetNotebookColor(Notebook nb, string hex)
    {
        nb.Color = hex;
        Save();
    }

    public string? SelectedNotebookDir => SelectedNotebook is { } nb ? _store.NotebookDir(nb) : null;

    public string? ImportPageImage(string sourcePath) =>
        SelectedNotebook is { } nb ? _store.SavePageImage(nb, sourcePath) : null;

    public string? ImportPageAsset(string sourcePath) =>
        SelectedNotebook is { } nb ? _store.SavePageAsset(nb, sourcePath) : null;

    public void SetNotebookCover(Notebook nb, string sourcePath)
    {
        var dest = _store.SaveCover(nb, sourcePath);
        if (dest is null) return;
        nb.Cover = System.IO.Path.GetFileName(dest);
        nb.CoverPath = dest;
        Save();
    }

    public void ClearNotebookCover(Notebook nb)
    {
        _store.DeleteCover(nb);
        nb.Cover = "";
        nb.CoverPath = null;
        Save();
    }

    public void SetNotebookPaperTint(Notebook nb, string? hex)
    {
        nb.PaperTint = hex;
        Save();
    }

    public void SetPageGridStyle(Page pg, string? gridStyle)
    {
        pg.GridStyle = gridStyle;
        Save();
    }

    public void SetPageStyleChoice(Page pg, string? pageStyle, int? mode = null)
    {
        pg.PageStyle = pageStyle;
        if (mode is { } m) pg.PageStyleMode = m;
        Save();
    }

    public void StampPageStyle(Page page)
    {
        var owner = FindOwner(page) ?? SelectedNotebook;
        if (owner is null) return;
        var (style, mode) = Editor.PageStyles.EffectiveStyle(page, owner);
        var starters = Editor.PageStyleTemplate.StartersFor(style, mode,
            new Avalonia.Size(CanvasViewport.W, CanvasViewport.H));
        if (starters.Count == 0) return;
        var doc = DocumentFor(page);
        foreach (var b in starters)
        {
            var added = doc.AddBox(b.X, b.Y, b.Width, b.Doc);
            added.H = b.H;
            added.Locked = b.Locked;
        }
        FlushDirtyDocs();
    }

    public System.DateTime? LastBackupUtc => _settings?.LastBackupUtc;

    public bool BackupDue() =>
        _settings is { BackupFolder: { Length: > 0 } folder } s &&
        BackupService.IsDue(s.LastBackupUtc, s.BackupEveryDays, System.DateTime.UtcNow);

    public string? RunBackupNow()
    {
        if (_settings is not { BackupFolder: { Length: > 0 } folder } s || _settingsDir is null) return null;
        var now = System.DateTime.UtcNow;
        var zip = BackupService.CreateBackup(_settingsDir, folder, now);
        BackupService.PruneBackups(folder, s.BackupKeep);
        s.LastBackupUtc = now;
        s.Save(_settingsDir);
        return zip;
    }

    public Page? ImportTextAsPage(string title, string text)
    {
        if (SelectedSection is not { } sec) return null;
        var page = new Page { Title = string.IsNullOrWhiteSpace(title) ? "Imported" : title.Trim() };
        sec.Pages.Add(page);
        var doc = DocumentFor(page);
        doc.AddBox(40, 40, NoteBox.DefaultWidth, PlainTextDoc(text));
        SelectedPage = page;
        Save();
        FlushDirtyDocs();
        return page;
    }

    private static Editor.RichDocument PlainTextDoc(string text)
    {
        var d = new Editor.RichDocument();
        d.Paragraphs.Clear();
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            d.Paragraphs.Add(new Editor.Paragraph { Runs = { new Editor.RichRun { Text = line } } });
        if (d.Paragraphs.Count == 0) d.Paragraphs.Add(new Editor.Paragraph());
        return d;
    }

    public readonly record struct ExportPage(string Folder, string NotebookName, string SectionName, string PageId, string PageTitle);

    public IReadOnlyList<ExportPage> SnapshotExport()
    {
        FlushDirtyDocs();
        var list = new List<ExportPage>();
        foreach (var nb in Notebooks)
            foreach (var sec in nb.Sections)
                foreach (var page in sec.Pages)
                    list.Add(new ExportPage(nb.Folder, nb.Name, sec.Name, page.Id, page.Title));
        return list;
    }

    public int WriteExport(IReadOnlyList<ExportPage> pages, string destFolder)
    {
        int count = 0;
        var utf8 = new UTF8Encoding(false);
        var usedPerDir = new Dictionary<string, HashSet<string>>();
        foreach (var p in pages)
        {
            var secDir = Path.Combine(destFolder,
                MarkdownExport.SafeName(p.NotebookName), MarkdownExport.SafeName(p.SectionName));
            Directory.CreateDirectory(secDir);
            if (!usedPerDir.TryGetValue(secDir, out var used))
                usedPerDir[secDir] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var doc = _store.LoadPageDoc(p.Folder, p.PageId) ?? new Editor.CanvasDocument();
            var file = UniqueFile(used, MarkdownExport.SafeName(p.PageTitle));
            File.WriteAllText(Path.Combine(secDir, file + ".md"),
                MarkdownExport.PageToMarkdown(p.PageTitle, doc), utf8);
            count++;
        }
        return count;
    }

    public int ExportAllNotebooks(string destFolder) => WriteExport(SnapshotExport(), destFolder);

    private static string UniqueFile(HashSet<string> used, string name)
    {
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    [RelayCommand]
    private void AddSection()
    {
        if (SelectedNotebook is not { } nb) return;
        var sec = new Section { Name = "New section" };
        sec.Pages.Add(new Page { Title = "Untitled page" });
        nb.Sections.Add(sec);
        SelectedSection = sec;
        Save();
    }

    [RelayCommand]
    private void AddPage()
    {
        if (SelectedSection is not { } sec) return;
        var pg = new Page { Title = "Untitled page" };
        sec.Pages.Add(pg);

        StampPageStyle(pg);
        SelectedPage = pg;
        Save();
    }

    [RelayCommand]
    private void DeleteNotebook(Notebook? nb)
    {
        nb ??= SelectedNotebook;
        if (nb is null) return;
        foreach (var page in nb.Sections.SelectMany(s => s.Pages))
            ForgetPageDoc(page, deleteFile: false);
        int idx = Notebooks.IndexOf(nb);
        Notebooks.Remove(nb);
        _store.DeleteNotebook(nb);
        SelectedNotebook = Notebooks.ElementAtOrDefault(Math.Max(0, idx - 1));
        Save();
    }

    [RelayCommand]
    private void DeleteSection(Section? sec)
    {
        sec ??= SelectedSection;
        if (sec is null || SelectedNotebook is not { } nb) return;
        foreach (var page in sec.Pages) ForgetPageDoc(page, deleteFile: true);
        int idx = nb.Sections.IndexOf(sec);
        nb.Sections.Remove(sec);
        SelectedSection = nb.Sections.ElementAtOrDefault(Math.Max(0, idx - 1));
        Save();
    }

    [RelayCommand]
    private void DeletePage(Page? pg)
    {
        pg ??= SelectedPage;
        if (pg is null || SelectedSection is not { } sec) return;
        ForgetPageDoc(pg, deleteFile: true);
        int idx = sec.Pages.IndexOf(pg);
        sec.Pages.Remove(pg);
        SelectedPage = sec.Pages.ElementAtOrDefault(Math.Max(0, idx - 1));
        Save();
    }

    [RelayCommand] private void ToggleRail() => IsRailVisible = !IsRailVisible;
    [RelayCommand] private void TogglePages() => IsPagesVisible = !IsPagesVisible;
}
