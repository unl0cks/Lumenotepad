using Avalonia;
using Lumenotepad.Platform;
using Xunit;

namespace Lumenotepad.Tests;

public class MaximizeInsetTests
{

    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void CleanMaximize_HasNoInset()
    {

        var inset = WindowGeometry.MaximizeInset(Work, new PixelPoint(0, 0), new Size(1920, 1040), 1);
        Assert.Equal(new Thickness(0), inset);
    }

    [Fact]
    public void SnapMaximize_InsetsTheSevenPixelOverhang()
    {

        var inset = WindowGeometry.MaximizeInset(Work, new PixelPoint(-7, -7), new Size(1934, 1054), 1);
        Assert.Equal(new Thickness(7, 7, 7, 7), inset);
    }

    [Fact]
    public void SnapMaximize_ScalesTheInsetBackToLogicalUnits()
    {

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

        var work = new PixelRect(-1920, 0, 1920, 1040);
        var inset = WindowGeometry.MaximizeInset(work, new PixelPoint(-1927, -7), new Size(1934, 1054), 1);
        Assert.Equal(new Thickness(7, 7, 7, 7), inset);
    }

    [Fact]
    public void ClientSizeAlreadyPhysical_ProducesAGrotesqueInset()
    {

        var work = new PixelRect(0, 0, 3420, 2224);
        var inset = WindowGeometry.MaximizeInset(work, new PixelPoint(0, 0), new Size(3420, 2224), 2);
        Assert.True(inset.Right > 1000, $"expected an absurd inset, got {inset.Right}");
        Assert.True(inset.Bottom > 1000, $"expected an absurd inset, got {inset.Bottom}");
    }
}
