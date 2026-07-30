using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

public sealed class PdfAnnotation
{
    public const string Highlight = "highlight";
    public const string Note = "note";
    public const string TextBox = "text";
    public const string Arrow = "arrow";

    [JsonPropertyName("pg")] public int Page { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Highlight;
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
    [JsonPropertyName("x2")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double X2 { get; set; }
    [JsonPropertyName("y2")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Y2 { get; set; }

    [JsonPropertyName("cv")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Curved { get; set; }
    [JsonPropertyName("c1x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C1x { get; set; }
    [JsonPropertyName("c1y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C1y { get; set; }
    [JsonPropertyName("c2x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C2x { get; set; }
    [JsonPropertyName("c2y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C2y { get; set; }
    [JsonPropertyName("c3x")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C3x { get; set; }
    [JsonPropertyName("c3y")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double C3y { get; set; }

    [JsonPropertyName("hs")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double HeadScale { get; set; }

    [JsonPropertyName("hst")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HeadStyle { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; } = "#66FFD54A";
    [JsonPropertyName("text")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }

    [JsonPropertyName("rich")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Rich { get; set; }

    [JsonPropertyName("b")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Bold { get; set; }
    [JsonPropertyName("it")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Italic { get; set; }
    [JsonPropertyName("u")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Underline { get; set; }
    [JsonPropertyName("st")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Strike { get; set; }
}

public sealed class PdfAnnotationDoc
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("items")] public List<PdfAnnotation> Items { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static PdfAnnotationDoc FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PdfAnnotationDoc();
        try { return JsonSerializer.Deserialize<PdfAnnotationDoc>(json) ?? new PdfAnnotationDoc(); }
        catch { return new PdfAnnotationDoc(); }
    }

    public static string SidecarPath(string pdfPath) => pdfPath + ".lumenotes.json";
}

public static class PdfAnnotationHub
{
    private static readonly Dictionary<string, PdfAnnotationDoc> Docs = new(StringComparer.OrdinalIgnoreCase);

    public static event Action<string, object?>? Changed;

    public static PdfAnnotationDoc Get(string pdfPath, string? sidecarJson)
    {
        if (Docs.TryGetValue(pdfPath, out var d)) return d;
        return Docs[pdfPath] = PdfAnnotationDoc.FromJson(sidecarJson);
    }

    public static void NotifyChanged(string pdfPath, object? sender) => Changed?.Invoke(pdfPath, sender);

    public static void Reset() => Docs.Clear();
}
