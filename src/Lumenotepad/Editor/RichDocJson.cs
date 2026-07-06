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
    }

    private sealed class ParaDto
    {
        [JsonPropertyName("runs")] public List<RunDto> Runs { get; set; } = new();
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
                Runs = p.Runs.Select(r => new RunDto { T = r.Text, B = r.Bold, I = r.Italic }).ToList(),
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
            var para = new Paragraph();
            foreach (var r in p.Runs.Where(r => r.T.Length > 0))
                para.Runs.Add(new RichRun { Text = r.T, Bold = r.B, Italic = r.I });
            doc.Paragraphs.Add(para);
        }
        if (doc.Paragraphs.Count == 0) doc.Paragraphs.Add(new Paragraph());
        return doc;
    }
}
