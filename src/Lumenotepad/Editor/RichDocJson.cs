using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

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
        [JsonPropertyName("bl")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Bl { get; set; }
        [JsonPropertyName("lnk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Lnk { get; set; }
    }

    internal sealed class ParaDto
    {
        [JsonPropertyName("runs")] public List<RunDto> Runs { get; set; } = new();
        [JsonPropertyName("bul")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Bul { get; set; }
        [JsonPropertyName("chk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Chk { get; set; }
        [JsonPropertyName("tag")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tag { get; set; }
        [JsonPropertyName("nb")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Nb { get; set; }
        [JsonPropertyName("ni")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Ni { get; set; }
        [JsonPropertyName("nu")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Nu { get; set; }
        [JsonPropertyName("ns")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Ns { get; set; }
        [JsonPropertyName("al")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Al { get; set; }
        [JsonPropertyName("ps")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Ps { get; set; }
        [JsonPropertyName("fn")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Fn { get; set; }
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
            Tag = p.Tag,
            Nb = p.NumBold, Ni = p.NumItalic, Nu = p.NumUnderline, Ns = p.NumStrike,
            Al = (int)p.Align, Ps = (int)p.Style, Fn = p.Footnote,
            Runs = p.Runs.Select(r => new RunDto
            {
                T = r.Text, B = r.Bold, I = r.Italic, U = r.Underline, S = r.Strike,
                Hl = r.Highlight, C = r.Color, Fs = r.Size, F = r.Font,
                Bl = (int)r.Baseline, Lnk = r.Link,
            }).ToList(),
        }).ToList();

    internal static RichDocument FromDtos(List<ParaDto>? paras)
    {
        var doc = new RichDocument();
        if (paras is null || paras.Count == 0) return doc;
        doc.Paragraphs.Clear();
        foreach (var p in paras)
        {
            var para = new Paragraph
            {
                Bullet = p.Bul, Checked = p.Chk, Tag = p.Tag,
                NumBold = p.Nb, NumItalic = p.Ni, NumUnderline = p.Nu, NumStrike = p.Ns,
                Align = (TextAlign)p.Al, Style = (ParaStyle)p.Ps, Footnote = p.Fn,
            };
            foreach (var r in p.Runs.Where(r => r.T.Length > 0))
                para.Runs.Add(new RichRun
                {
                    Text = r.T, Bold = r.B, Italic = r.I, Underline = r.U, Strike = r.S,
                    Highlight = r.Hl, Color = r.C, Size = r.Fs, Font = r.F,
                    Baseline = (Baseline)r.Bl, Link = r.Lnk,
                });
            doc.Paragraphs.Add(para);
        }
        if (doc.Paragraphs.Count == 0) doc.Paragraphs.Add(new Paragraph());
        return doc;
    }

    public static string ToJson(RichDocument doc) =>
        JsonSerializer.Serialize(new DocDto { Paras = ToDtos(doc) }, Options);

    public static RichDocument FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RichDocument();
        DocDto? dto;
        try { dto = JsonSerializer.Deserialize<DocDto>(json); }
        catch { return new RichDocument(); }
        return FromDtos(dto?.Paras);
    }
}
