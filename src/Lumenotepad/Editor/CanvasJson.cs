using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumenotepad.Editor;

/// <summary>The v2 page file format: a freeform canvas of note boxes,
/// <c>{"v":2,"boxes":[{"x":0,"y":0,"w":360,"paras":[…]}]}</c>. Reads v1 files (a bare document)
/// as one wide box at the origin, so pre-canvas pages migrate transparently on first load.</summary>
public static class CanvasDocJson
{
    /// <summary>Width given to the single box a legacy v1 page migrates into.</summary>
    public const double MigratedBoxWidth = 680;

    private sealed class BoxDto
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; } = NoteBox.DefaultWidth;
        [JsonPropertyName("h")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double H { get; set; }
        [JsonPropertyName("lk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Locked { get; set; }
        [JsonPropertyName("rg")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Region { get; set; }
        [JsonPropertyName("col")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
        [JsonPropertyName("img")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Img { get; set; }
        [JsonPropertyName("div")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Div { get; set; }
        [JsonPropertyName("att")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Att { get; set; }
        /// <summary>Table cells, row-major: <c>Tbl[row][col]</c> is a cell's paragraphs.</summary>
        [JsonPropertyName("tbl")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<List<List<RichDocJson.ParaDto>>>? Tbl { get; set; }
        [JsonPropertyName("paras")] public List<RichDocJson.ParaDto> Paras { get; set; } = new();
    }

    private sealed class CanvasDto
    {
        [JsonPropertyName("v")] public int V { get; set; } = 2;
        [JsonPropertyName("boxes")] public List<BoxDto> Boxes { get; set; } = new();
        [JsonPropertyName("trash")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<BoxDto>? Trash { get; set; }
        /// <summary>Mindmap links as BOX-INDEX pairs into <see cref="Boxes"/> (M9 Part 5).</summary>
        [JsonPropertyName("links")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int[]>? Links { get; set; }
    }

    private static BoxDto ToDto(NoteBox b) => new()
    {
        X = b.X, Y = b.Y, W = b.Width, H = b.H, Locked = b.Locked, Region = b.Region, Color = b.Color,
        Img = b.ImagePath, Div = b.Divider, Att = b.AttachPath,
        Tbl = b.Table?.Rows.Select(row => row.Select(RichDocJson.ToDtos).ToList()).ToList(),
        Paras = RichDocJson.ToDtos(b.Doc),
    };

    private static NoteBox FromDto(BoxDto b)
    {
        var box = new NoteBox(RichDocJson.FromDtos(b.Paras))
        {
            X = b.X, Y = b.Y, Width = b.W, H = b.H, Locked = b.Locked, Region = b.Region, Color = b.Color,
            ImagePath = b.Img, Divider = b.Div, AttachPath = b.Att,
        };
        if (b.Tbl is { Count: > 0 })
        {
            var table = new NoteTable();
            foreach (var rowDto in b.Tbl)
                table.Rows.Add(rowDto.Select(RichDocJson.FromDtos).ToList());
            box.Table = table;
        }
        return box;
    }

    public static string ToJson(CanvasDocument canvas)
    {
        var dto = new CanvasDto
        {
            Boxes = canvas.Boxes.Select(ToDto).ToList(),
            Trash = canvas.Trash.Count > 0 ? canvas.Trash.Select(ToDto).ToList() : null,
            Links = canvas.Links.Count > 0
                ? canvas.Links
                    .Select(l => new[] { canvas.Boxes.IndexOf(l.A), canvas.Boxes.IndexOf(l.B) })
                    .Where(p => p[0] >= 0 && p[1] >= 0)
                    .ToList()
                : null,
        };
        return JsonSerializer.Serialize(dto, RichDocJson.Options);
    }

    /// <summary>Parse a page file of either version; null/corrupt input yields an empty canvas (never throws).</summary>
    public static CanvasDocument FromJson(string? json)
    {
        var canvas = new CanvasDocument();
        if (string.IsNullOrWhiteSpace(json)) return canvas;
        try
        {
            using var probe = JsonDocument.Parse(json);
            if (probe.RootElement.ValueKind == JsonValueKind.Object &&
                probe.RootElement.TryGetProperty("boxes", out _))
            {
                var dto = JsonSerializer.Deserialize<CanvasDto>(json);
                if (dto is not null)
                {
                    foreach (var b in dto.Boxes)
                        canvas.Adopt(FromDto(b));       // full box incl. table; subscribes every cell doc
                    if (dto.Trash is not null)                       // trash docs hook on restore, not here
                        foreach (var b in dto.Trash)
                            canvas.Trash.Add(FromDto(b));
                    if (dto.Links is not null)                       // invalid/out-of-range pairs ignored
                        foreach (var pair in dto.Links)
                            if (pair is { Length: 2 } &&
                                pair[0] >= 0 && pair[0] < canvas.Boxes.Count &&
                                pair[1] >= 0 && pair[1] < canvas.Boxes.Count &&
                                pair[0] != pair[1])
                                canvas.Links.Add((canvas.Boxes[pair[0]], canvas.Boxes[pair[1]]));
                }
                return canvas;
            }
        }
        catch { return canvas; }

        // v1 page (a single linear document) → one wide box at the origin; empty docs → no boxes.
        var doc = RichDocJson.FromJson(json);
        if (!NoteBox.IsBlank(doc))
            canvas.AddBox(0, 0, MigratedBoxWidth, doc);
        return canvas;
    }
}
