using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

/// <summary>JSON mapping for <see cref="RichDocument"/> content. The v1 page format was a bare
/// document (<c>{"v":1,"paras":[…]}</c>); pages now persist as a canvas of boxes via
/// <see cref="CanvasDocJson"/>, which reuses these DTOs per box. Human-readable, defaults omitted
/// so files stay small: <c>{"runs":[{"t":"hello","b":true}]}</c></summary>
public static class RichDocJson
{
    internal sealed class RunDto
    {
        [JsonPropertyName("t")] public string T { get; set; } = "";
        [JsonPropertyName("b")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool B { get; set; }
        [JsonPropertyName("i")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool I { get; set; }
        [JsonPropertyName("u")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool U { get; set; }
        [JsonPropertyName("s")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool S { get; set; }
        [JsonPropertyName("hl")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Hl { get; set; }
        [JsonPropertyName("c")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? C { get; set; }
        [JsonPropertyName("fs")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Fs { get; set; }
        [JsonPropertyName("f")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? F { get; set; }
    }

    internal sealed class ParaDto
    {
        [JsonPropertyName("runs")] public List<RunDto> Runs { get; set; } = new();
        [JsonPropertyName("bul")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Bul { get; set; }
        [JsonPropertyName("chk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Chk { get; set; }
        [JsonPropertyName("nb")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Nb { get; set; }
        [JsonPropertyName("ni")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Ni { get; set; }
        [JsonPropertyName("nu")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Nu { get; set; }
        [JsonPropertyName("ns")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Ns { get; set; }
    }

    private sealed class DocDto
    {
        [JsonPropertyName("v")] public int V { get; set; } = 1;
        [JsonPropertyName("paras")] public List<ParaDto> Paras { get; set; } = new();
    }

    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal static List<ParaDto> ToDtos(RichDocument doc) =>
        doc.Paragraphs.Select(p => new ParaDto
        {
            Bul = p.Bullet,
            Chk = p.Checked,
            Nb = p.NumBold, Ni = p.NumItalic, Nu = p.NumUnderline, Ns = p.NumStrike,
            Runs = p.Runs.Select(r => new RunDto
            {
                T = r.Text, B = r.Bold, I = r.Italic, U = r.Underline, S = r.Strike,
                Hl = r.Highlight, C = r.Color, Fs = r.Size, F = r.Font,
            }).ToList(),
        }).ToList();

    /// <summary>Rebuild a document from para DTOs; always yields at least one paragraph.</summary>
    internal static RichDocument FromDtos(List<ParaDto>? paras)
    {
        var doc = new RichDocument();
        if (paras is null || paras.Count == 0) return doc;
        doc.Paragraphs.Clear();
        foreach (var p in paras)
        {
            var para = new Paragraph
            {
                Bullet = p.Bul, Checked = p.Chk,
                NumBold = p.Nb, NumItalic = p.Ni, NumUnderline = p.Nu, NumStrike = p.Ns,
            };
            foreach (var r in p.Runs.Where(r => r.T.Length > 0))
                para.Runs.Add(new RichRun
                {
                    Text = r.T, Bold = r.B, Italic = r.I, Underline = r.U, Strike = r.S,
                    Highlight = r.Hl, Color = r.C, Size = r.Fs, Font = r.F,
                });
            doc.Paragraphs.Add(para);
        }
        if (doc.Paragraphs.Count == 0) doc.Paragraphs.Add(new Paragraph());
        return doc;
    }

    public static string ToJson(RichDocument doc) =>
        JsonSerializer.Serialize(new DocDto { Paras = ToDtos(doc) }, Options);

    /// <summary>Parse a v1 document; null/corrupt input yields a fresh empty document (never throws).</summary>
    public static RichDocument FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RichDocument();
        DocDto? dto;
        try { dto = JsonSerializer.Deserialize<DocDto>(json); }
        catch { return new RichDocument(); }
        return FromDtos(dto?.Paras);
    }
}
