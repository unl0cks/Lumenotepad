using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumenotepad.Editor;

namespace Lumenotepad.Services;

public static class PdfText
{
    private const string Lib = "pdfium";
    private const int Scale = 100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FsRectF { public float Left, Top, Right, Bottom; }

    [DllImport(Lib)] private static extern void FPDF_InitLibrary();
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, IntPtr password);
    [DllImport(Lib)] private static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Lib)] private static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Lib)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] private static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int deviceX, out int deviceY);
    [DllImport(Lib)] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Lib)] private static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Lib)] private static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Lib)] private static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
    [DllImport(Lib)] private static extern int FPDFText_IsGenerated(IntPtr textPage, int index);
    [DllImport(Lib)] private static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out FsRectF rect);

    private static bool _initialized;
    private static bool _failureLogged;

    public static IReadOnlyList<PdfPageText> ReadAll(byte[] pdf)
    {
        if (pdf.Length == 0) return Array.Empty<PdfPageText>();
        IntPtr buf = IntPtr.Zero, doc = IntPtr.Zero;
        try
        {
            buf = Marshal.AllocHGlobal(pdf.Length);
            Marshal.Copy(pdf, 0, buf, pdf.Length);
            int count;
            lock (PdfRenderer.Gate)
            {
                if (!_initialized) { FPDF_InitLibrary(); _initialized = true; }
                doc = FPDF_LoadMemDocument64(buf, (UIntPtr)pdf.Length, IntPtr.Zero);
                count = doc == IntPtr.Zero ? 0 : FPDF_GetPageCount(doc);
            }
            var pages = new List<PdfPageText>(count);
            for (int i = 0; i < count; i++)
                lock (PdfRenderer.Gate) pages.Add(ReadPage(doc, i));
            return pages;
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                StartupLog.Mark($"pdf text unavailable: {ex.GetType().Name}: {ex.Message}");
            }
            return Array.Empty<PdfPageText>();
        }
        finally
        {
            if (doc != IntPtr.Zero) lock (PdfRenderer.Gate) FPDF_CloseDocument(doc);
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
    }

    private static PdfPageText ReadPage(IntPtr doc, int index)
    {
        IntPtr page = FPDF_LoadPage(doc, index);
        if (page == IntPtr.Zero) return new PdfPageText(index, Array.Empty<PdfChar>());
        IntPtr text = IntPtr.Zero;
        try
        {
            text = FPDFText_LoadPage(page);
            if (text == IntPtr.Zero) return new PdfPageText(index, Array.Empty<PdfChar>());
            int n = Math.Max(0, FPDFText_CountChars(text));
            var chars = new PdfChar[n];
            for (int c = 0; c < n; c++)
            {
                int cp = (int)FPDFText_GetUnicode(text, c);
                bool generated = FPDFText_IsGenerated(text, c) == 1;
                double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                if (cp is not ('\r' or '\n') && FPDFText_GetLooseCharBox(text, c, out var r) != 0
                    && r.Right > r.Left && r.Top > r.Bottom)
                {
                    var (ax, ay) = ToUnit(page, r.Left, r.Top);
                    var (bx, by) = ToUnit(page, r.Right, r.Bottom);
                    x0 = Math.Min(ax, bx); x1 = Math.Max(ax, bx);
                    y0 = Math.Min(ay, by); y1 = Math.Max(ay, by);
                }
                chars[c] = new PdfChar(cp, x0, y0, x1, y1, generated);
            }
            return new PdfPageText(index, chars);
        }
        finally
        {
            if (text != IntPtr.Zero) FPDFText_ClosePage(text);
            FPDF_ClosePage(page);
        }
    }

    private static (double X, double Y) ToUnit(IntPtr page, double px, double py)
    {
        FPDF_PageToDevice(page, 0, 0, Scale, Scale, 0, px, py, out int dx, out int dy);
        return (dx / (double)Scale, dy / (double)Scale);
    }
}
