using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-26 (WF-12): grid-aware Douglas–Peucker that keeps segment kinds, build sites, climbs and walkability.</summary>
public class RouteSimplifierTests
{
    private static readonly ClimbCostModel Model = new();

    [Fact]
    public void StraightWalk_CollapsesToItsEnds()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(20, 3);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 1), new GridPos(19, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid);
        Assert.Equal([new RoutePoint(new GridPos(0, 1, 1), SegmentKind.Walk), new RoutePoint(new GridPos(19, 1, 1), SegmentKind.Walk)], points);
    }

    [Fact]
    public void KindChanges_AreKept()
    {
        BlockGridSnapshot moat = FakeGrid.FromHeightmap(["#########", "#000~000#", "#########"]);
        FineRoute r = FineAStar.FindPath(moat, new GridPos(1, 1, 1), new GridPos(7, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, moat);

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

        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid);
        GridPos[] bridged = r.Steps.Where(s => s.Kind == MoveKind.Bridge).Select(s => s.Pos).ToArray();
        Assert.Equal(5, bridged.Length); // 4 gap cells and the landing
        Assert.All(bridged, p => Assert.Contains(points, q => q.Pos == p && q.Segment == SegmentKind.Build));
    }

    [Fact]
    public void UnknownTail_IsOneStraightSegmentToTheGoal()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(Enumerable.Repeat("0000000???00000", 3).ToArray());
        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 1), new GridPos(14, 1, 1));
        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid);

        Assert.Equal(new RoutePoint(new GridPos(14, 1, 1), SegmentKind.Unknown), points[^1]);
        Assert.Equal(new GridPos(6, 1, 1), points[^2].Pos);
        Assert.Single(points, p => p.Segment == SegmentKind.Unknown);
    }

    [Fact]
    public void OneBlockClimb_IsNeverFlattened()
    {
        // Review case: the route (1,1,1)…(5,1,1),(6,2,1)…(10,2,1) used to collapse to (1,1,1)->(10,2,1).
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["############", "#0000011111#", "############"]);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(10, 2, 1));
        Assert.True(r.Found);

        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid, Model, tolerance: 1.0);

        Assert.Equal(
            [new GridPos(1, 1, 1), new GridPos(5, 1, 1), new GridPos(6, 2, 1), new GridPos(10, 2, 1)],
            points.Select(p => p.Pos));
    }

    [Fact]
    public void Shortcut_AcrossAWallCorner_IsRefused()
    {
        // An L-shaped corridor: plain Douglas–Peucker at tolerance 1.0 cut the inside corner through the wall.
        string[] rows =
        [
            "#######",
            "#00000#",
            "####00#",
            "####00#",
            "####00#",
            "#######",
        ];
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(4, 1, 4));
        Assert.True(r.Found);

        IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid, Model, tolerance: 1.5);
        AssertWalkable(grid, r, points);
        Assert.True(points.Count >= 3, "the corner must stay");
    }

    [Fact]
    public void DroppedPoints_StayWithinTolerance()
    {
        foreach (double tolerance in new[] { 0.5, 1.0, 2.0 })
        {
            string[] rows = SearchTestSupport.RandomHeightmap(5, 40, 40, closed: true);
            BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
            GridPos start = SearchTestSupport.Feet(grid, 2, 2);
            GridPos goal = SearchTestSupport.Feet(grid, 37, 36);
            FineRoute r = FineAStar.FindPath(grid, start, goal);
            Assert.True(r.Found);

            IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid, Model, tolerance);
            Assert.True(points.Count < r.Steps.Count);
            Assert.Equal(start, points[0].Pos);
            Assert.Equal(goal, points[^1].Pos);
            AssertDroppedWithinTolerance(r.Path, points, tolerance);
        }
    }

    /// <summary>
    /// Property test (PR #6 review, must-fix 2): random maps with walls, water and lava, tolerances up to 2. No
    /// simplified segment crosses Solid, Lava, Hazard or Unknown, every Y change survives, and every dropped point
    /// is within the tolerance. The walkability check here samples the swept body independently of the
    /// simplifier's own slab test.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void RandomMaps_SimplifiedRoutesStayWalkable_AndKeepEveryClimb(double tolerance)
    {
        int routes = 0;
        for (int seed = 1; seed <= 151; seed++)
        {
            string[] rows = SearchTestSupport.RandomHeightmap(seed, 24, 24, closed: true, features: 0.18);
            BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
            GridPos[] cells = SearchTestSupport.StandableCells(grid, rows);
            var rng = new Random(seed * 31);
            for (int pair = 0; pair < 3; pair++)
            {
                GridPos start = cells[rng.Next(cells.Length)];
                GridPos goal = cells[rng.Next(cells.Length)];
                FineRoute r = FineAStar.FindPath(grid, start, goal);
                if (!r.Found)
                {
                    continue;
                }

                routes++;
                IReadOnlyList<RoutePoint> points = RouteSimplifier.Simplify(r, grid, Model, tolerance);
                AssertWalkable(grid, r, points);
                AssertDroppedWithinTolerance(r.Path, points, tolerance);
            }
        }

        Assert.True(routes > 200, $"only {routes} routes were found; the property test needs more coverage");
    }

    [Fact]
    public void Simplify_ValidatesItsArguments()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(3, 3);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 0), new GridPos(2, 1, 2));
        Assert.Throws<ArgumentNullException>(() => RouteSimplifier.Simplify(r, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteSimplifier.Simplify(r, grid, tolerance: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteSimplifier.Simplify(r, grid, tolerance: double.NaN));
    }

    [Fact]
    public void EmptyRoute_IsEmpty()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(2, 2);
        Assert.Empty(RouteSimplifier.Simplify(FineAStar.FindPath(grid, new GridPos(0, 5, 0), new GridPos(1, 1, 1)), grid));
    }

    /// <summary>
    /// Every simplified Known segment is either one search move (neighbouring route points) or a same-Y chord
    /// whose swept body (half-width 0.3, sampled every 0.02 blocks) only touches columns that are occupiable at
    /// that Y. Every route point next to a Y change is kept. Build points are all kept.
    /// </summary>
    private static void AssertWalkable(IBlockGrid grid, FineRoute r, IReadOnlyList<RoutePoint> points)
    {
        IReadOnlyList<GridPos> path = r.Path;
        var index = new Dictionary<GridPos, int>();
        for (int i = 0; i < path.Count; i++)
        {
            index.TryAdd(path[i], i);
        }

        var kept = new HashSet<GridPos>(points.Select(p => p.Pos));
        for (int i = 1; i < path.Count; i++)
        {
            if (path[i].Y != path[i - 1].Y)
            {
                Assert.True(kept.Contains(path[i - 1]) && kept.Contains(path[i]), $"climb/drop {path[i - 1]} -> {path[i]} was dropped");
            }

            if (r.Steps[i].Segment == SegmentKind.Build)
            {
                Assert.Contains(path[i], kept);
            }
        }

        for (int k = 1; k < points.Count; k++)
        {
            RoutePoint a = points[k - 1], b = points[k];
            if (b.Segment is not (SegmentKind.Walk or SegmentKind.Swim))
            {
                continue;
            }

            int ia = index[a.Pos], ib = index[b.Pos];
            if (ib - ia == 1)
            {
                continue; // a validated search move
            }

            Assert.True(a.Pos.Y == b.Pos.Y, $"multi-step segment {a.Pos} -> {b.Pos} changes Y");
            var touched = new HashSet<(int, int)>();
            double ax = a.Pos.X + 0.5, az = a.Pos.Z + 0.5, bx = b.Pos.X + 0.5, bz = b.Pos.Z + 0.5;
            int samples = (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) / 0.02);
            for (int s = 0; s <= samples; s++)
            {
                double t = (double)s / samples;
                double cx = ax + t * (bx - ax), cz = az + t * (bz - az);
                foreach (double ox in new[] { -0.3, 0, 0.3 })
                {
                    foreach (double oz in new[] { -0.3, 0, 0.3 })
                    {
                        touched.Add(((int)Math.Floor(cx + ox), (int)Math.Floor(cz + oz)));
                    }
                }
            }

            foreach ((int x, int z) in touched)
            {
                var feet = new GridPos(x, a.Pos.Y, z);
                if (feet == a.Pos || feet == b.Pos)
                {
                    continue;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    CellClass c = grid.GetClass(x, a.Pos.Y + dy, z);
                    Assert.False(c is CellClass.Lava or CellClass.Hazard or CellClass.Unknown, $"{a.Pos} -> {b.Pos} touches {c} at ({x}, {a.Pos.Y + dy}, {z})");
                    if (dy >= 0)
                    {
                        Assert.False(c == CellClass.Solid, $"{a.Pos} -> {b.Pos} passes through Solid at ({x}, {a.Pos.Y + dy}, {z})");
                    }
                }

                StandKind stand = Standability.Classify(grid, feet);
                Assert.True(stand == StandKind.Ground || (b.Segment == SegmentKind.Swim && stand == StandKind.Swim),
                    $"{a.Pos} -> {b.Pos} crosses ({x}, {z}), which is {stand} at Y {a.Pos.Y}");
            }
        }
    }

    /// <summary>Every route point between two kept points is within the tolerance of their chord.</summary>
    private static void AssertDroppedWithinTolerance(IReadOnlyList<GridPos> path, IReadOnlyList<RoutePoint> points, double tolerance)
    {
        int k = 0;
        int lastKept = 0;
        Assert.Equal(path[0], points[0].Pos);
        for (int i = 1; i < path.Count; i++)
        {
            if (k + 1 < points.Count && path[i] == points[k + 1].Pos)
            {
                for (int j = lastKept + 1; j < i; j++)
                {
                    double d = DistanceToSegment(path[j], path[lastKept], path[i]);
                    Assert.True(d <= tolerance + 1e-9, $"dropped {path[j]} is {d:F3} from {path[lastKept]} -> {path[i]} (tolerance {tolerance})");
                }

                k++;
                lastKept = i;
            }
        }

        Assert.Equal(path.Count - 1, lastKept); // the last planned point is always kept
    }

    private static double DistanceToSegment(GridPos p, GridPos a, GridPos b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
        double apx = p.X - a.X, apy = p.Y - a.Y, apz = p.Z - a.Z;
        double len2 = abx * abx + aby * aby + abz * abz;
        double t = len2 == 0 ? 0 : Math.Clamp((apx * abx + apy * aby + apz * abz) / len2, 0, 1);
        double dx = apx - t * abx, dy = apy - t * aby, dz = apz - t * abz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
