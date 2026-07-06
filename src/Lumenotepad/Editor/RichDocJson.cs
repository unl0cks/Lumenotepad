using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

/// <summary>JSON persistence for <see cref="RichDocument"/> — the on-disk page content format.
/// Human-readable, versioned, defaults omitted so files stay small:
/// <c>{"v":1,"paras":[{"runs":[{"t":"hello","b":true}]}]}</c></summary>
public static class RichDocJson
{
    private sealed class RunDto
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

    private sealed class ParaDto
    {
        [JsonPropertyName("runs")] public List<RunDto> Runs { get; set; } = new();
        [JsonPropertyName("bul")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Bul { get; set; }
        [JsonPropertyName("chk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Chk { get; set; }
    }

    private sealed class DocDto
    {
        [JsonPropertyName("v")] public int V { get; set; } = 1;
        [JsonPropertyName("paras")] public List<ParaDto> Paras { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(RichDocument doc)
    {
        var dto = new DocDto
        {
            Paras = doc.Paragraphs.Select(p => new ParaDto
            {
                Bul = p.Bullet,
                Chk = p.Checked,
                Runs = p.Runs.Select(r => new RunDto
                {
                    T = r.Text, B = r.Bold, I = r.Italic, U = r.Underline, S = r.Strike,
                    Hl = r.Highlight, C = r.Color, Fs = r.Size, F = r.Font,
                }).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Parse a document; null/corrupt input yields a fresh empty document (never throws).</summary>
    public static RichDocument FromJson(string? json)
    {
        var doc = new RichDocument();
        if (string.IsNullOrWhiteSpace(json)) return doc;
        DocDto? dto;
        try { dto = JsonSerializer.Deserialize<DocDto>(json); }
        catch { return doc; }
        if (dto is null || dto.Paras.Count == 0) return doc;

        doc.Paragraphs.Clear();
        foreach (var p in dto.Paras)
        {
            var para = new Paragraph { Bullet = p.Bul, Checked = p.Chk };
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
}
