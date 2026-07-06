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
    private static readonly string[] NotebookColors =
        { "#4DA6FF", "#3E9C6B", "#E27BA6", "#E0A64D", "#9B7BE2", "#4DC6C0" };

    private readonly WorkspaceStore _store;
    private readonly Workspace _workspace;

    public ObservableCollection<Notebook> Notebooks => _workspace.Notebooks;

    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Section? _selectedSection;
    [ObservableProperty] private Page? _selectedPage;
    [ObservableProperty] private bool _isRailVisible = true;
    [ObservableProperty] private bool _isPagesVisible = true;
    [ObservableProperty] private string _toolbarPosition = "Top";   // "Top" | "Left" | "Right" | "Bottom"
    [ObservableProperty] private string _toolbarScope = "Window";   // "Window" | "Page"

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

    /// <summary>Persist the whole tree (called after every structural change / rename).</summary>
    public void Save() => _store.Save(_workspace);

    // Per-page rich documents: loaded from disk on first access, dirty-tracked on edit, saved by
    // FlushDirtyDocs (the view debounces it while typing; page switch and window close flush too).
    private readonly Dictionary<string, (RichDocument Doc, Notebook Owner)> _docs = new();
    private readonly HashSet<string> _dirty = new();

    /// <summary>Raised whenever any page document changes — the view uses it to debounce an autosave.</summary>
    public event Action? DocsDirtied;

    /// <summary>The rich document for a page (loaded from its notebook folder on first access).</summary>
    public RichDocument DocumentFor(Page page)
    {
        if (_docs.TryGetValue(page.Id, out var entry)) return entry.Doc;
        var owner = FindOwner(page) ?? SelectedNotebook ?? Notebooks.First();
        var doc = _store.LoadPageDoc(owner, page.Id) ?? new RichDocument();
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
        var nb = new Notebook { Name = "New notebook", Color = NotebookColors[Notebooks.Count % NotebookColors.Length] };
        var sec = new Section { Name = "Notes" };
        sec.Pages.Add(new Page { Title = "Untitled page" });
        nb.Sections.Add(sec);
        Notebooks.Add(nb);
        SelectedNotebook = nb;
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
