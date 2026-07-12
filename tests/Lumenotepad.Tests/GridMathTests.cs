using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class GridMathTests
{
    // No exact midpoints (10, 30, …) — Math.Round uses banker's rounding there and the
    // pointer never delivers a perfect .5 anyway.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(11, 20)]
    [InlineData(20, 20)]
    [InlineData(29, 20)]
    [InlineData(31, 40)]
    [InlineData(347, 340)]
    public void Snap_landsOnNearestCell(double input, double expected) =>
        Assert.Equal(expected, GridMath.Snap(input));
}
