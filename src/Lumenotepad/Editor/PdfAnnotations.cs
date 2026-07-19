using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

/// <summary>One mark laid over a PDF page (M11 view + annotate): a highlight rectangle or a typed
/// note/callout. Geometry is stored NORMALIZED (0..1 of the page's width/height) so it survives any
/// render DPI or zoom. Kept in a sidecar file next to the PDF — the source PDF is never modified.</summary>
public sealed class PdfAnnotation
{
    public const string Highlight = "highlight";
    public const string Note = "note";
    public const string TextBox = "text";      // free text written over the page
    public const string Arrow = "arrow";        // a line with an arrowhead; X,Y = start, X2,Y2 = end

    [JsonPropertyName("pg")] public int Page { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Highlight;
    [JsonPropertyName("x")] public double X { get; set; }        // normalized left / arrow start x
    [JsonPropertyName("y")] public double Y { get; set; }        // normalized top / arrow start y
    [JsonPropertyName("w")] public double W { get; set; }        // normalized width
    [JsonPropertyName("h")] public double H { get; set; }        // normalized height
    [JsonPropertyName("x2")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double X2 { get; set; }  // arrow end x
    [JsonPropertyName("y2")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Y2 { get; set; }  // arrow end y
    // Curved arrows (M11): three control points at ~25/50/75% that bend the shaft into a smooth
    // spline. Curved=false ⇒ a straight line (the control points are ignored / unset).
    [JsonPropertyName("cv")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Curved { get; set; }
    [JsonPropertyName("c1x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C1x { get; set; }
    [JsonPropertyName("c1y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C1y { get; set; }
    [JsonPropertyName("c2x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C2x { get; set; }
    [JsonPropertyName("c2y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C2y { get; set; }
    [JsonPropertyName("c3x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C3x { get; set; }
    [JsonPropertyName("c3y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C3y { get; set; }
    /// <summary>Arrowhead size multiplier (0 ⇒ the default 1.0). Bigger = a chunkier head.</summary>
    [JsonPropertyName("hs")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double HeadScale { get; set; }
    /// <summary>Arrowhead style: null/"triangle" (filled), "open" (chevron), "diamond", "circle", "none".</summary>
    [JsonPropertyName("hst")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HeadStyle { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; } = "#66FFD54A";
    [JsonPropertyName("text")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }
    /// <summary>Full rich content for note / text annotations (M11): a serialized
    /// <see cref="RichDocument"/> (see <see cref="RichDocJson"/>) so notes carry the SAME formatting as
    /// the note canvas — fonts, sizes, colors, super/subscript, alignment, bullets, links. When present
    /// this is the source of truth; <see cref="Text"/> is kept as a plain-text mirror for exports and
    /// back-compat. Older sidecars that only have <see cref="Text"/> + the whole-box flags below still
    /// load (they're migrated to a rich document on first edit).</summary>
    [JsonPropertyName("rich")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Rich { get; set; }
    // Legacy whole-box formatting for note / text annotations (superseded by Rich; kept so old
    // sidecars keep rendering and migrate cleanly).
    [JsonPropertyName("b")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Bold { get; set; }
    [JsonPropertyName("it")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Italic { get; set; }
    [JsonPropertyName("u")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Underline { get; set; }
    [JsonPropertyName("st")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Strike { get; set; }
}

/// <summary>The full set of a PDF's annotations, persisted as its sidecar (<c>&lt;name&gt;.lumenotes.json</c>).</summary>
public sealed class PdfAnnotationDoc
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("items")] public List<PdfAnnotation> Items { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parse a sidecar; null/blank/corrupt input yields an empty doc (never throws).</summary>
    public static PdfAnnotationDoc FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PdfAnnotationDoc();
        try { return JsonSerializer.Deserialize<PdfAnnotationDoc>(json) ?? new PdfAnnotationDoc(); }
        catch { return new PdfAnnotationDoc(); }
    }

    /// <summary>The sidecar path for a PDF (same folder, name + ".lumenotes.json").</summary>
    public static string SidecarPath(string pdfPath) => pdfPath + ".lumenotes.json";
}

/// <summary>One SHARED in-memory annotation doc per PDF path. The same file can be open in two
/// viewers at once (the embedded PDF page and the attachment popup window) — if each held its own
/// copy, whichever saved last would silently overwrite the other's marks. Sharing the instance makes
/// that impossible, and <see cref="Changed"/> (raised by a viewer after it saves) lets the other
/// viewers repaint live. In-process only — sidecars are still read once and written on save.</summary>
public static class PdfAnnotationHub
{
    private static readonly Dictionary<string, PdfAnnotationDoc> Docs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after a viewer saves changes to a path's doc; sender = that viewer.</summary>
    public static event Action<string, object?>? Changed;

    /// <summary>The shared doc for a PDF. <paramref name="sidecarJson"/> seeds it on FIRST open only —
    /// once cached, the in-memory doc is the newest state and disk is ignored.</summary>
    public static PdfAnnotationDoc Get(string pdfPath, string? sidecarJson)
    {
        if (Docs.TryGetValue(pdfPath, out var d)) return d;
        return Docs[pdfPath] = PdfAnnotationDoc.FromJson(sidecarJson);
    }

    public static void NotifyChanged(string pdfPath, object? sender) => Changed?.Invoke(pdfPath, sender);

    /// <summary>Tests only — forget every cached doc.</summary>
    public static void Reset() => Docs.Clear();
}
