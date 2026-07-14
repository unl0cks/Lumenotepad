using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumenotepad.Models;

/// <summary>The whole set of notebooks, in display order. Root of the persisted tree.</summary>
public sealed class Workspace
{
    public ObservableCollection<Notebook> Notebooks { get; set; } = new();
}

/// <summary>A subject / project / case. One on-disk folder per notebook (see <see cref="Folder"/>).</summary>
public sealed partial class Notebook : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>On-disk folder name (a stable slug of the original name); assigned once by the store.</summary>
    public string Folder { get; set; } = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _color = "#4DA6FF";
    /// <summary>Cover image file name inside the notebook folder ("" = color cover).</summary>
    [ObservableProperty] private string _cover = "";
    /// <summary>Absolute path of the cover image, hydrated by the store on load. Never persisted.</summary>
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private string? _coverPath;
    /// <summary>Paper tint hex for this notebook's pages (null = untinted). Per-notebook data,
    /// not a preference — set from the notebook context menu, untouched by Reset settings.</summary>
    [ObservableProperty] private string? _paperTint;
    /// <summary>Per-notebook style defaults for NEW pages (M9). Grid null = inherit the global
    /// paper-grid pref; font null = the app default. Set by the notebook wizard / customization.</summary>
    [ObservableProperty] private string? _defaultGridStyle;
    [ObservableProperty] private string _defaultPageStyle = "Freeform";
    [ObservableProperty] private int _defaultPageStyleMode;
    [ObservableProperty] private string? _defaultFont;
    [ObservableProperty] private double _defaultFontSize = 15;
    public ObservableCollection<Section> Sections { get; set; } = new();
}

/// <summary>A topic group within a notebook (the colored tabs). Optional and collapsible in the UI.</summary>
public sealed partial class Section : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "";
    public ObservableCollection<Page> Pages { get; set; } = new();

    /// <summary>Transient UI flag: this section's tab is in inline-rename mode. Never persisted.</summary>
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isEditing;
}

/// <summary>A single page: title + per-page style overrides; canvas content lives in its page file.</summary>
public sealed partial class Page : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _title = "";
    /// <summary>Per-page style overrides (M9): null = inherit the notebook's default. An explicit
    /// PageStyle carries its own apply mode (0 guides+starters, 1 starters only, 2 rigid).</summary>
    [ObservableProperty] private string? _gridStyle;
    [ObservableProperty] private string? _pageStyle;
    [ObservableProperty] private int _pageStyleMode;
}
