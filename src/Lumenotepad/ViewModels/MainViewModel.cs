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

    // Designer / default: use the portable userdata folder beside the exe.
    public MainViewModel() : this(new WorkspaceStore(AppSettings.DefaultDir)) { }

    public MainViewModel(WorkspaceStore store)
    {
        _store = store;
        _workspace = store.LoadOrSeed();
        SelectedNotebook = Notebooks.FirstOrDefault();
    }

    /// <summary>Persist the whole tree (called after every structural change / rename).</summary>
    public void Save() => _store.Save(_workspace);

    // M3 slice: per-page rich documents, session-only (the persisted page format lands with M3.2).
    private readonly Dictionary<string, RichDocument> _docs = new();

    /// <summary>The rich document for a page (created on first access; kept for the session).</summary>
    public RichDocument DocumentFor(Page page)
    {
        if (!_docs.TryGetValue(page.Id, out var doc))
            _docs[page.Id] = doc = new RichDocument();
        return doc;
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
        int idx = sec.Pages.IndexOf(pg);
        sec.Pages.Remove(pg);
        SelectedPage = sec.Pages.ElementAtOrDefault(Math.Max(0, idx - 1));
        Save();
    }

    [RelayCommand] private void ToggleRail() => IsRailVisible = !IsRailVisible;
    [RelayCommand] private void TogglePages() => IsPagesVisible = !IsPagesVisible;
}
