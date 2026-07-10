using Lumenotepad.Views;
using Xunit;

namespace Lumenotepad.Tests;

public class MotionTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.875)]   // 1-(1-0.5)^3
    public void EaseOut_matches_cubic(double t, double expected)
        => Assert.Equal(expected, Motion.EaseOut(t), 3);

    [Fact]
    public void Lerp_interpolates_endpoints()
    {
        Assert.Equal(10, Motion.Lerp(10, 20, 0), 3);
        Assert.Equal(20, Motion.Lerp(10, 20, 1), 3);
        Assert.Equal(15, Motion.Lerp(10, 20, 0.5), 3);
    }

    [Fact]
    public void Steps_never_zero()   // a 1ms animation still runs at least one frame
        => Assert.True(Motion.Steps(1) >= 1);

    [Fact]
    public void Ms_ScalesWithSpeedScale()
    {
        var old = Motion.SpeedScale;
        try
        {
            Motion.SpeedScale = 1.4;
            Assert.Equal(308, Motion.Ms(220));
            Motion.SpeedScale = 0.6;
            Assert.Equal(132, Motion.Ms(220));
            Motion.SpeedScale = 1.0;
            Assert.Equal(220, Motion.Ms(220));
        }
        finally { Motion.SpeedScale = old; }
    }
}
