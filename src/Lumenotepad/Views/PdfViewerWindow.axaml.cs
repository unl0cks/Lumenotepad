using Avalonia.Controls;
using Avalonia.Input;
using Lumenotepad.Platform;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>A window that hosts a <see cref="PdfViewer"/> — the popup used when a PDF ATTACHMENT is
/// opened. PDF PAGES embed the same PdfViewer inline in the canvas instead.</summary>
public partial class PdfViewerWindow : Window
{
    public PdfViewerWindow(string pdfPath, bool doubleClickCreate = false)
    {
        InitializeComponent();
        PdfTitle.Text = System.IO.Path.GetFileName(pdfPath);
        CloseBtn.Click += (_, _) => Close();
        PdfTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        Opened += (_, _) =>
        {
            WinChrome.RoundCorners(this, true);
            ThemeManager.ApplyChildChrome(this);
            if (Content is Control root) Motion.ScaleIn(root, 0.96, 180);
            Viewer.Load(pdfPath, doubleClickCreate);
        };
        bool closing = false;
        Closing += (_, e) =>
        {
            Viewer.Flush();
            if (closing) return;
            e.Cancel = true; closing = true;
            if (Content is Control root) Motion.CollapseOut(root, 140, Close);
            else Close();
        };
    }
}
