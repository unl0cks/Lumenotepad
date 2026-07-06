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
    public bool Underline;
    public bool Strike;
    public string? Highlight;   // hex ("#FFD666") or null = none
    public string? Color;       // hex or null = theme default
    public double? Size;        // null = editor default
    public string? Font;        // family name or null = editor default

    public RichRun Clone() => new()
    {
        Text = Text, Bold = Bold, Italic = Italic, Underline = Underline, Strike = Strike,
        Highlight = Highlight, Color = Color, Size = Size, Font = Font,
    };

    public bool SameFormat(RichRun o) =>
        Bold == o.Bold && Italic == o.Italic && Underline == o.Underline && Strike == o.Strike &&
        Highlight == o.Highlight && Color == o.Color && Size == o.Size && Font == o.Font;

    public RunFormat Format => new(Bold, Italic, Underline, Strike, Highlight, Color, Size, Font);
    public void SetFormat(RunFormat f)
    {
        Bold = f.Bold; Italic = f.Italic; Underline = f.Underline; Strike = f.Strike;
        Highlight = f.Highlight; Color = f.Color; Size = f.Size; Font = f.Font;
    }
}

/// <summary>A complete character format — what the caret "carries" and what runs store.</summary>
public readonly record struct RunFormat(
    bool Bold, bool Italic, bool Underline, bool Strike,
    string? Highlight, string? Color, double? Size, string? Font);

/// <summary>A paragraph = an ordered list of runs (zero runs = an empty paragraph).</summary>
public sealed class Paragraph
{
    public List<RichRun> Runs = new();

    /// <summary>Bumped by RichDocument on every mutation of this paragraph, so views can cache a
    /// per-paragraph layout and rebuild only what changed (keystroke cost stays O(1), not O(document)).</summary>
    public int Version;

    /// <summary>Bullet style key: null = none; "dot" | "arrow" | "star" | "heart" | "flower" | "spark"
    /// (cute glyph bullets), "num" (numbered), "check" (checkbox).</summary>
    public string? Bullet;

    /// <summary>Ticked state for "check" bullets.</summary>
    public bool Checked;

    public int Length => Runs.Sum(r => r.Text.Length);
    public string Text => string.Concat(Runs.Select(r => r.Text));

    public Paragraph Clone() => new()
    {
        Runs = Runs.Select(r => r.Clone()).ToList(),
        Bullet = Bullet,
        Checked = Checked,
    };

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

    /// <summary>Insert text (may contain '\n' → paragraph splits) with bold/italic only (test/back-compat).</summary>
    public DocPos InsertText(DocPos pos, string text, bool bold = false, bool italic = false)
        => InsertText(pos, text, new RunFormat(bold, italic, false, false, null, null, null, null));

    /// <summary>Insert text (may contain '\n' → paragraph splits) with the given formatting.
    /// Returns the position after the inserted text.</summary>
    public DocPos InsertText(DocPos pos, string text, RunFormat fmt)
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
                var run = new RichRun { Text = lines[i] };
                run.SetFormat(fmt);
                para.Runs.Insert(runIdx, run);
                para.Normalize();
                para.Version++;
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
        // The new paragraph continues the list (standard list behavior); the tick state does not carry over.
        var next = new Paragraph { Runs = para.Runs.Skip(runIdx).ToList(), Bullet = para.Bullet };
        para.Runs.RemoveRange(runIdx, para.Runs.Count - runIdx);
        para.Version++;
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
            para.Version++;
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
            first.Version++;
        }
        OnChanged();
    }

    /// <summary>Formatting of the character just before the position (what typing "continues"), or the
    /// first run's format at paragraph start.</summary>
    public RunFormat FormatAt(DocPos pos)
    {
        Clamp(ref pos);
        var para = Paragraphs[pos.Para];
        if (para.Runs.Count == 0) return default;
        int acc = 0;
        foreach (var r in para.Runs)
        {
            int end = acc + r.Text.Length;
            if (pos.Off <= end && (pos.Off > acc || acc == 0)) return r.Format;
            acc = end;
        }
        return para.Runs[^1].Format;
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
            para.Version++;
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

    /// <summary>Set (or clear, with null) the bullet style on every paragraph the range touches.
    /// The tick state resets when the style changes away from "check".</summary>
    public void SetBullet(DocPos a, DocPos b, string? style)
    {
        Clamp(ref a); Clamp(ref b);
        if (a > b) (a, b) = (b, a);
        for (int pi = a.Para; pi <= b.Para; pi++)
        {
            var para = Paragraphs[pi];
            para.Bullet = style;
            if (style != "check") para.Checked = false;
            para.Version++;
        }
        OnChanged();
    }

    /// <summary>Every paragraph in the range already has exactly this bullet style.</summary>
    public bool BulletAll(DocPos a, DocPos b, string? style)
    {
        Clamp(ref a); Clamp(ref b);
        if (a > b) (a, b) = (b, a);
        for (int pi = a.Para; pi <= b.Para; pi++)
            if (Paragraphs[pi].Bullet != style) return false;
        return true;
    }

    /// <summary>Flip a checkbox paragraph's ticked state.</summary>
    public void ToggleChecked(int paraIndex)
    {
        if (paraIndex < 0 || paraIndex >= Paragraphs.Count) return;
        var para = Paragraphs[paraIndex];
        para.Checked = !para.Checked;
        para.Version++;
        OnChanged();
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
