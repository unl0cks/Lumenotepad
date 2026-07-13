using System;
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
                if (nb is not null)
                {
                    nb.Folder = folder;
                    var cover = Path.Combine(_root, folder, nb.Cover);
                    nb.CoverPath = nb.Cover.Length > 0 && File.Exists(cover) ? cover : null;
                    ws.Notebooks.Add(nb);
                }
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

    /// <summary>Copy an image into the notebook folder as its cover (replacing any old one).
    /// Returns the new absolute path, or null if the notebook has no folder yet.</summary>
    public string? SaveCover(Notebook nb, string sourcePath)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return null;
        var dir = Path.Combine(_root, nb.Folder);
        Directory.CreateDirectory(dir);
        DeleteCoverFiles(dir);
        var dest = Path.Combine(dir, "cover" + Path.GetExtension(sourcePath).ToLowerInvariant());
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    public void DeleteCover(Notebook nb)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return;
        DeleteCoverFiles(Path.Combine(_root, nb.Folder));
    }

    private static void DeleteCoverFiles(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "cover.*"))
            try { File.Delete(f); } catch { }
    }

    // ---- page content (one JSON per page in <notebook>/pages/) ----

    private string PageDocPath(Notebook nb, string pageId) => PageDocPath(nb.Folder, pageId);

    private string PageDocPath(string folder, string pageId) =>
        Path.Combine(_root, folder, "pages", pageId + ".page.json");

    public void SavePageDoc(Notebook nb, string pageId, Editor.CanvasDocument doc)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return;   // notebook not persisted yet (Save assigns folders)
        var path = PageDocPath(nb, pageId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Editor.CanvasDocJson.ToJson(doc));
    }

    /// <summary>The page's saved canvas, or null if it has none yet (v1 files migrate on read).</summary>
    public Editor.CanvasDocument? LoadPageDoc(Notebook nb, string pageId) => LoadPageDoc(nb.Folder, pageId);

    /// <summary>Same, keyed by the notebook's on-disk FOLDER (a stable string) rather than the live
    /// model — so a background export can read page content without touching the UI-thread tree.</summary>
    public Editor.CanvasDocument? LoadPageDoc(string folder, string pageId)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        var path = PageDocPath(folder, pageId);
        if (!File.Exists(path)) return null;
        try { return Editor.CanvasDocJson.FromJson(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>When the page's content file was last written (UTC), or null if it has none —
    /// powers the homepage's "Jump back in" strip without storing any extra state.</summary>
    public DateTime? PageDocTime(Notebook nb, string pageId)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return null;
        var path = PageDocPath(nb, pageId);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    public void DeletePageDoc(Notebook nb, string pageId)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return;
        try { File.Delete(PageDocPath(nb, pageId)); } catch { }
    }
}
