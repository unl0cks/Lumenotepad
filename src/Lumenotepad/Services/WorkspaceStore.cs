using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumenotepad.Models;

namespace Lumenotepad.Services;

/// <summary>Loads/saves the notebook tree under <c>userdata/notebooks/</c>: one folder per notebook, its
/// structure in <c>notebook.json</c>, and the notebook order in <c>order.json</c>. Human-readable and portable
/// — a notebook is just a folder you can back up, sync, or copy. Page content folders arrive in M3.</summary>
public sealed class WorkspaceStore
{
    private readonly string _root;   // userdata/notebooks
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public WorkspaceStore(string userDataDir) => _root = Path.Combine(userDataDir, "notebooks");

    public Workspace Load()
    {
        var ws = new Workspace();
        var orderFile = Path.Combine(_root, "order.json");
        if (!File.Exists(orderFile)) return ws;

        List<string>? order;
        try { order = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(orderFile)); }
        catch { order = null; }
        if (order is null) return ws;

        foreach (var folder in order)
        {
            var nbFile = Path.Combine(_root, folder, "notebook.json");
            if (!File.Exists(nbFile)) continue;
            try
            {
                var nb = JsonSerializer.Deserialize<Notebook>(File.ReadAllText(nbFile));
                if (nb is not null) { nb.Folder = folder; ws.Notebooks.Add(nb); }
            }
            catch { /* skip a corrupt notebook rather than losing the whole workspace */ }
        }
        return ws;
    }

    /// <summary>Load, or seed a friendly default workspace on first run so the app is never empty.</summary>
    public Workspace LoadOrSeed()
    {
        var ws = Load();
        if (ws.Notebooks.Count == 0)
        {
            var nb = new Notebook { Name = "My Notebook", Color = "#4DA6FF" };
            var sec = new Section { Name = "Notes" };
            sec.Pages.Add(new Page { Title = "Welcome" });
            nb.Sections.Add(sec);
            ws.Notebooks.Add(nb);
            Save(ws);
        }
        return ws;
    }

    public void Save(Workspace ws)
    {
        Directory.CreateDirectory(_root);
        var used = new HashSet<string>(
            ws.Notebooks.Where(n => !string.IsNullOrEmpty(n.Folder)).Select(n => n.Folder));

        foreach (var nb in ws.Notebooks)
        {
            if (string.IsNullOrEmpty(nb.Folder))
            {
                nb.Folder = Slug.Unique(nb.Name, used);
                used.Add(nb.Folder);
            }
            var dir = Path.Combine(_root, nb.Folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "notebook.json"), JsonSerializer.Serialize(nb, Json));
        }
        File.WriteAllText(Path.Combine(_root, "order.json"),
            JsonSerializer.Serialize(ws.Notebooks.Select(n => n.Folder).ToList(), Json));
    }

    public void DeleteNotebook(Notebook nb)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return;
        var dir = Path.Combine(_root, nb.Folder);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
