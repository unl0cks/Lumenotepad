using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Lumenotepad.Editor;

namespace Lumenotepad.Services;

public enum ExportFormat { Txt, Markdown, Html, Rtf, Pdf, Docx, Odt, Epub }

/// <summary>Exports a page (title + its freeform canvas) to any of the supported document formats
/// (M10). Boxes are emitted in reading order (top-to-bottom, then left-to-right); empty boxes are
/// skipped. The plain-text family (TXT/MD/HTML/RTF) is pure and unit-tested; PDF is drawn with
/// SkiaSharp and embeds image boxes, draws divider rules, and draws real vector checkboxes;
/// DOCX/ODT/EPUB are minimal-but-valid zip packages that honor headings, alignment,
/// bold/italic/underline/strike, text color, and links.</summary>
public static class PageExport
{
    public static readonly (ExportFormat Fmt, string Label, string Ext)[] Formats =
    {
        (ExportFormat.Txt, "Plain text", ".txt"),
        (ExportFormat.Markdown, "Markdown", ".md"),
        (ExportFormat.Html, "Web page (HTML)", ".html"),
        (ExportFormat.Rtf, "Rich text (RTF)", ".rtf"),
        (ExportFormat.Pdf, "PDF", ".pdf"),
        (ExportFormat.Docx, "Word (DOCX)", ".docx"),
        (ExportFormat.Odt, "OpenDocument (ODT)", ".odt"),
        (ExportFormat.Epub, "eBook (EPUB)", ".epub"),
    };

    public static string ExtensionFor(ExportFormat fmt) => Formats.First(f => f.Fmt == fmt).Ext;

    /// <summary>Produce the file bytes for a page in the given format. <paramref name="imageRoot"/>
    /// is the notebook folder image-box paths resolve against — only PDF embeds pictures.</summary>
    public static byte[] Export(ExportFormat fmt, string title, CanvasDocument doc, string? imageRoot = null)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        return fmt switch
        {
            ExportFormat.Txt => Utf8(ToText(title, doc)),
            ExportFormat.Markdown => Utf8(ToMarkdown(title, doc)),
            ExportFormat.Html => Utf8(ToHtml(title, doc, standalone: true)),
            ExportFormat.Rtf => Utf8(ToRtf(title, doc)),
            ExportFormat.Pdf => ToPdf(title, doc, imageRoot),
            ExportFormat.Docx => ToDocx(title, doc),
            ExportFormat.Odt => ToOdt(title, doc),
            ExportFormat.Epub => ToEpub(title, doc),
            _ => Utf8(ToText(title, doc)),
        };
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static IEnumerable<NoteBox> Boxes(CanvasDocument doc) =>
        doc.Boxes.Where(b => !b.IsEmpty).OrderBy(b => b.Y).ThenBy(b => b.X);

    private static string AttachName(NoteBox b) => Path.GetFileName(b.AttachPath ?? "");

    /// <summary>A table cell's plain text (newlines flattened to spaces) for the text-family exports.</summary>
    private static string CellText(RichDocument cell) => cell.GetText().Replace('\n', ' ').Trim();

    /// <summary>A GitHub-style Markdown table (first row treated as the header).</summary>
    private static string MarkdownTable(NoteTable t)
    {
        string Row(IEnumerable<string> cells) => "| " + string.Join(" | ", cells) + " |";
        var lines = new List<string>();
        lines.Add(Row(t.Rows[0].Select(c => CellText(c).Replace("|", "\\|"))));
        lines.Add("| " + string.Join(" | ", Enumerable.Repeat("---", t.ColCount)) + " |");
        foreach (var row in t.Rows.Skip(1))
            lines.Add(Row(row.Select(c => CellText(c).Replace("|", "\\|"))));
        return string.Join("\n", lines);
    }

    /// <summary>A monospaced plain-text table with padded columns (TXT export).</summary>
    private static string TextTable(NoteTable t)
    {
        var cells = t.Rows.Select(r => r.Select(CellText).ToList()).ToList();
        var widths = new int[t.ColCount];
        for (int c = 0; c < t.ColCount; c++)
            widths[c] = Math.Max(1, cells.Max(r => r[c].Length));
        var sb = new StringBuilder();
        void Line() => sb.Append('+').Append(string.Join("+", widths.Select(w => new string('-', w + 2)))).Append('+').Append('\n');
        Line();
        foreach (var row in cells)
        {
            sb.Append("| ");
            for (int c = 0; c < t.ColCount; c++) sb.Append(row[c].PadRight(widths[c])).Append(" | ");
            sb.Append('\n');
            Line();
        }
        return sb.ToString();
    }

