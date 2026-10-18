using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;

namespace Wayfinding.Pathing.Tests;

public class StraightRouteTests
{
    [Fact]
    public void Straight_line_is_one_unknown_segment()
    {
        var from = new GridPos(1, 64, 3);
        var to = new GridPos(90, 70, 40);
        var route = new FineRoute(
            FineSearchStatus.UnknownBoundary,
            double.PositiveInfinity,
            [],
            [],
            new UnknownSegment(from, to),
            0,
            0);

        Assert.NotNull(route.Unknown);
        Assert.Equal(from, route.Unknown.Value.From);
        Assert.Equal(to, route.Unknown.Value.To);
        Assert.Empty(route.Steps);
        Assert.Empty(route.Path);
        Assert.Equal(0, route.BlocksNeeded);
        Assert.Equal(0, route.TotalClimb);
    }
}
