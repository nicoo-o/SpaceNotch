using NotchFlow.Platform.Windows.Display;
using Xunit;

namespace NotchFlow.Core.Tests;

public class DpiHelperTests
{
    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.25, 125)]
    [InlineData(100, 1.5, 150)]
    [InlineData(100, 2.0, 200)]
    [InlineData(180, 1.5, 270)]
    public void DpiHelper_ToPhysicalPixels_CalculatesCorrectly(double dips, double scale, int expectedPhysical)
    {
        int physical = DpiHelper.ToPhysicalPixels(dips, scale);
        Assert.Equal(expectedPhysical, physical);
    }
}
