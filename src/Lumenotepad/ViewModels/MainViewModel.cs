using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumenotepad.Editor;
using Lumenotepad.Models;
using Lumenotepad.Services;

namespace Lumenotepad.ViewModels;

public partial class MainViewModel : ObservableObject
{
    /// <summary>Default covers cycled onto new notebooks (the base shade of some palette families).</summary>
    public static readonly (string Hex, string Name)[] NotebookColors =
    {
        ("#4DA6FF", "Blue"), ("#3E9C6B", "Green"), ("#E27BA6", "Pink"),
        ("#E0BD4D", "Gold"), ("#9B7BE2", "Purple"), ("#3FAEA6", "Teal"),
    };

    /// <summary>The gallery's Color menu: 9 hue families, 5 shades each (pastel → dark).
    /// The family's own swatch shows the middle (base) shade.</summary>
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

    private readonly WorkspaceStore _store;
    private readonly Workspace _workspace;

    public ObservableCollection<Notebook> Notebooks => _workspace.Notebooks;

    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Section? _selectedSection;
    [ObservableProperty] private Page? _selectedPage;
    [ObservableProperty] private bool _isRailVisible = true;
    [ObservableProperty] private bool _isPagesVisible = true;
    [ObservableProperty] private bool _isHomeVisible = true;       // launch lands on the notebook gallery
    [ObservableProperty] private string _toolbarPosition = "Top";   // "Top" | "Left" | "Right" | "Bottom"
    [ObservableProperty] private string _toolbarScope = "Window";   // "Window" | "Page"
    [ObservableProperty] private bool _resizablePages = true;       // future prefs: "Resizable pages"
    [ObservableProperty] private bool _deletedHistory = true;       // future prefs: "Deleted pages history"

    private readonly AppSettings? _settings;
    private readonly string? _settingsDir;

    // Designer / default: use the portable userdata folder beside the exe.
    public MainViewModel() : this(new WorkspaceStore(AppSettings.DefaultDir), AppSettings.DefaultDir) { }

    public MainViewModel(WorkspaceStore store, string? settingsDir = null)
    {
        _store = store;
        _settingsDir = settingsDir;
        if (settingsDir is not null)
        {
            _settings = AppSettings.Load(settingsDir);
            ToolbarPosition = _settings.ToolbarPosition;
            ToolbarScope = _settings.ToolbarScope;
            ResizablePages = _settings.ResizablePages;
            DeletedHistory = _settings.DeletedHistory;
        }
        _workspace = store.LoadOrSeed();
        SelectedNotebook = Notebooks.FirstOrDefault();
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

    partial void OnDeletedHistoryChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.DeletedHistory = value;
        _settings.Save(_settingsDir);
    }

    /// <summary>Persist the whole tree (called after every structural change / rename).</summary>
    public void Save() => _store.Save(_workspace);

    // Per-page canvas documents: loaded from disk on first access, dirty-tracked on edit, saved by
    // FlushDirtyDocs (the view debounces it while typing; page switch and window close flush too).
    private readonly Dictionary<string, (CanvasDocument Doc, Notebook Owner)> _docs = new();
    private readonly HashSet<string> _dirty = new();

    /// <summary>Raised whenever any page document changes — the view uses it to debounce an autosave.</summary>
    public event Action? DocsDirtied;

    /// <summary>The canvas document for a page (loaded from its notebook folder on first access).</summary>
    public CanvasDocument DocumentFor(Page page)
    {
        if (_docs.TryGetValue(page.Id, out var entry)) return entry.Doc;
        var owner = FindOwner(page) ?? SelectedNotebook ?? Notebooks.First();
        var doc = _store.LoadPageDoc(owner, page.Id) ?? new CanvasDocument();
        doc.Changed += () => { _dirty.Add(page.Id); DocsDirtied?.Invoke(); };
        _docs[page.Id] = (doc, owner);
        return doc;
    }

    /// <summary>Write every dirty page document to disk.</summary>
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

    /// <summary>Drop cached/dirty state (and optionally the file) for pages that no longer exist.</summary>
    private void ForgetPageDoc(Page page, bool deleteFile)
    {
        if (_docs.TryGetValue(page.Id, out var entry) && deleteFile)
            _store.DeletePageDoc(entry.Owner, page.Id);
        else if (deleteFile && FindOwner(page) is { } owner)
            _store.DeletePageDoc(owner, page.Id);
        _docs.Remove(page.Id);
        _dirty.Remove(page.Id);
    }

    // Selecting a notebook drops into its first section; selecting a section drops into its first page.
    partial void OnSelectedNotebookChanged(Notebook? value) => SelectedSection = value?.Sections.FirstOrDefault();
    partial void OnSelectedSectionChanged(Section? value) => SelectedPage = value?.Pages.FirstOrDefault();

    [RelayCommand]
    private void AddNotebook()
    {
        var nb = new Notebook { Name = "New notebook", Color = NotebookColors[Notebooks.Count % NotebookColors.Length].Hex };
        var sec = new Section { Name = "Notes" };
        sec.Pages.Add(new Page { Title = "Untitled page" });
        nb.Sections.Add(sec);
        Notebooks.Add(nb);
        SelectedNotebook = nb;
        IsHomeVisible = false;                 // a fresh notebook opens right away (name it, start writing)
        Save();
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
        FlushDirtyDocs();                      // leaving the editor saves immediately
        IsHomeVisible = true;
    }

    public void SetNotebookColor(Notebook nb, string hex)
    {
        nb.Color = hex;
        Save();
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
        SelectedPage = pg;
        Save();
    }

    [RelayCommand]
    private void DeleteNotebook(Notebook? nb)
    {
        nb ??= SelectedNotebook;
        if (nb is null) return;
        foreach (var page in nb.Sections.SelectMany(s => s.Pages))
            ForgetPageDoc(page, deleteFile: false);           // whole folder goes away below
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
