using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class ClaimBoxTests
{
    [Fact]
    public void Contains_is_half_open_and_order_independent()
    {
        ClaimBox box = ClaimBox.FromCorners(10, 20, 30, 0, 5, 40);
        Assert.Equal(0, box.MinX);
        Assert.Equal(10, box.MaxX);
        Assert.True(box.Contains(0, 5, 30));
        Assert.False(box.Contains(10, 5, 30));
        Assert.False(box.Contains(0, 20, 30));
        Assert.True(box.ContainsXZ(9, 39));
        Assert.False(box.ContainsXZ(9, 40));
    }
}
