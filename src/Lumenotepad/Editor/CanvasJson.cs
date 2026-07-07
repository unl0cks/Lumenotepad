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
        [JsonPropertyName("paras")] public List<RichDocJson.ParaDto> Paras { get; set; } = new();
    }

    private sealed class CanvasDto
    {
        [JsonPropertyName("v")] public int V { get; set; } = 2;
        [JsonPropertyName("boxes")] public List<BoxDto> Boxes { get; set; } = new();
    }

    public static string ToJson(CanvasDocument canvas)
    {
        var dto = new CanvasDto
        {
            Boxes = canvas.Boxes.Select(b => new BoxDto
            {
                X = b.X, Y = b.Y, W = b.Width,
                Paras = RichDocJson.ToDtos(b.Doc),
            }).ToList(),
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
                    foreach (var b in dto.Boxes)
                        canvas.AddBox(b.X, b.Y, b.W, RichDocJson.FromDtos(b.Paras));
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
