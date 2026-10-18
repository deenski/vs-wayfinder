using System.Diagnostics;
using Wayfinding.Pathing.Coarse;
using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;
using static Wayfinding.Pathing.Tests.SearchTestSupport;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-27 (WF-13): the 8×8 coarse planner and its stitching with the fine A*.</summary>
public class CoarsePlannerTests
{
    private static readonly ClimbCostModel Model = new();

    /// <summary>
    /// Smooth rolling terrain (neighbouring columns differ by at most 1), with lakes and lava pits, and an optional
    /// Unknown band. Feet Y = height + 1, like <see cref="FakeGrid.FromHeightmap"/>.
    /// </summary>
    private static HeightGridSnapshot Rolling(int sizeX, int sizeZ, int seed = 1, (int From, int To)? unknownBandX = null, bool ring = false)
    {
        var rng = new Random(seed);
        var b = new HeightGridBuilder(0, 0, sizeX, sizeZ);
        double p1 = rng.NextDouble() * 10, p2 = rng.NextDouble() * 10;
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                double hv = 6 + 4 * Math.Sin(x / 23.0 + p1) + 3 * Math.Cos(z / 17.0 + p2) + 2 * Math.Sin((x + z) / 41.0);
                int h = (int)Math.Round(hv);
                ColumnKind kind = ColumnKind.Land;
                double lake = Math.Sin(x / 53.0 + p2) * Math.Cos(z / 11.0 + p1);
                if (lake > 0.85)
                {
                    kind = ColumnKind.Water;
                }
                else if (lake < -0.95)
                {
                    kind = ColumnKind.Lava;
                }

                if (ring && (x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1))
                {
                    kind = ColumnKind.Blocked;
                }

                if (unknownBandX is { } band && x >= band.From && x < band.To)
                {
                    kind = ColumnKind.Unknown;
                }

                b.Set(x, z, kind, h + 1);
            }
        }

        return b.Build();
    }

    // -------------------------------------------------------------- WF-13 AC

    [Fact]
    public void TwoThousandBlockRoute_PlansWellUnder250ms()
    {
        HeightGridSnapshot heights = Rolling(2048, 96, seed: 7);
        GridPos start = Surface(heights, 8, 48);
        GridPos goal = Surface(heights, 2008, 48);
        CoarseRoute r = CoarsePlanner.Plan(heights, start, goal);
        Assert.Equal(CoarseStatus.Found, r.Status);

        TimeSpan best = TimeSpan.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var timer = Stopwatch.StartNew();
            r = CoarsePlanner.Plan(heights, start, goal);
            best = timer.Elapsed < best ? timer.Elapsed : best;
        }

        Console.WriteLine($"2000-block coarse route: {best.TotalMilliseconds:F1} ms, {r.Expanded} nodes, {r.CellsBuilt} cells, {r.Waypoints.Count} waypoints");
        Assert.Equal(CoarseStatus.Found, r.Status);
        Assert.Equal(goal, r.Waypoints[^1]);
        Assert.True(GridPos.HorizontalDistance(start, goal) >= 2000);
        Assert.True(best.TotalMilliseconds < 250, $"took {best.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void UnrevealedBandInTheMiddle_BecomesExactlyOneStraightUnknownSegment()
    {
        HeightGridSnapshot heights = Rolling(400, 64, seed: 3, unknownBandX: (180, 220));
        GridPos start = Surface(heights, 10, 32);
        GridPos goal = Surface(heights, 380, 32);

        CoarseRoute r = CoarsePlanner.Plan(heights, start, goal);

        Assert.Equal(CoarseStatus.UnknownBoundary, r.Status);
        CoarseSegment unknown = Assert.Single(r.Segments, s => s.Kind == SegmentKind.Unknown);
        Assert.Equal(r.Segments[^1], unknown);
        Assert.Equal(goal, unknown.To);
        Assert.Equal(r.Waypoints[^1], unknown.From);
        Assert.InRange(unknown.From.X, 160, 179); // at the band's near edge
        Assert.All(r.Waypoints, w => Assert.True(w.X < 180, $"planned into the band at {w}"));
    }

    // -------------------------------------------------------------- corridor quality

    [Fact]
    public void LongRoute_FollowsTheTerrain_AndIsDeterministic()
    {
        HeightGridSnapshot heights = Rolling(600, 64, seed: 11);
        GridPos start = Surface(heights, 4, 10), goal = Surface(heights, 590, 50);
        CoarseRoute a = CoarsePlanner.Plan(heights, start, goal);
        CoarseRoute b = CoarsePlanner.Plan(heights, start, goal);

        Assert.Equal(CoarseStatus.Found, a.Status);
        Assert.Equal(a.Waypoints, b.Waypoints);
        Assert.All(a.Waypoints.Skip(1).SkipLast(1), w => Assert.Equal(ColumnKind.Land, heights.GetColumn(w.X, w.Z).Kind));
        for (int i = 1; i < a.Waypoints.Count; i++)
        {
            Assert.True(GridPos.HorizontalDistance(a.Waypoints[i - 1], a.Waypoints[i]) <= 2 * CoarsePlanner.CellSize * Math.Sqrt(2));
        }
    }

    [Fact]
    public void Wall_IsDetouredThroughItsGap()
    {
        // A wall across x = 100 with a gap at z = 56..60.
        string[] rows = Enumerable.Range(0, 64).Select(z => new string('0', 100) + (z is >= 56 and <= 60 ? "0" : "#") + new string('0', 99)).ToArray();
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 200, 64, 0, 8);
        var start = new GridPos(10, 1, 10);
        var goal = new GridPos(190, 1, 10);

        CoarseRoute coarse = CoarsePlanner.Plan(heights, start, goal);
        Assert.Equal(CoarseStatus.Found, coarse.Status);
        Assert.Contains(coarse.Waypoints, w => Math.Abs(w.X - 100) <= 8 && w.Z >= 50);

        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 199, 63), heights, start, goal,
            new HierarchicalOptions { DirectWhenGoalInWindow = false });
        ReferenceSearchResult oracle = ReferenceSearch.FindPath(fine, start, goal);
        Assert.True(h.RefinedToGoal);
        Assert.True(h.FineCost >= oracle.Cost - Eps);
        Console.WriteLine($"wall detour: refined {h.FineCost:F1} vs optimal {oracle.Cost:F1}");
        Assert.True(h.FineCost <= oracle.Cost * 1.25 + 10, $"refined {h.FineCost:F1} vs optimal {oracle.Cost:F1}");
        AssertValid(fine, new FineRoute(FineSearchStatus.Found, h.FineCost, h.FineSteps, h.BuildSites, null, 0, 0), Model);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void CorridorPlusRefine_StaysWithinABoundOfTheOptimum(int seed)
    {
        string[] rows = RandomHeightmap(seed, 96, 96, closed: true, features: 0.06);
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 96, 96, 0, 10);
        GridPos start = NearestFeet(fine, 3, 3);
        GridPos goal = NearestFeet(fine, 92, 90);

        ReferenceSearchResult oracle = ReferenceSearch.FindPath(fine, start, goal);
        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 95, 95), heights, start, goal,
            new HierarchicalOptions { LegLength = 24, DirectWhenGoalInWindow = false });

        if (!oracle.Found)
        {
            Assert.False(h.RefinedToGoal);
            return;
        }

        Assert.True(h.RefinedToGoal, $"status {h.Status}, tail {h.CoarseTail.Count}");
        Assert.True(h.FineCost >= oracle.Cost - Eps);
        Console.WriteLine($"seed {seed}: refined {h.FineCost:F1} vs optimal {oracle.Cost:F1} ({h.FineCost / oracle.Cost:P0})");
        Assert.True(h.FineCost <= oracle.Cost * 1.25 + 10, $"refined {h.FineCost:F1} vs optimal {oracle.Cost:F1}");
        AssertValid(fine, new FineRoute(FineSearchStatus.Found, h.FineCost, h.FineSteps, h.BuildSites, null, 0, 0), Model);
    }

    [Fact]
    public void HillIsAvoided_WhenTheDetourIsCheaperThanTheClimb()
    {
        // A 6-high round hill on flat land straight between start and goal.
        var b = new HeightGridBuilder(0, 0, 120, 80);
        for (int z = 0; z < 80; z++)
        {
            for (int x = 0; x < 120; x++)
            {
                double d = Math.Sqrt(Sq(x - 60) + Sq(z - 40));
                int h = d < 6 ? 6 : d < 12 ? (int)Math.Round(12 - d) : 0;
                b.Set(x, z, ColumnKind.Land, h + 1);
            }
        }

        HeightGridSnapshot heights = b.Build();
        CoarseRoute r = CoarsePlanner.Plan(heights, new GridPos(5, 1, 40), new GridPos(115, 1, 40));
        Assert.Equal(CoarseStatus.Found, r.Status);
        Assert.True(r.Waypoints.Max(w => w.Y) <= 2, "the detour may graze the foot of the hill, never climb it");

        var flat = new ClimbCostModel(new ClimbCostOptions { UphillPenalty = 0, DownhillPenalty = 0 });
        CoarseRoute straight = CoarsePlanner.Plan(heights, new GridPos(5, 1, 40), new GridPos(115, 1, 40), new CoarsePlannerOptions { Model = flat });
        Assert.Contains(straight.Waypoints, w => w.Y >= 5);

        static double Sq(double v) => v * v;
    }

    [Fact]
    public void Water_IsCrossedOnlyWhenThereIsNoCheaperWay_AndMarkedSwim()
    {
        var b = new HeightGridBuilder(0, 0, 80, 40);
        for (int z = 0; z < 40; z++)
        {
            for (int x = 0; x < 80; x++)
            {
                bool river = x is >= 40 and < 48; // exactly one column of 8x8 cells
                b.Set(x, z, river ? ColumnKind.Water : ColumnKind.Land, 1);
            }
        }

        CoarseRoute r = CoarsePlanner.Plan(b.Build(), new GridPos(4, 1, 20), new GridPos(76, 1, 20));
        Assert.Equal(CoarseStatus.Found, r.Status);
        Assert.Contains(r.Segments, s => s.Kind == SegmentKind.Swim);
        Assert.True(r.Cost > 72 + 4 * 8 * 0.9, $"cost {r.Cost:F1} should include the water multiplier");
    }

    [Fact]
    public void Lava_AndCliffs_AreNeverCrossed()
    {
        var b = new HeightGridBuilder(0, 0, 64, 32);
        for (int z = 0; z < 32; z++)
        {
            for (int x = 0; x < 64; x++)
            {
                ColumnKind k = x is >= 20 and < 24 && z < 28 ? ColumnKind.Lava : ColumnKind.Land;
                int feet = x >= 40 && z < 28 ? 12 : 1; // a 11-block cliff, except near z = 28..31
                if (x >= 40 && z >= 28)
                {
                    feet = 1 + Math.Min(11, x - 40);  // a ramp
                }

                b.Set(x, z, k, feet);
            }
        }

        HeightGridSnapshot heights = b.Build();
        CoarseRoute r = CoarsePlanner.Plan(heights, new GridPos(2, 1, 2), new GridPos(60, 12, 2));
        Assert.Equal(CoarseStatus.Found, r.Status);
        Assert.All(r.Waypoints, w => Assert.NotEqual(ColumnKind.Lava, heights.GetColumn(w.X, w.Z).Kind));
        Assert.Contains(r.Waypoints, w => w.Z >= 24 && w.X is >= 16 and < 28); // round the lava's open end
        Assert.Contains(r.Waypoints, w => w.Z >= 24 && w.X >= 40);           // up the ramp
    }

    // -------------------------------------------------------------- unreachable and invalid

    [Fact]
    public void Unreachable_IsNoPath_InAClosedWorld_AndUnknownBoundaryInAnOpenOne()
    {
        string[] rows = Enumerable.Range(0, 32).Select(_ => new string('0', 30) + "#" + new string('0', 33)).ToArray();
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot open = HeightGrid.FromBlockGrid(fine, 0, 0, 64, 32, 0, 4);
        var start = new GridPos(5, 1, 5);
        var goal = new GridPos(60, 1, 5);
        Assert.Equal(CoarseStatus.UnknownBoundary, CoarsePlanner.Plan(open, start, goal).Status); // the map edge is unrevealed

        var b = new HeightGridBuilder(-1, -1, 66, 34);
        for (int z = -1; z <= 32; z++)
        {
            for (int x = -1; x <= 64; x++)
            {
                HeightColumn c = open.GetColumn(x, z);
                b.Set(x, z, c.Kind == ColumnKind.Unknown ? ColumnKind.Blocked : c.Kind, c.FeetY);
            }
        }

        CoarseRoute closed = CoarsePlanner.Plan(b.Build(), start, goal);
        Assert.Equal(CoarseStatus.NoPath, closed.Status);
        Assert.Empty(closed.Waypoints);
    }

    [Fact]
    public void InvalidEndpoints_AndBudget()
    {
        var b = new HeightGridBuilder(0, 0, 200, 16);
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 200; x++)
            {
                b.Set(x, z, x == 50 && z == 5 ? ColumnKind.Lava : ColumnKind.Land, 1);
            }
        }

        HeightGridSnapshot h = b.Build();
        Assert.Equal(CoarseStatus.InvalidStart, CoarsePlanner.Plan(h, new GridPos(-5, 1, 0), new GridPos(10, 1, 0)).Status);
        Assert.Equal(CoarseStatus.InvalidGoal, CoarsePlanner.Plan(h, new GridPos(1, 1, 1), new GridPos(50, 1, 5)).Status);

        CoarseRoute budget = CoarsePlanner.Plan(h, new GridPos(1, 1, 1), new GridPos(198, 1, 8), new CoarsePlannerOptions { MaxNodes = 5 });
        Assert.Equal(CoarseStatus.BudgetExhausted, budget.Status);
        Assert.Equal(5, budget.Expanded);
        Assert.Equal(new GridPos(198, 1, 8), budget.Unknown!.Value.To);
        Assert.Single(budget.Segments, s => s.Kind == SegmentKind.Unknown);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(CoarseStatus.Cancelled, CoarsePlanner.Plan(h, new GridPos(1, 1, 1), new GridPos(198, 1, 8), null, cts.Token).Status);
    }

    // -------------------------------------------------------------- stitching with the fine window

    [Fact]
    public void Stitching_RefinesInsideTheWindow_AndKeepsTheCoarseTailBeyondIt()
    {
        string[] rows = RandomHeightmap(21, 300, 48, closed: true, features: 0.04);
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 300, 48, 0, 10);
        GridPos start = NearestFeet(fine, 3, 24);
        GridPos goal = NearestFeet(fine, 296, 24);
        FineWindow window = FineWindow.Around(start, 48);

        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, window, heights, start, goal);

        Assert.Equal(CoarseStatus.Found, h.Status);
        Assert.False(h.RefinedToGoal);
        Assert.NotNull(h.StitchPoint);
        Assert.True(window.Contains(h.StitchPoint!.Value));
        Assert.True(h.StitchPoint.Value.X > start.X + 24, "refinement should get well into the window");
        Assert.Contains(h.StitchPoint.Value, h.Coarse!.Waypoints);
        Assert.Equal(goal, h.CoarseTail[^1]);
        Assert.All(h.FineSteps, s => Assert.True(window.Contains(s.Pos)));
        Assert.Null(h.Unknown);
        AssertValid(fine, new FineRoute(FineSearchStatus.Found, h.FineCost, h.FineSteps, h.BuildSites, null, 0, 0), Model);
        Assert.Equal(h.FineSteps.Count - 1 + h.CoarseTail.Count, h.Segments.Count);
    }

    [Fact]
    public void Stitching_GoalInsideTheWindow_UsesTheFineSearchAlone()
    {
        BlockGridSnapshot fine = FakeGrid.Flat(40, 40);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 40, 40, 0, 4);
        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 39, 39), heights, new GridPos(1, 1, 1), new GridPos(38, 1, 30));
        Assert.True(h.RefinedToGoal);
        Assert.Null(h.Coarse);
        Assert.Equal(FineAStar.FindPath(fine, new GridPos(1, 1, 1), new GridPos(38, 1, 30)).Cost, h.FineCost, Eps);
    }

    [Fact]
    public void Stitching_KeepsTheSingleUnknownTail()
    {
        string[] rows = Enumerable.Repeat(new string('0', 60) + new string('?', 20) + new string('0', 40), 24).ToArray();
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 120, 24, 0, 4);
        var start = new GridPos(2, 1, 12);
        var goal = new GridPos(115, 1, 12);

        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 119, 23), heights, start, goal);

        Assert.Equal(CoarseStatus.UnknownBoundary, h.Status);
        CoarseSegment unknown = Assert.Single(h.Segments, s => s.Kind == SegmentKind.Unknown);
        Assert.Equal(goal, unknown.To);
        Assert.True(unknown.From.X < 60);
        Assert.Empty(h.CoarseTail);
    }

    // -------------------------------------------------------------- height grid

    [Fact]
    public void HeightGrid_FromBlockGrid_ClassifiesColumns()
    {
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(["02~L#?"]);
        HeightGridSnapshot h = HeightGrid.FromBlockGrid(fine, 0, 0, 6, 1, 0, 6);
        Assert.Equal(new HeightColumn(ColumnKind.Land, 1), h.GetColumn(0, 0));
        Assert.Equal(new HeightColumn(ColumnKind.Land, 3), h.GetColumn(1, 0));
        Assert.Equal(new HeightColumn(ColumnKind.Water, 1), h.GetColumn(2, 0));
        Assert.Equal(ColumnKind.Lava, h.GetColumn(3, 0).Kind);
        Assert.Equal(ColumnKind.Blocked, h.GetColumn(4, 0).Kind);
        Assert.Equal(ColumnKind.Unknown, h.GetColumn(5, 0).Kind);
        Assert.Equal(ColumnKind.Unknown, h.GetColumn(-1, 0).Kind);
    }

    private static GridPos NearestFeet(IBlockGrid grid, int x, int z)
    {
        for (int r = 0; r < 8; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int y = 10; y >= 1; y--)
                    {
                        var p = new GridPos(x + dx, y, z + dz);
                        if (Standability.Classify(grid, p) == StandKind.Ground)
                        {
                            return p;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException($"No ground near ({x}, {z}).");
    }

    private static GridPos Surface(IHeightGrid h, int x, int z)
    {
        HeightColumn c = h.GetColumn(x, z);
        for (int dz = 0; !c.IsPassable && dz < 32; dz++)
        {
            c = h.GetColumn(x, z + dz);
            if (c.IsPassable)
            {
                return new GridPos(x, c.FeetY, z + dz);
            }
        }

        Assert.True(c.IsPassable, $"no passable column near ({x}, {z})");
        return new GridPos(x, c.FeetY, z);
    }
}
