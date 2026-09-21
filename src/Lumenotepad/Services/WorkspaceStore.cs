using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumenotepad.Models;

namespace Lumenotepad.Services;

public sealed class WorkspaceStore
{
    private readonly string _root;
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
            catch {  }
        }
        return ws;
    }

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

    public string? NotebookDir(Notebook nb) =>
        string.IsNullOrEmpty(nb.Folder) ? null : Path.Combine(_root, nb.Folder);

    public string? SavePageImage(Notebook nb, string sourcePath)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return null;
        try
        {
            var imagesDir = Path.Combine(_root, nb.Folder, "images");
            Directory.CreateDirectory(imagesDir);
            var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
            File.Copy(sourcePath, Path.Combine(imagesDir, name), overwrite: true);
            return "images/" + name;
        }
        catch { return null; }
    }

    public string? SavePageAsset(Notebook nb, string sourcePath)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return null;
        try
        {
            var assetsDir = Path.Combine(_root, nb.Folder, "assets");
            Directory.CreateDirectory(assetsDir);

            // Opening the same file again must land on the copy that's already in the notebook, or
            // everything drawn on the earlier copy (PDF highlights, notes, arrows) looks lost.
            if (FindIdenticalAsset(assetsDir, sourcePath) is { } existing)
                return "assets/" + existing;

            string stem = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            string name = stem + ext;
            for (int i = 2; File.Exists(Path.Combine(assetsDir, name)); i++)
                name = $"{stem} ({i}){ext}";
            File.Copy(sourcePath, Path.Combine(assetsDir, name));
            return "assets/" + name;
        }
        catch { return null; }
    }

    /// <summary>The file name of an asset with exactly the same bytes as <paramref name="sourcePath"/>,
    /// or null. Earlier versions copied a PDF again on every open, so a notebook can already hold
    /// several identical copies; the one carrying the most annotations wins, then the plainest name.</summary>
    private static string? FindIdenticalAsset(string assetsDir, string sourcePath)
    {
        var src = new FileInfo(sourcePath);
        byte[]? srcHash = null;
        string? best = null;
        int bestNotes = -1;
        foreach (var path in Directory.EnumerateFiles(assetsDir))
        {
            var fi = new FileInfo(path);
            if (fi.Length != src.Length
                || !string.Equals(fi.Extension, src.Extension, StringComparison.OrdinalIgnoreCase)) continue;
            srcHash ??= Hash(sourcePath);
            if (!srcHash.AsSpan().SequenceEqual(Hash(path))) continue;
            int notes = AnnotationCount(path);
            if (notes > bestNotes || (notes == bestNotes && fi.Name.Length < best!.Length))
            {
                best = fi.Name;
                bestNotes = notes;
            }
        }
        return best;
    }

    private static byte[] Hash(string path)
    {
        using var s = File.OpenRead(path);
        return System.Security.Cryptography.SHA256.HashData(s);
    }

    private static int AnnotationCount(string assetPath)
    {
        var side = Editor.PdfAnnotationDoc.SidecarPath(assetPath);
        if (!File.Exists(side)) return 0;
        try { return Editor.PdfAnnotationDoc.FromJson(File.ReadAllText(side)).Items.Count; }
        catch { return 0; }
    }

    private static void DeleteCoverFiles(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "cover.*"))
            try { File.Delete(f); } catch { }
    }

    private string PageDocPath(Notebook nb, string pageId) => PageDocPath(nb.Folder, pageId);

    private string PageDocPath(string folder, string pageId) =>
        Path.Combine(_root, folder, "pages", pageId + ".page.json");

    public void SavePageDoc(Notebook nb, string pageId, Editor.CanvasDocument doc)
    {
        if (string.IsNullOrEmpty(nb.Folder)) return;
        var path = PageDocPath(nb, pageId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Editor.CanvasDocJson.ToJson(doc));
    }

    public Editor.CanvasDocument? LoadPageDoc(Notebook nb, string pageId) => LoadPageDoc(nb.Folder, pageId);

    public Editor.CanvasDocument? LoadPageDoc(string folder, string pageId)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        var path = PageDocPath(folder, pageId);
        if (!File.Exists(path)) return null;
        try { return Editor.CanvasDocJson.FromJson(File.ReadAllText(path)); }
        catch { return null; }
    }

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
