using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;

namespace Lumenotepad.Editor;

public readonly record struct PdfChar(int CodePoint, double X0, double Y0, double X1, double Y1, bool Generated)
{
    public bool HasBox => X1 > X0 && Y1 > Y0;
    public double CenterY => (Y0 + Y1) / 2;
    public double Height => Y1 - Y0;
}

public sealed record PdfPageText(int Page, IReadOnlyList<PdfChar> Chars)
{
    public bool IsEmpty => !Chars.Any(c => c.HasBox);
}

public static class PdfTextSelection
{
    private const double Slop = 0.15;

    public static int CharAt(IReadOnlyList<PdfChar> chars, Point p)
    {
        for (int i = 0; i < chars.Count; i++)
        {
            var c = chars[i];
            if (!c.HasBox) continue;
            double pad = c.Height * Slop;
            if (p.X >= c.X0 - pad && p.X <= c.X1 + pad && p.Y >= c.Y0 - pad && p.Y <= c.Y1 + pad) return i;
        }
        return -1;
    }

    public static int CaretAt(IReadOnlyList<PdfChar> chars, Point p)
    {
        int best = -1;
        double bestDx = double.MaxValue;
        for (int i = 0; i < chars.Count; i++)
        {
            var c = chars[i];
            if (!c.HasBox || p.Y < c.Y0 || p.Y > c.Y1) continue;
            double dx = p.X < c.X0 ? c.X0 - p.X : p.X > c.X1 ? p.X - c.X1 : 0;
            if (dx < bestDx) { bestDx = dx; best = i; }
        }
        if (best < 0)
        {
            double bestD = double.MaxValue;
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (!c.HasBox) continue;
                double dx = p.X < c.X0 ? c.X0 - p.X : p.X > c.X1 ? p.X - c.X1 : 0;
                double dy = p.Y < c.Y0 ? c.Y0 - p.Y : p.Y > c.Y1 ? p.Y - c.Y1 : 0;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return -1;
            double dyBest = p.Y < chars[best].Y0 ? chars[best].Y0 - p.Y : p.Y - chars[best].Y1;
            if (dyBest > chars[best].Height) return -1;
        }
        var hit = chars[best];
        return p.X > (hit.X0 + hit.X1) / 2 ? best + 1 : best;
    }

    public static (int Start, int End) WordAt(IReadOnlyList<PdfChar> chars, int index)
    {
        if (index < 0 || index >= chars.Count) return (0, 0);
        if (!IsWordChar(chars, index)) return (index, index + 1);
        int a = index, b = index + 1;
        while (a > 0 && IsWordChar(chars, a - 1)) a--;
        while (b < chars.Count && IsWordChar(chars, b)) b++;
        return (a, b);
    }

    public static (int Start, int End) LineAt(IReadOnlyList<PdfChar> chars, int index)
    {
        if (index < 0 || index >= chars.Count) return (0, 0);
        int refIndex = index;
        while (refIndex < chars.Count && !chars[refIndex].HasBox) refIndex++;
        if (refIndex >= chars.Count) return (index, index + 1);
        var r = chars[refIndex];
        int a = refIndex;
        for (int i = refIndex - 1; i >= 0; i--)
        {
            var c = chars[i];
            if (IsBreak(c)) break;
            if (!c.HasBox) continue;
            if (!SameLine(r, c)) break;
            a = i;
        }
        int b = refIndex + 1;
        for (int i = refIndex + 1; i < chars.Count; i++)
        {
            var c = chars[i];
            if (IsBreak(c)) break;
            if (!c.HasBox) continue;
            if (!SameLine(r, c)) break;
            b = i + 1;
        }
        return (a, b);
    }

    public static IReadOnlyList<Rect> Rects(IReadOnlyList<PdfChar> chars, int start, int end)
    {
        var rects = new List<Rect>();
        start = Math.Max(0, start);
        end = Math.Min(chars.Count, end);
        Rect? cur = null;
        for (int i = start; i < end; i++)
        {
            var c = chars[i];
            if (!c.HasBox) continue;
            var r = new Rect(c.X0, c.Y0, c.X1 - c.X0, c.Y1 - c.Y0);
            if (cur is { } k && c.CenterY >= k.Top && c.CenterY <= k.Bottom && c.X0 >= k.Left - k.Height * 0.5)
                cur = k.Union(r);
            else
            {
                if (cur is { } done) rects.Add(done);
                cur = r;
            }
        }
        if (cur is { } last) rects.Add(last);
        return rects;
    }

    public static string TextOf(IReadOnlyList<PdfChar> chars, int start, int end)
    {
        var sb = new StringBuilder();
        start = Math.Max(0, start);
        end = Math.Min(chars.Count, end);
        for (int i = start; i < end; i++)
        {
            int cp = chars[i].CodePoint;
            if (cp == '\r') continue;
            if (cp == '\n') { sb.Append('\n'); continue; }
            if (cp < 0x20 && cp != '\t') continue;
            if (!Rune.IsValid(cp) || cp == 0xFFFE || cp == 0xFFFF) continue;
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString().TrimEnd();
    }

    public static Rect? CaretBox(IReadOnlyList<PdfChar> chars, int caret)
    {
        if (caret >= 0 && caret < chars.Count && chars[caret].HasBox)
        {
            var c = chars[caret];
            return new Rect(c.X0, c.Y0, 0, c.Height);
        }
        for (int i = Math.Min(caret, chars.Count) - 1; i >= 0; i--)
            if (chars[i].HasBox)
            {
                var c = chars[i];
                return new Rect(c.X1, c.Y0, 0, c.Height);
            }
        for (int i = Math.Max(0, caret); i < chars.Count; i++)
            if (chars[i].HasBox)
            {
                var c = chars[i];
                return new Rect(c.X0, c.Y0, 0, c.Height);
            }
        return null;
    }

    private static bool IsBreak(PdfChar c) => c.CodePoint is '\r' or '\n';

    private static bool SameLine(PdfChar a, PdfChar b) => b.CenterY >= a.Y0 && b.CenterY <= a.Y1;

    private static bool IsLetterOrDigit(int cp) => Rune.IsValid(cp) && Rune.IsLetterOrDigit(new Rune(cp));

    private static bool IsWordChar(IReadOnlyList<PdfChar> chars, int i)
    {
        int cp = chars[i].CodePoint;
        if (IsLetterOrDigit(cp)) return true;
        if (cp is '\'' or '’')
            return i > 0 && i + 1 < chars.Count && IsLetterOrDigit(chars[i - 1].CodePoint) && IsLetterOrDigit(chars[i + 1].CodePoint);
        return false;
    }
}
