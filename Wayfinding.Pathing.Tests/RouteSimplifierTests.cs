using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-26 (WF-12): Douglas–Peucker that keeps segment kinds and build sites.</summary>
public class RouteSimplifierTests
{
    [Fact]
    public void StraightWalk_CollapsesToItsEnds()
    {
        FineRoute r = FineAStar.FindPath(FakeGrid.Flat(20, 3), new GridPos(0, 1, 1), new GridPos(19, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r);
        Assert.Equal([new RoutePoint(new GridPos(0, 1, 1), SegmentKind.Walk), new RoutePoint(new GridPos(19, 1, 1), SegmentKind.Walk)], points);
    }

    [Fact]
    public void KindChanges_AreKept()
    {
        BlockGridSnapshot moat = FakeGrid.FromHeightmap(["#########", "#000~000#", "#########"]);
        FineRoute r = FineAStar.FindPath(moat, new GridPos(1, 1, 1), new GridPos(7, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r);

        Assert.Equal(
            [
                new RoutePoint(new GridPos(1, 1, 1), SegmentKind.Walk),
                new RoutePoint(new GridPos(3, 1, 1), SegmentKind.Walk),
                new RoutePoint(new GridPos(4, 1, 1), SegmentKind.Swim), // into the water
                new RoutePoint(new GridPos(7, 1, 1), SegmentKind.Walk), // out of it and on; (5,1,1) is collinear
            ],
            points);
    }

    [Fact]
    public void BuildSegments_KeepEveryPoint()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["############", "#333LLLL333#", "############"]);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 4, 1), new GridPos(10, 4, 1),
            new FineSearchOptions { Build = BuildMoveOptions.EnabledDefaults });
        Assert.Equal(BuildKind.Bridge, Assert.Single(r.BuildSites).Kind);

        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r);
        GridPos[] bridged = r.Steps.Where(s => s.Kind == MoveKind.Bridge).Select(s => s.Pos).ToArray();
        Assert.Equal(5, bridged.Length); // 4 gap cells and the landing
        Assert.All(bridged, p => Assert.Contains(points, q => q.Pos == p && q.Segment == SegmentKind.Build));
    }

    [Fact]
    public void UnknownTail_IsOneStraightSegmentToTheGoal()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(Enumerable.Repeat("0000000???00000", 3).ToArray());
        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 1), new GridPos(14, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r);

        Assert.Equal(new RoutePoint(new GridPos(14, 1, 1), SegmentKind.Unknown), points[^1]);
        Assert.Equal(new GridPos(6, 1, 1), points[^2].Pos);
        Assert.Single(points, p => p.Segment == SegmentKind.Unknown);
    }

    [Fact]
    public void DroppedPoints_StayWithinTolerance()
    {
        string[] rows = SearchTestSupport.RandomHeightmap(5, 40, 40, closed: true);
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        GridPos start = SearchTestSupport.Feet(grid, 2, 2);
        GridPos goal = SearchTestSupport.Feet(grid, 37, 36);
        FineRoute r = FineAStar.FindPath(grid, start, goal);
        Assert.True(r.Found);

        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, tolerance: 1.0);
        Assert.True(points.Count < r.Steps.Count);
        Assert.Equal(start, points[0].Pos);
        Assert.Equal(goal, points[^1].Pos);
    }

    [Fact]
    public void EmptyRoute_IsEmpty() =>
        Assert.Empty(RouteSimplifier.Simplify(FineAStar.FindPath(FakeGrid.Flat(2, 2), new GridPos(0, 5, 0), new GridPos(1, 1, 1))));
}