    private static string HtmlTable(NoteTable t)
    {
        var sb = new StringBuilder("<table class=\"grid\">\n");
        foreach (var row in t.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row) sb.Append("<td>").Append(InlineHtmlCell(cell)).Append("</td>");
            sb.Append("</tr>\n");
        }
        return sb.Append("</table>\n").ToString();
    }

    /// <summary>A table cell's rich inline HTML (its paragraphs joined by &lt;br&gt;).</summary>
    private static string InlineHtmlCell(RichDocument cell) =>
        string.Join("<br>", cell.Paragraphs.Select(InlineHtml));

    /// <summary>Plain-text marker for a tagged paragraph (TXT/MD; HTML gets the colored glyph).</summary>
    private static string TagMark(Paragraph p) => p.Tag switch
    {
        "important" => "[!] ", "question" => "[?] ", "idea" => "[idea] ",
        "star" => "[fav] ", "flag" => "[flag] ", _ => "",
    };

    private static string TagHtml(Paragraph p) =>
        p.Tag is { } t && TagStyles.Info(t) is { } g
            ? $"<span style=\"color:{g.Color};font-weight:bold\">{Esc(g.Glyph)}</span> "
            : "";

    // ---- plain text ----

    public static string ToText(string title, CanvasDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title).AppendLine(new string('=', title.Length)).AppendLine();
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { sb.AppendLine(new string('-', 24)).AppendLine(); continue; }
            if (box.Table is { } tt) { sb.Append(TextTable(tt)).AppendLine(); continue; }
            if (box.AttachPath is { Length: > 0 }) { sb.AppendLine("Attachment: " + AttachName(box)).AppendLine(); continue; }
            if (box.ImagePath is not null) continue;   // pictures can't ride along in plain text
            int num = 0;
            foreach (var p in box.Doc.Paragraphs)
            {
                if (p.Bullet == "num") num++; else num = 0;
                string prefix = p.Bullet switch
                {
                    null => "", "num" => $"{num}. ",
                    "check" => p.Checked ? "[x] " : "[ ] ", _ => "• ",
                };
                sb.AppendLine(prefix + TagMark(p) + p.Text);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    // ---- markdown (extends the box walk with headings + links) ----

    public static string ToMarkdown(string title, CanvasDocument doc)
    {
        var blocks = new List<string> { "# " + title };
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { blocks.Add("---"); continue; }
            if (box.Table is { } tt) { blocks.Add(MarkdownTable(tt)); continue; }
            if (box.AttachPath is { Length: > 0 }) { blocks.Add("**Attachment:** " + AttachName(box)); continue; }
            if (box.ImagePath is not null) continue;   // the .md leaves the notebook — no path survives
            var lines = new List<string>();
            int num = 0;
            foreach (var p in box.Doc.Paragraphs)
            {
                if (p.Bullet == "num") num++; else num = 0;
                string body = string.Concat(p.Runs.Select(MdRun));
                string heading = p.Style switch
                {
                    ParaStyle.Title or ParaStyle.Heading1 => "## ",
                    ParaStyle.Subtitle or ParaStyle.Heading2 => "### ",
                    ParaStyle.Heading3 => "#### ",
                    _ => "",
                };
                string prefix = p.Bullet switch
                {
                    null => heading, "num" => $"{num}. ",
                    "check" => p.Checked ? "- [x] " : "- [ ] ", _ => "- ",
                };
                lines.Add(prefix + TagMark(p) + body);      // tag AFTER the marker so "## " stays a heading
            }
            blocks.Add(string.Join("\n", lines));
        }
        return string.Join("\n\n", blocks) + "\n";
    }

    private static string MdRun(RichRun r)
    {
        var text = r.Text;
        if (text.Length == 0) return "";
        if (r.Link is { Length: > 0 } url) return $"[{text}]({url})";
        string open = "", close = "";
        if (r.Bold) { open += "**"; close = "**" + close; }
        if (r.Italic) { open += "*"; close = "*" + close; }
        if (r.Strike) { open = "~~" + open; close += "~~"; }
        if (open.Length == 0) return text;
        int a = 0; while (a < text.Length && char.IsWhiteSpace(text[a])) a++;
        int b = text.Length; while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
        if (a >= b) return text;
        return text[..a] + open + text[a..b] + close + text[b..];
    }

    // ---- HTML (also the EPUB body) ----

    public static string ToHtml(string title, CanvasDocument doc, bool standalone)
    {
        var body = new StringBuilder();
        body.Append("<h1>").Append(Esc(title)).Append("</h1>\n");
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { body.Append("<hr/>\n"); continue; }   // self-closed: EPUB is XHTML
            if (box.Table is { } tt) { body.Append(HtmlTable(tt)); continue; }
            if (box.AttachPath is { Length: > 0 })
            { body.Append("<p class=\"attachment\">📎 ").Append(Esc(AttachName(box))).Append("</p>\n"); continue; }
            if (box.ImagePath is not null) continue;
            body.Append("<section>\n");
            AppendHtmlParagraphs(body, box.Doc);
            body.Append("</section>\n");
        }
        if (!standalone) return body.ToString();
        return "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n" +
               $"<title>{Esc(title)}</title>\n<style>{HtmlCss}</style></head>\n<body>\n" +
               body + "</body></html>\n";
    }

    private const string HtmlCss =
        "body{font-family:Segoe UI,system-ui,sans-serif;max-width:820px;margin:40px auto;padding:0 24px;line-height:1.5;}" +
        "ul{list-style:disc;}ul.tasklist{list-style:none;padding-left:1em;}li{margin:2px 0;}a{color:#2f6fc4;}" +
        "table.grid{border-collapse:collapse;margin:12px 0;}table.grid td{border:1px solid #bbb;padding:5px 9px;vertical-align:top;}";

    private static void AppendHtmlParagraphs(StringBuilder body, RichDocument d)
    {
        int i = 0;
        var ps = d.Paragraphs;
        while (i < ps.Count)
        {
            var p = ps[i];
            if (p.Bullet == "check")     // a checklist: real (disabled) checkboxes, not plain bullets
            {
                body.Append("<ul class=\"tasklist\">\n");
                while (i < ps.Count && ps[i].Bullet == "check")
                {
                    body.Append("<li><input type=\"checkbox\" disabled")
                        .Append(ps[i].Checked ? " checked" : "").Append("> ")
                        .Append(TagHtml(ps[i])).Append(InlineHtml(ps[i])).Append("</li>\n");
                    i++;
                }
                body.Append("</ul>\n");
                continue;
            }
            if (p.Bullet is "dot" or "arrow" or "star" or "heart" or "flower" or "spark" or "num")
            {
                bool ordered = p.Bullet == "num";
                body.Append(ordered ? "<ol>\n" : "<ul>\n");
                while (i < ps.Count && ps[i].Bullet == p.Bullet)
                {
                    body.Append("<li>").Append(TagHtml(ps[i])).Append(InlineHtml(ps[i])).Append("</li>\n");
                    i++;
                }
                body.Append(ordered ? "</ol>\n" : "</ul>\n");
                continue;
            }
            string tag = p.Style switch
            {
                ParaStyle.Title or ParaStyle.Heading1 => "h1",
                ParaStyle.Subtitle or ParaStyle.Heading2 => "h2",
                ParaStyle.Heading3 => "h3",
                _ => "p",
            };
            string align = p.Align switch
            {
                TextAlign.Center => " style=\"text-align:center\"",
                TextAlign.Right => " style=\"text-align:right\"",
                TextAlign.Justify => " style=\"text-align:justify\"",
                _ => "",
            };
            body.Append('<').Append(tag).Append(align).Append('>')
                .Append(TagHtml(p)).Append(InlineHtml(p)).Append("</").Append(tag).Append(">\n");
            i++;
        }
    }

    private static string InlineHtml(Paragraph p) => string.Concat(p.Runs.Select(InlineHtmlRun));

    private static string InlineHtmlRun(RichRun r)
    {
        if (r.Text.Length == 0) return "";
        var sb = new StringBuilder();
        var close = new Stack<string>();
        void Wrap(string open, string closeTag) { sb.Append(open); close.Push(closeTag); }
        if (r.Link is { Length: > 0 } url) Wrap($"<a href=\"{Esc(url)}\">", "</a>");
        if (r.Bold) Wrap("<strong>", "</strong>");
        if (r.Italic) Wrap("<em>", "</em>");
        if (r.Underline) Wrap("<u>", "</u>");
        if (r.Strike) Wrap("<s>", "</s>");
        if (r.Baseline == Baseline.Super) Wrap("<sup>", "</sup>");
        if (r.Baseline == Baseline.Sub) Wrap("<sub>", "</sub>");
        var styles = new List<string>();
        if (r.Color is { Length: > 0 } c) styles.Add("color:" + c);
        if (r.Highlight is { Length: > 0 } h) styles.Add("background-color:" + HexNoAlpha(h));
        if (styles.Count > 0) Wrap($"<span style=\"{string.Join(';', styles)}\">", "</span>");
        sb.Append(Esc(r.Text));
        while (close.Count > 0) sb.Append(close.Pop());
        return sb.ToString();
    }

    // ---- RTF ----

    public static string ToRtf(string title, CanvasDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}");
        sb.Append(@"{\colortbl;\red47\green111\blue196;}");   // color 1 = link blue
        sb.Append(@"\fs48\b ").Append(RtfEsc(title)).Append(@"\b0\fs22\par\par");
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { sb.Append(@"\ql ").Append(new string('_', 32)).Append(@"\par\par"); continue; }
            if (box.Table is { } tt)
            {
                foreach (var row in tt.Rows)
                    sb.Append(@"\ql ").Append(RtfEsc(string.Join("   |   ", row.Select(CellText)))).Append(@"\par");
                sb.Append(@"\par");
                continue;
            }
            if (box.AttachPath is { Length: > 0 })
            { sb.Append(@"\ql\i Attachment: ").Append(RtfEsc(AttachName(box))).Append(@"\i0\par\par"); continue; }
            if (box.ImagePath is not null) continue;
            int num = 0;
            foreach (var p in box.Doc.Paragraphs)
            {
                if (p.Bullet == "num") num++; else num = 0;
                sb.Append(p.Align switch
                {
                    TextAlign.Center => @"\qc ", TextAlign.Right => @"\qr ",
                    TextAlign.Justify => @"\qj ", _ => @"\ql ",
                });
                int fs = (int)Math.Round(RichTextEditor.BaseSizeFor(p.Style, 11) * 2);   // RTF half-points
                bool headingBold = RichTextEditor.BaseWeightFor(p.Style) >= Avalonia.Media.FontWeight.SemiBold;
                sb.Append(@"\fs").Append(fs).Append(' ');
                string bullet = p.Bullet switch
                {
                    null => "", "num" => $"{num}. ",
                    "check" => p.Checked ? "[x] " : "[ ] ", _ => "• ",
                };
                if (bullet.Length > 0) sb.Append(RtfEsc(bullet));
                foreach (var r in p.Runs) AppendRtfRun(sb, r, headingBold);
                sb.Append(@"\par");
            }
            sb.Append(@"\par");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendRtfRun(StringBuilder sb, RichRun r, bool headingBold)
    {
        if (r.Text.Length == 0) return;
        bool bold = r.Bold || headingBold;
        bool link = r.Link is { Length: > 0 };
        if (bold) sb.Append(@"\b ");
        if (r.Italic) sb.Append(@"\i ");
        if (r.Underline || link) sb.Append(@"\ul ");
        if (r.Strike) sb.Append(@"\strike ");
        if (r.Baseline == Baseline.Super) sb.Append(@"\super ");
        if (r.Baseline == Baseline.Sub) sb.Append(@"\sub ");
        if (link) sb.Append(@"\cf1 ");
        sb.Append(RtfEsc(r.Text));
        if (link) sb.Append(@"\cf0 ");
        if (r.Baseline != Baseline.Normal) sb.Append(@"\nosupersub ");
        if (r.Strike) sb.Append(@"\strike0 ");
        if (r.Underline || link) sb.Append(@"\ulnone ");
        if (r.Italic) sb.Append(@"\i0 ");
        if (bold) sb.Append(@"\b0 ");
    }

    // ---- PDF (SkiaSharp) ----

    private static byte[] ToPdf(string title, CanvasDocument doc, string? imageRoot) =>
        ToPdfMulti(new[] { (title, doc) }, imageRoot);

    /// <summary>Render several pages into one PDF (each starts on a fresh page). Used both by the
    /// single-page export and by "Open section as PDF".</summary>
    public static byte[] ToPdfMulti(IReadOnlyList<(string Title, CanvasDocument Doc)> items, string? imageRoot = null)
    {
        const float W = 595, H = 842, Margin = 54;         // A4 points
        const float MaxW = W - 2 * Margin;
        using var ms = new MemoryStream();
        using (var pdf = SkiaSharp.SKDocument.CreatePdf(ms))
        {
            var canvas = pdf.BeginPage(W, H);
            float y = Margin;

            void NewPage() { pdf.EndPage(); canvas = pdf.BeginPage(W, H); y = Margin; }
            void EnsureRoom(float need) { if (y + need > H - Margin) NewPage(); }

            void DrawParagraph(IEnumerable<(string Word, TextPaint Paint)> words, float lineHeight, float indent = 0)
            {
                float x = Margin + indent;
                float maxX = W - Margin;
                foreach (var (word, paint) in words)
                {
                    float w = paint.Measure(word + " ");
                    if (x + w > maxX && x > Margin + indent)
                    {
                        x = Margin + indent; y += lineHeight;
                        if (y > H - Margin) NewPage();
                    }
                    canvas.DrawText(word, x, y, SkiaSharp.SKTextAlign.Left, paint.Font, paint.Paint);
                    x += w;
                }
                y += lineHeight * 1.4f;
                if (y > H - Margin) NewPage();
            }

            // A real drawn checkbox (no font glyph to go missing): a rounded square hugging the
            // first line's baseline, with an accent tick when done.
            void DrawCheckbox(bool ticked, float size)
            {
                float side = size * 0.85f;
                float top = y - side + size * 0.12f;
                var rect = new SkiaSharp.SKRect(Margin, top, Margin + side, top + side);
                using var stroke = new SkiaSharp.SKPaint
                {
                    Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1.2f,
                    Color = new SkiaSharp.SKColor(0x77, 0x77, 0x77), IsAntialias = true,
                };
                canvas.DrawRoundRect(rect, 2.5f, 2.5f, stroke);
                if (!ticked) return;
                using var tick = new SkiaSharp.SKPaint
                {
                    Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1.6f,
                    Color = new SkiaSharp.SKColor(0x2F, 0x6F, 0xC4), IsAntialias = true,
                    StrokeCap = SkiaSharp.SKStrokeCap.Round,
                };
                using var path = new SkiaSharp.SKPath();
                path.MoveTo(rect.Left + side * 0.22f, rect.Top + side * 0.52f);
                path.LineTo(rect.Left + side * 0.44f, rect.Top + side * 0.74f);
                path.LineTo(rect.Left + side * 0.80f, rect.Top + side * 0.26f);
                canvas.DrawPath(path, tick);
            }

            using var titlePaint = MakePaint(bold: true, italic: false, link: false, 26);
            for (int it = 0; it < items.Count; it++)
            {
                if (it > 0) NewPage();          // each page/section entry starts fresh
                y += 26;
                canvas.DrawText(items[it].Title, Margin, y, SkiaSharp.SKTextAlign.Left, titlePaint.Font, titlePaint.Paint);
                y += 30;

                foreach (var box in Boxes(items[it].Doc))
                {
                    if (box.Divider is not null) { DrawDivider(box); continue; }
                    if (box.Table is { } tbl) { DrawTable(tbl); continue; }
                    if (box.ImagePath is { Length: > 0 }) { DrawImage(box); continue; }
                    if (box.AttachPath is { Length: > 0 })
                    {
                        // The file itself can't ride inside the page — a quiet italic marker names it.
                        EnsureRoom(20);
                        using var ap = MakePaint(false, true, false, 12);
                        ap.Paint.Color = new SkiaSharp.SKColor(0x77, 0x77, 0x77);
                        canvas.DrawText("Attachment: " + AttachName(box), Margin, y, SkiaSharp.SKTextAlign.Left, ap.Font, ap.Paint);
                        y += 22;
                        continue;
                    }

                    int num = 0;
                    foreach (var p in box.Doc.Paragraphs)
                    {
                        if (p.Bullet == "num") num++; else num = 0;
                        double size = RichTextEditor.BaseSizeFor(p.Style, 12);
                        bool headBold = RichTextEditor.BaseWeightFor(p.Style) >= Avalonia.Media.FontWeight.SemiBold;
                        bool check = p.Bullet == "check";
                        string bullet = p.Bullet switch
                        {
                            null or "check" => "", "num" => $"{num}.", _ => "•",
                        };
                        var words = new List<(string, TextPaint)>();
                        var paints = new List<TextPaint>();
                        if (bullet.Length > 0)
                        {
                            var bp = MakePaint(false, false, false, (float)size);
                            paints.Add(bp);
                            words.Add((bullet, bp));
                        }
                        foreach (var r in p.Runs)
                        {
                            if (r.Text.Length == 0) continue;
                            var paint = MakePaint(r.Bold || headBold, r.Italic, r.Link is { Length: > 0 }, (float)size);
                            paints.Add(paint);
                            foreach (var word in r.Text.Split(' ').Where(s => s.Length > 0))
                                words.Add((word, paint));
                        }
                        if (check)
                        {
                            EnsureRoom((float)size * 1.5f);
                            DrawCheckbox(p.Checked, (float)size);
                            if (words.Count == 0) { y += (float)size * 1.3f * 1.4f; continue; }
                            DrawParagraph(words, (float)size * 1.3f, indent: (float)size * 0.85f + 7f);
                        }
                        else
                        {
                            if (words.Count == 0) { y += (float)size * 1.4f; continue; }
                            DrawParagraph(words, (float)size * 1.3f);
                        }
                        foreach (var pt in paints) pt.Dispose();
                    }
                    y += 8;
                }
            }
            pdf.EndPage();

            // A drawn table grid: equal columns across the content width, one line per cell (trimmed
            // with an ellipsis if it overflows), hairline borders.
            void DrawTable(NoteTable t)
            {
                float colW = MaxW / t.ColCount;
                const float rowH = 22f, pad = 5f;
                using var cellPaint = MakePaint(false, false, false, 11);
                using var grid = new SkiaSharp.SKPaint
                {
                    Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 0.8f,
                    Color = new SkiaSharp.SKColor(0x99, 0x99, 0x99), IsAntialias = true,
                };
                foreach (var row in t.Rows)
                {
                    EnsureRoom(rowH + 2);
                    float top = y - 12;
                    for (int c = 0; c < t.ColCount; c++)
                    {
                        float cx = Margin + c * colW;
                        canvas.DrawRect(new SkiaSharp.SKRect(cx, top, cx + colW, top + rowH), grid);
                        canvas.DrawText(TrimToWidth(CellText(row[c]), cellPaint, colW - pad * 2),
                                        cx + pad, top + rowH - 7, SkiaSharp.SKTextAlign.Left, cellPaint.Font, cellPaint.Paint);
                    }
                    y += rowH;
                }
                y += 12;
            }

            static string TrimToWidth(string s, TextPaint p, float maxW)
            {
                if (p.Measure(s) <= maxW) return s;
                while (s.Length > 1 && p.Measure(s + "…") > maxW) s = s[..^1];
                return s + "…";
            }

            // A drawn rule matching the on-page divider: horizontal at its own length, vertical as
            // a centered column stroke (reading order can't keep true side-by-side placement).
            void DrawDivider(NoteBox box)
            {
                using var rule = new SkiaSharp.SKPaint
                {
                    Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1.4f,
                    Color = new SkiaSharp.SKColor(0x9A, 0x9A, 0x9A), IsAntialias = true,
                    StrokeCap = SkiaSharp.SKStrokeCap.Round,
                };
                if (box.Divider == "v")
                {
                    float len = Math.Clamp((float)box.H, 40, 260);
                    EnsureRoom(len + 18);
                    canvas.DrawLine(W / 2, y, W / 2, y + len, rule);
                    y += len + 18;
                }
                else
                {
                    float len = Math.Clamp((float)box.Width, 40, MaxW);
                    EnsureRoom(24);
                    y += 4;
                    canvas.DrawLine(Margin, y, Margin + len, y, rule);
                    y += 18;
                }
            }

            // The actual picture, embedded: fit to the box's on-page width, capped to the content
            // column and one page tall. Missing/unreadable files are skipped, never fatal.
            void DrawImage(NoteBox box)
            {
                SkiaSharp.SKBitmap? bmp = null;
                try
                {
                    string full = imageRoot is { Length: > 0 }
                        ? Path.Combine(imageRoot, box.ImagePath!) : box.ImagePath!;
                    if (File.Exists(full)) bmp = SkiaSharp.SKBitmap.Decode(full);
                }
                catch { /* unreadable image → skip it */ }
                if (bmp is null || bmp.Width <= 0 || bmp.Height <= 0) return;
                using (bmp)
                {
                    float drawW = Math.Min((float)box.Width, MaxW);
                    float drawH = bmp.Height * (drawW / bmp.Width);
                    float maxH = H - 2 * Margin;
                    if (drawH > maxH) { drawW *= maxH / drawH; drawH = maxH; }
                    EnsureRoom(drawH);
                    float top = y - 8;                      // y is a text baseline; hug the flow
                    canvas.DrawBitmap(bmp, new SkiaSharp.SKRect(Margin, top, Margin + drawW, top + drawH));
                    y = top + drawH + 24;
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>An SKPaint + SKFont pair — SkiaSharp 3 moved text styling (size/typeface/measure)
    /// off SKPaint onto SKFont, so the drawn-PDF path carries both around together.</summary>
    private sealed record TextPaint(SkiaSharp.SKPaint Paint, SkiaSharp.SKFont Font) : IDisposable
    {
        public float Measure(string s) => Font.MeasureText(s);
        public void Dispose() { Paint.Dispose(); Font.Dispose(); }
    }

    private static TextPaint MakePaint(bool bold, bool italic, bool link, float size) => new(
        new SkiaSharp.SKPaint
        {
            IsAntialias = true,
            Color = link ? new SkiaSharp.SKColor(0x2F, 0x6F, 0xC4) : SkiaSharp.SKColors.Black,
        },
        new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI",
            new SkiaSharp.SKFontStyle(bold ? SkiaSharp.SKFontStyleWeight.Bold : SkiaSharp.SKFontStyleWeight.Normal,
                SkiaSharp.SKFontStyleWidth.Normal,
                italic ? SkiaSharp.SKFontStyleSlant.Italic : SkiaSharp.SKFontStyleSlant.Upright)), size));

    // ---- DOCX (minimal OOXML) ----

    private static byte[] ToDocx(string title, CanvasDocument doc)
    {
        var body = new StringBuilder();
        body.Append(DocxParagraph(title, TextAlign.Left, 40, true, null));
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { body.Append(DocxParagraph(new string('—', 12), TextAlign.Left, 22, false, null)); continue; }
            if (box.Table is { } tt)
            {
                foreach (var row in tt.Rows)
                    body.Append(DocxParagraph(string.Join("   |   ", row.Select(CellText)), TextAlign.Left, 22, false, null));
                continue;
            }
            if (box.AttachPath is { Length: > 0 }) { body.Append(DocxParagraph("Attachment: " + AttachName(box), TextAlign.Left, 22, false, null)); continue; }
            if (box.ImagePath is not null) continue;
            foreach (var p in box.Doc.Paragraphs)
                body.Append(DocxParagraphFrom(p));
        }

        string document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body>" + body + "</w:body></w:document>";

        return Zip(new (string, byte[])[]
        {
            ("[Content_Types].xml", Utf8(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>")),
            ("_rels/.rels", Utf8(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>")),
            ("word/document.xml", Utf8(document)),
        });
    }

    private static string DocxParagraphFrom(Paragraph p)
    {
        int half = (int)Math.Round(RichTextEditor.BaseSizeFor(p.Style, 11) * 2);
        bool headBold = RichTextEditor.BaseWeightFor(p.Style) >= Avalonia.Media.FontWeight.SemiBold;
        // Word does proper font fallback, so the real ballot glyphs render (only the drawn PDF lacked them).
        string bullet = p.Bullet switch
        {
            null => "", "num" => "• ", "check" => p.Checked ? "☑ " : "☐ ", _ => "• ",
        };
        var runs = new StringBuilder();
        if (bullet.Length > 0) runs.Append(DocxRun(bullet, false, false, false, false, half, null));
        foreach (var r in p.Runs)
            if (r.Text.Length > 0)
                runs.Append(DocxRun(r.Text, r.Bold || headBold, r.Italic, r.Underline || r.Link is { Length: > 0 },
                    r.Strike, half, r.Link is { Length: > 0 } ? "2F6FC4" : HexNoHash(r.Color)));
        string jc = p.Align switch
        {
            TextAlign.Center => "center", TextAlign.Right => "right", TextAlign.Justify => "both", _ => "left",
        };
        return $"<w:p><w:pPr><w:jc w:val=\"{jc}\"/></w:pPr>{runs}</w:p>";
    }

    private static string DocxParagraph(string text, TextAlign align, int half, bool bold, string? color) =>
        $"<w:p><w:pPr><w:jc w:val=\"left\"/></w:pPr>{DocxRun(text, bold, false, false, false, half, color)}</w:p>";

    private static string DocxRun(string text, bool b, bool i, bool u, bool s, int half, string? colorHex)
    {
        var rpr = new StringBuilder("<w:rPr>");
        if (b) rpr.Append("<w:b/>");
        if (i) rpr.Append("<w:i/>");
        if (u) rpr.Append("<w:u w:val=\"single\"/>");
        if (s) rpr.Append("<w:strike/>");
        if (colorHex is { Length: > 0 }) rpr.Append($"<w:color w:val=\"{colorHex}\"/>");
        rpr.Append($"<w:sz w:val=\"{half}\"/></w:rPr>");
        return $"<w:r>{rpr}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";
    }

    // ---- ODT (minimal ODF) ----

    private static byte[] ToOdt(string title, CanvasDocument doc)
    {
        var content = new StringBuilder();
        content.Append($"<text:h text:outline-level=\"1\">{Esc(title)}</text:h>");
        foreach (var box in Boxes(doc))
        {
            if (box.Divider is not null) { content.Append($"<text:p>{new string('—', 12)}</text:p>"); continue; }
            if (box.Table is { } tt)
            {
                foreach (var row in tt.Rows)
                    content.Append($"<text:p>{Esc(string.Join("   |   ", row.Select(CellText)))}</text:p>");
                continue;
            }
            if (box.AttachPath is { Length: > 0 }) { content.Append($"<text:p>Attachment: {Esc(AttachName(box))}</text:p>"); continue; }
            if (box.ImagePath is not null) continue;
            foreach (var p in box.Doc.Paragraphs)
            {
                bool heading = p.Style != ParaStyle.Body;
                string tag = heading ? "text:h" : "text:p";
                string lvl = heading ? " text:outline-level=\"2\"" : "";
                var runs = new StringBuilder();
                string bullet = p.Bullet switch          // LibreOffice font-fallback renders the glyphs
                {
                    null => "", "num" => "• ", "check" => p.Checked ? "☑ " : "☐ ", _ => "• ",
                };
                if (bullet.Length > 0) runs.Append(Esc(bullet));
                foreach (var r in p.Runs) runs.Append(OdtRun(r));
                content.Append($"<{tag}{lvl}>{runs}</{tag.Split(' ')[0]}>");
            }
        }

        string contentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
            "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.2\">" +
            "<office:body><office:text>" + content + "</office:text></office:body></office:document-content>";

        string manifest =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">" +
            "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
            "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
            "</manifest:manifest>";

        return Zip(new (string, byte[])[]
        {
            ("mimetype", Utf8("application/vnd.oasis.opendocument.text")),   // stored first, uncompressed
            ("content.xml", Utf8(contentXml)),
            ("META-INF/manifest.xml", Utf8(manifest)),
        }, storeFirst: true);
    }

    private static string OdtRun(RichRun r)
    {
        if (r.Text.Length == 0) return "";
        // ODF styling needs automatic-styles plumbing; keep it readable — bold/italic via nested spans
        // isn't valid without style defs, so emit the text (structure + headings are preserved).
        string text = Esc(r.Text);
        if (r.Link is { Length: > 0 } url)
            return $"<text:a xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"{Esc(url)}\">{text}</text:a>";
        return text;
    }

    // ---- EPUB (zipped XHTML) ----

    private static byte[] ToEpub(string title, CanvasDocument doc)
    {
        string xhtml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>" +
            $"<title>{Esc(title)}</title><meta charset=\"utf-8\"/></head><body>\n" +
            ToHtml(title, doc, standalone: false) + "</body></html>";

        string opf =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\">" +
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            $"<dc:title>{Esc(title)}</dc:title><dc:language>en</dc:language>" +
            "<dc:identifier id=\"id\">lumenotepad-export</dc:identifier>" +
            "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>" +
            "<manifest>" +
            "<item id=\"page\" href=\"page.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "<item id=\"nav\" href=\"nav.xhtml\" properties=\"nav\" media-type=\"application/xhtml+xml\"/>" +
            "</manifest><spine><itemref idref=\"page\"/></spine></package>";

        string nav =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head>" +
            "<title>Contents</title></head><body><nav epub:type=\"toc\"><ol>" +
            $"<li><a href=\"page.xhtml\">{Esc(title)}</a></li></ol></nav></body></html>";

        string container =
            "<?xml version=\"1.0\"?>\n<container version=\"1.0\" " +
            "xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>" +
            "</rootfiles></container>";

        return Zip(new (string, byte[])[]
        {
            ("mimetype", Utf8("application/epub+zip")),      // stored first, uncompressed
            ("META-INF/container.xml", Utf8(container)),
            ("OEBPS/content.opf", Utf8(opf)),
            ("OEBPS/nav.xhtml", Utf8(nav)),
            ("OEBPS/page.xhtml", Utf8(xhtml)),
        }, storeFirst: true);
    }

    // ---- helpers ----

    /// <summary>Build a zip archive. <paramref name="storeFirst"/> writes the FIRST entry uncompressed
    /// (the ODF/EPUB "mimetype" rule) so the package is recognized.</summary>
    private static byte[] Zip(IReadOnlyList<(string Name, byte[] Data)> entries, bool storeFirst = false)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 0; i < entries.Count; i++)
            {
                var (name, data) = entries[i];
                var level = storeFirst && i == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var e = zip.CreateEntry(name, level);
                using var s = e.Open();
                s.Write(data, 0, data.Length);
            }
        return ms.ToArray();
    }

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    private static string RtfEsc(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c > 127) sb.Append(@"\u").Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string HexNoHash(string? hex) =>
        hex is { Length: > 0 } ? HexNoAlpha(hex).TrimStart('#') : "";

    /// <summary>Drop a leading '#AA' alpha (8-digit) down to '#RRGGBB' for CSS/Office color values.</summary>
    private static string HexNoAlpha(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];              // AARRGGBB → RRGGBB
        return "#" + h;
    }
}
