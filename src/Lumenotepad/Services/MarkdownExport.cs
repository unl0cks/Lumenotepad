using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumenotepad.Editor;

namespace Lumenotepad.Services;

public static class MarkdownExport
{
    public static string PageToMarkdown(string title, CanvasDocument doc)
    {
        var blocks = new List<string>
        {
            "# " + (string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim()),
        };
        foreach (var box in doc.Boxes.Where(b => !b.IsEmpty).OrderBy(b => b.Y).ThenBy(b => b.X))
            blocks.Add(BoxToMarkdown(box));
        return string.Join("\n\n", blocks) + "\n";
    }

    private static string BoxToMarkdown(NoteBox box)
    {
        var lines = new List<string>();
        int num = 0;
        foreach (var p in box.Doc.Paragraphs)
        {
            if (p.Bullet == "num") num++; else num = 0;
            lines.Add(Prefix(p, num) + Inline(p));
        }
        return string.Join("\n", lines);
    }

    private static string Prefix(Paragraph p, int num) => p.Bullet switch
    {
        null => "",
        "num" => $"{num}. ",
        "check" => p.Checked ? "- [x] " : "- [ ] ",
        _ => "- ",
    };

    private static string Inline(Paragraph p) => string.Concat(p.Runs.Select(Run));

    private static string Run(RichRun r)
    {
        var text = r.Text;
        if (text.Length == 0) return "";
        string open = "", close = "";
        if (r.Bold) { open += "**"; close = "**" + close; }
        if (r.Italic) { open += "*"; close = "*" + close; }
        if (r.Strike) { open = "~~" + open; close += "~~"; }
        if (open.Length == 0 && close.Length == 0) return text;

        int a = 0; while (a < text.Length && char.IsWhiteSpace(text[a])) a++;
        int b = text.Length; while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
        if (a >= b) return text;
        return text[..a] + open + text[a..b] + close + text[b..];
    }

    public static string SafeName(string raw)
    {
        var sb = new StringBuilder();
        bool lastDash = false;
        foreach (char c in (raw ?? "").Trim())
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') { sb.Append(c); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        var s = sb.ToString().Trim().Trim('-', '.').Trim();
        return s.Length == 0 ? "Untitled" : s;
    }
}
