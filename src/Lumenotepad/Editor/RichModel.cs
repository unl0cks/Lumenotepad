using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lumenotepad.Editor;

/// <summary>One contiguous stretch of identically-formatted text inside a paragraph.</summary>
public sealed class RichRun
{
    public string Text = "";
    public bool Bold;
    public bool Italic;

    public RichRun Clone() => new() { Text = Text, Bold = Bold, Italic = Italic };
    public bool SameFormat(RichRun o) => Bold == o.Bold && Italic == o.Italic;
}

/// <summary>A paragraph = an ordered list of runs (zero runs = an empty paragraph).</summary>
public sealed class Paragraph
{
    public List<RichRun> Runs = new();

    public int Length => Runs.Sum(r => r.Text.Length);
    public string Text => string.Concat(Runs.Select(r => r.Text));

    public Paragraph Clone() => new() { Runs = Runs.Select(r => r.Clone()).ToList() };

    /// <summary>Ensure a run boundary exists exactly at <paramref name="offset"/>; returns the index of the
    /// run that STARTS at that offset (== Runs.Count when offset == Length).</summary>
    public int SplitAt(int offset)
    {
        int acc = 0;
        for (int i = 0; i < Runs.Count; i++)
        {
            if (acc == offset) return i;
            int end = acc + Runs[i].Text.Length;
            if (offset < end)
            {
                var r = Runs[i];
                var right = r.Clone();
                right.Text = r.Text[(offset - acc)..];
                r.Text = r.Text[..(offset - acc)];
                Runs.Insert(i + 1, right);
                return i + 1;
            }
            acc = end;
        }
        return Runs.Count;
    }

    /// <summary>Merge adjacent same-format runs and drop empties.</summary>
    public void Normalize()
    {
        Runs.RemoveAll(r => r.Text.Length == 0);
        for (int i = Runs.Count - 2; i >= 0; i--)
        {
            if (Runs[i].SameFormat(Runs[i + 1]))
            {
                Runs[i].Text += Runs[i + 1].Text;
                Runs.RemoveAt(i + 1);
            }
        }
    }
}

/// <summary>A document position: paragraph index + character offset within it.</summary>
public readonly record struct DocPos(int Para, int Off) : IComparable<DocPos>
{
    public int CompareTo(DocPos o) => Para != o.Para ? Para.CompareTo(o.Para) : Off.CompareTo(o.Off);
    public static bool operator <(DocPos a, DocPos b) => a.CompareTo(b) < 0;
    public static bool operator >(DocPos a, DocPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocPos a, DocPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocPos a, DocPos b) => a.CompareTo(b) >= 0;
}

/// <summary>Immutable deep copy of a document, used for snapshot-based undo.</summary>
public sealed class DocSnapshot
{
    internal List<Paragraph> Paragraphs = new();
}

/// <summary>The rich document: paragraphs of formatted runs, plus the edit operations the editor needs.
/// Pure model — no Avalonia dependencies, fully unit-testable.</summary>
public sealed class RichDocument
{
    public List<Paragraph> Paragraphs { get; } = new() { new Paragraph() };

    /// <summary>Raised after any mutation, so views can re-layout.</summary>
    public event Action? Changed;
    private void OnChanged() => Changed?.Invoke();

    public DocPos End => new(Paragraphs.Count - 1, Paragraphs[^1].Length);

    public string GetText() => string.Join("\n", Paragraphs.Select(p => p.Text));

    public void Clamp(ref DocPos p)
    {
        int para = Math.Clamp(p.Para, 0, Paragraphs.Count - 1);
        p = new DocPos(para, Math.Clamp(p.Off, 0, Paragraphs[para].Length));
    }

    /// <summary>Move a position one character left/right, crossing paragraph boundaries.</summary>
    public DocPos Move(DocPos p, int dir)
    {
        Clamp(ref p);
        if (dir > 0)
        {
            if (p.Off < Paragraphs[p.Para].Length) return p with { Off = p.Off + 1 };
            return p.Para + 1 < Paragraphs.Count ? new DocPos(p.Para + 1, 0) : p;
        }
        if (p.Off > 0) return p with { Off = p.Off - 1 };
        return p.Para > 0 ? new DocPos(p.Para - 1, Paragraphs[p.Para - 1].Length) : p;
    }

