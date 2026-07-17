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
