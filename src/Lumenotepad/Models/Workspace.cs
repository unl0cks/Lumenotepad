using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumenotepad.Models;

public sealed class Workspace
{
    public ObservableCollection<Notebook> Notebooks { get; set; } = new();
}

public sealed partial class Notebook : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Folder { get; set; } = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _color = "#4DA6FF";

    [ObservableProperty] private string _cover = "";

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private string? _coverPath;

    [ObservableProperty] private string? _paperTint;

    [ObservableProperty] private string? _defaultGridStyle;
    [ObservableProperty] private string _defaultPageStyle = "Freeform";
    [ObservableProperty] private int _defaultPageStyleMode;
    [ObservableProperty] private string? _defaultFont;
    [ObservableProperty] private double _defaultFontSize = 15;
    public ObservableCollection<Section> Sections { get; set; } = new();
}

public sealed partial class Section : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "";
    public ObservableCollection<Page> Pages { get; set; } = new();

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isEditing;
}

public sealed partial class Page : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private string? _gridStyle;
    [ObservableProperty] private string? _pageStyle;
    [ObservableProperty] private int _pageStyleMode;

    [ObservableProperty] private string? _pdfPath;
}