    /// <summary>Insert text (may contain '\n' → paragraph splits) with the given formatting.
    /// Returns the position after the inserted text.</summary>
    public DocPos InsertText(DocPos pos, string text, bool bold = false, bool italic = false)
    {
        Clamp(ref pos);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cur = pos;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) cur = SplitParagraphCore(cur);
            if (lines[i].Length > 0)
            {
                var para = Paragraphs[cur.Para];
                int runIdx = para.SplitAt(cur.Off);
                para.Runs.Insert(runIdx, new RichRun { Text = lines[i], Bold = bold, Italic = italic });
                para.Normalize();
                cur = cur with { Off = cur.Off + lines[i].Length };
            }
        }
        OnChanged();
        return cur;
    }

    /// <summary>Split the paragraph at the position (the Enter key). Returns the start of the new paragraph.</summary>
    public DocPos SplitParagraph(DocPos pos)
    {
        var r = SplitParagraphCore(pos);
        OnChanged();
        return r;
    }

    private DocPos SplitParagraphCore(DocPos pos)
    {
        Clamp(ref pos);
        var para = Paragraphs[pos.Para];
        int runIdx = para.SplitAt(pos.Off);
        var next = new Paragraph { Runs = para.Runs.Skip(runIdx).ToList() };
        para.Runs.RemoveRange(runIdx, para.Runs.Count - runIdx);
        Paragraphs.Insert(pos.Para + 1, next);
        return new DocPos(pos.Para + 1, 0);
    }

    /// <summary>Delete everything between two positions (order-agnostic; merges paragraphs).</summary>
    public void DeleteRange(DocPos a, DocPos b)
    {
        Clamp(ref a); Clamp(ref b);
        if (a > b) (a, b) = (b, a);
        if (a == b) return;

        if (a.Para == b.Para)
        {
            var para = Paragraphs[a.Para];
            int i1 = para.SplitAt(a.Off);
            int i2 = para.SplitAt(b.Off);
            para.Runs.RemoveRange(i1, i2 - i1);
            para.Normalize();
        }
        else
        {
            var first = Paragraphs[a.Para];
            var last = Paragraphs[b.Para];
            int i1 = first.SplitAt(a.Off);
            first.Runs.RemoveRange(i1, first.Runs.Count - i1);
            int i2 = last.SplitAt(b.Off);
            last.Runs.RemoveRange(0, i2);
            first.Runs.AddRange(last.Runs);          // merge the tail of the last paragraph up
            Paragraphs.RemoveRange(a.Para + 1, b.Para - a.Para);
            first.Normalize();
        }
        OnChanged();
    }

    /// <summary>Formatting of the character just before the position (what typing "continues"), or the
    /// first run's format at paragraph start.</summary>
    public (bool Bold, bool Italic) FormatAt(DocPos pos)
    {
        Clamp(ref pos);
        var para = Paragraphs[pos.Para];
        if (para.Runs.Count == 0) return (false, false);
        int acc = 0;
        foreach (var r in para.Runs)
        {
            int end = acc + r.Text.Length;
            if (pos.Off <= end && (pos.Off > acc || acc == 0)) return (r.Bold, r.Italic);
            acc = end;
        }
        var lastRun = para.Runs[^1];
        return (lastRun.Bold, lastRun.Italic);
    }

    public bool RangeAll(DocPos a, DocPos b, Func<RichRun, bool> pred)
    {
        Clamp(ref a); Clamp(ref b);
        if (a > b) (a, b) = (b, a);
        bool any = false;
        foreach (var (run, _, _) in RunsIn(a, b)) { any = true; if (!pred(run)) return false; }
        return any;
    }

    /// <summary>Apply a format mutation to every run in the range (splitting runs at the edges).</summary>
    public void ApplyFormat(DocPos a, DocPos b, Action<RichRun> mutate)
    {
        Clamp(ref a); Clamp(ref b);
        if (a > b) (a, b) = (b, a);
        if (a == b) return;
        for (int pi = a.Para; pi <= b.Para; pi++)
        {
            var para = Paragraphs[pi];
            int start = pi == a.Para ? a.Off : 0;
            int end = pi == b.Para ? b.Off : para.Length;
            int i1 = para.SplitAt(start);
            int i2 = para.SplitAt(end);
            for (int i = i1; i < i2; i++) mutate(para.Runs[i]);
            para.Normalize();
        }
        OnChanged();
    }

    private IEnumerable<(RichRun Run, int Para, int RunIndex)> RunsIn(DocPos a, DocPos b)
    {
        for (int pi = a.Para; pi <= b.Para; pi++)
        {
            var para = Paragraphs[pi];
            int acc = 0;
            for (int ri = 0; ri < para.Runs.Count; ri++)
            {
                var run = para.Runs[ri];
                int rs = acc, re = acc + run.Text.Length;
                acc = re;
                if (pi == a.Para && re <= a.Off) continue;
                if (pi == b.Para && rs >= b.Off) break;
                if (run.Text.Length > 0) yield return (run, pi, ri);
            }
        }
    }

    // ---- snapshots (undo/redo) ----
    public DocSnapshot TakeSnapshot() => new() { Paragraphs = Paragraphs.Select(p => p.Clone()).ToList() };

    public void Restore(DocSnapshot s)
    {
        Paragraphs.Clear();
        Paragraphs.AddRange(s.Paragraphs.Select(p => p.Clone()));
        if (Paragraphs.Count == 0) Paragraphs.Add(new Paragraph());
        OnChanged();
    }
}
