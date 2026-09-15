using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lumenotepad.Tests;

internal static class TestPdf
{
    public sealed record Line(double X, double Y, string Text, double Size = 12);

    public static byte[] Build(IReadOnlyList<Line> lines, int rotate = 0, double[]? cropBox = null,
                               double width = 612, double height = 792)
    {
        var content = new StringBuilder();
        foreach (var l in lines)
            content.Append(Inv($"BT /F1 {l.Size} Tf {l.X} {l.Y} Td ({Escape(l.Text)}) Tj ET\n"));
        string stream = content.ToString();
        string extra = (rotate != 0 ? Inv($" /Rotate {rotate}") : "")
            + (cropBox is { Length: 4 } c ? Inv($" /CropBox [{c[0]} {c[1]} {c[2]} {c[3]}]") : "");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            Inv($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}]{extra} /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
        };
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public static byte[] Blank() => Build(Array.Empty<Line>());

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
}
