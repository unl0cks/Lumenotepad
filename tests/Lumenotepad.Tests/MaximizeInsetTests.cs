using Avalonia;
using Lumenotepad.Platform;
using Xunit;

namespace Lumenotepad.Tests;

/// <summary>Pins the Win32 Aero-Snap overhang compensation. Its caller is deliberately Windows-gated —
/// running it on macOS produced an inset hundreds of points deep and squeezed the whole UI into the
/// top-left corner of a zoomed window — so these tests exist to keep the Windows numbers correct while
/// that gate stays in place, and to document what the unit mismatch off Windows actually does.</summary>
public class MaximizeInsetTests
{
    // A 1920x1080 screen with a 40px taskbar, no DPI scaling.
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void CleanMaximize_HasNoInset()
    {
        // Button-maximize lands exactly on the work area: nothing overhangs.
        var inset = WindowGeometry.MaximizeInset(Work, new PixelPoint(0, 0), new Size(1920, 1040), 1);
        Assert.Equal(new Thickness(0), inset);
    }

    [Fact]
    public void SnapMaximize_InsetsTheSevenPixelOverhang()
    {
        // Aero Snap parks a WS_THICKFRAME window at (-7,-7) and oversizes it by 14 per axis.
        var inset = WindowGeometry.MaximizeInset(Work, new PixelPoint(-7, -7), new Size(1934, 1054), 1);
        Assert.Equal(new Thickness(7, 7, 7, 7), inset);
    }

    [Fact]
    public void SnapMaximize_ScalesTheInsetBackToLogicalUnits()
    {
        // Same overhang on a 200% display: 14 physical px of overhang is 7 logical.
        var work = new PixelRect(0, 0, 3840, 2080);
        var inset = WindowGeometry.MaximizeInset(work, new PixelPoint(-14, -14), new Size(1934, 1054), 2);
        Assert.Equal(new Thickness(7, 7, 7, 7), inset);
    }

    [Fact]
    public void OverhangOnOneEdgeOnly_LeavesTheOthersAtZero()
    {
        var inset = WindowGeometry.MaximizeInset(Work, new PixelPoint(0, 0), new Size(1920, 1060), 1);
        Assert.Equal(new Thickness(0, 0, 0, 20), inset);
    }

    [Fact]
    public void SecondMonitorAtNegativeOrigin_MeasuresAgainstThatScreen()
    {
        // Work areas are virtual-desktop coordinates, so a left-hand monitor has a negative X.
        var work = new PixelRect(-1920, 0, 1920, 1040);
        var inset = WindowGeometry.MaximizeInset(work, new PixelPoint(-1927, -7), new Size(1934, 1054), 1);
        Assert.Equal(new Thickness(7, 7, 7, 7), inset);
    }

    [Fact]
    public void ClientSizeAlreadyPhysical_ProducesAGrotesqueInset()
    {
        // WHY the caller is Windows-only. This is the macOS shape of the bug: the window genuinely fills
        // a 2x screen, but ClientSize does not mean what this maths assumes, so the computed overhang is
        // most of the window and the content gets crushed into a corner. No assertion on the exact value
        // beyond "absurd" — the point is that it is nowhere near zero.
        var work = new PixelRect(0, 0, 3420, 2224);
        var inset = WindowGeometry.MaximizeInset(work, new PixelPoint(0, 0), new Size(3420, 2224), 2);
        Assert.True(inset.Right > 1000, $"expected an absurd inset, got {inset.Right}");
        Assert.True(inset.Bottom > 1000, $"expected an absurd inset, got {inset.Bottom}");
    }
}
