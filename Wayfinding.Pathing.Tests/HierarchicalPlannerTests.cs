using Wayfinding.Pathing.Coarse;
using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;
using static Wayfinding.Pathing.Tests.SearchTestSupport;

namespace Wayfinding.Pathing.Tests;

/// <summary>PR #9 review fixes: cancellation, one budget per plan, window confinement, tail styling, validation.</summary>
public class HierarchicalPlannerTests
{
    private static readonly ClimbCostModel Model = new();

    // The review's probe terrain: 300×48 random, window around the start, goal far beyond it.
    private static (BlockGridSnapshot Fine, HeightGridSnapshot Heights, GridPos Start, GridPos Goal, FineWindow Window) LongRoute()
    {
        BlockGridSnapshot fine = FakeGrid.FromHeightmap(RandomHeightmap(21, 300, 48, closed: true, features: 0.04));
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 300, 48, 0, 10);
        GridPos start = Ground(fine, 3, 24);
        GridPos goal = Ground(fine, 296, 24);
        return (fine, heights, start, goal, FineWindow.Around(start, 48));
    }

    // A flat 120×60 field. The fine grid has a wall at x = 40 for z 0..49 (a gap at z 50..58, outside the window
    // z 0..40); the height grid does not know about the wall.
    private static (BlockGridSnapshot Fine, HeightGridSnapshot Heights) MissedWall(bool gap = true)
    {
        var rows = new string[60];
        for (int z = 0; z < 60; z++)
        {
            char[] row = new string('0', 120).ToCharArray();
            row[0] = row[119] = '#';
            if (z is 0 or 59 || (z < 50 || !gap))
            {
                row[40] = '#';
            }

            if (z is 0 or 59)
            {
                Array.Fill(row, '#');
            }

            rows[z] = new string(row);
        }

        BlockGridSnapshot fine = FakeGrid.FromHeightmap(rows);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(FakeGrid.FromHeightmap(
            rows.Select((r, z) => z is 0 or 59 ? r : "#" + new string('0', 118) + "#").ToArray()), 0, 0, 120, 60, 0, 6);
        return (fine, heights);
    }

    // -------------------------------------------------------------- must-fix 1: cancellation

    [Theory]
    [InlineData(50)]
    [InlineData(2_000)]
    [InlineData(20_000)]
    public void CancellationDuringRefinement_ReturnsCancelled_WithNoRoute(long afterReads)
    {
        var (fine, heights, start, goal, window) = LongRoute();
        HierarchicalRoute full = HierarchicalPlanner.Plan(fine, window, heights, start, goal);
        Assert.Equal(CoarseStatus.Found, full.Status);
        Assert.True(full.GridReads > afterReads, $"the plan reads only {full.GridReads} cells");

        using var cts = new CancellationTokenSource();
        var counting = new CountingGrid(fine, afterReads, cts.Cancel);
        HierarchicalRoute h = HierarchicalPlanner.Plan(counting, window, heights, start, goal, null, cts.Token);

        Assert.Equal(CoarseStatus.Cancelled, h.Status);
        Assert.False(h.HasRoute);
        Assert.Empty(h.FineSteps);
        Assert.Empty(h.CoarseTail);
        Assert.Null(h.Unknown);
        Assert.Empty(h.Segments);
    }

    [Fact]
    public void CancellationDuringTheDirectSearch_ReturnsCancelled()
    {
        BlockGridSnapshot fine = FakeGrid.Flat(60, 60);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 60, 60, 0, 4);
        using var cts = new CancellationTokenSource();
        var counting = new CountingGrid(fine, 100, cts.Cancel);
        HierarchicalRoute h = HierarchicalPlanner.Plan(counting, new FineWindow(0, 0, 59, 59), heights, new GridPos(1, 1, 1), new GridPos(58, 1, 58), null, cts.Token);
        Assert.Equal(CoarseStatus.Cancelled, h.Status);
        Assert.False(h.HasRoute);

        using var pre = new CancellationTokenSource();
        pre.Cancel();
        Assert.Equal(CoarseStatus.Cancelled, HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 59, 59), heights, new GridPos(1, 1, 1), new GridPos(58, 1, 58), null, pre.Token).Status);
    }

    // -------------------------------------------------------------- must-fix 2: one budget per plan

    [Fact]
    public void MissedWall_IsBoundedByTheGridReadBudget()
    {
        var (fine, heights) = MissedWall(gap: false);
        var window = new FineWindow(0, 0, 119, 59);
        var start = new GridPos(5, 1, 20);
        var goal = new GridPos(80, 1, 20);

        foreach (long maxReads in new long[] { 20_000, 200_000 })
        {
            HierarchicalRoute h = HierarchicalPlanner.Plan(fine, window, heights, start, goal, new HierarchicalOptions { MaxGridReads = maxReads });
            Assert.Equal(CoarseStatus.BudgetExhausted, h.Status);
            // One expansion reads at most a few hundred cells past the limit before the search notices.
            Assert.InRange(h.GridReads, maxReads, maxReads + 2_000);
            Assert.False(h.RefinedToGoal);
        }

        // Without a binding read budget the node budget caps the whole plan instead.
        HierarchicalRoute nodes = HierarchicalPlanner.Plan(fine, window, heights, start, goal, new HierarchicalOptions { MaxFineNodes = 3_000 });
        Assert.Equal(CoarseStatus.BudgetExhausted, nodes.Status);
        Assert.InRange(nodes.FineExpanded, 1, 3_000);
    }

    [Fact]
    public void PlanTimeBudget_IsShared_AndEndsTimedOut()
    {
        var (fine, heights, start, goal, window) = LongRoute();
        // Every clock read advances 1 ms. The coarse step reads the clock every 16 expansions (231 here), so 30 ms
        // lets it finish and then runs out inside the fine refinement.
        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, window, heights, start, goal,
            new HierarchicalOptions { MaxTime = TimeSpan.FromMilliseconds(30), TimeProvider = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)), DirectWhenGoalInWindow = false });

        Assert.Equal(CoarseStatus.TimedOut, h.Status);
        Assert.Equal(CoarseStatus.Found, h.Coarse!.Status);
        Assert.True(h.FineExpanded > 0);
        // The stitched prefix (possibly just the start) plus the coarse tail to the goal.
        Assert.Equal(goal, h.CoarseTail[^1]);
        AssertValid(fine, new FineRoute(FineSearchStatus.Found, h.FineCost, h.FineSteps, h.BuildSites, null, 0, 0), Model);

        // 8 ms runs out inside the coarse step: its partial corridor and Unknown tail are returned, nothing refined.
        HierarchicalRoute early = HierarchicalPlanner.Plan(fine, window, heights, start, goal,
            new HierarchicalOptions { MaxTime = TimeSpan.FromMilliseconds(8), TimeProvider = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)), DirectWhenGoalInWindow = false });
        Assert.Equal(CoarseStatus.TimedOut, early.Status);
        Assert.Equal(CoarseStatus.TimedOut, early.Coarse!.Status);
        Assert.Equal(0, early.FineExpanded);
        Assert.Equal(goal, early.Unknown!.Value.To);

        // A zero budget stops before any fine search.
        HierarchicalRoute zero = HierarchicalPlanner.Plan(fine, window, heights, start, goal,
            new HierarchicalOptions { MaxTime = TimeSpan.Zero, TimeProvider = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)) });
        Assert.Equal(CoarseStatus.TimedOut, zero.Status);
        Assert.Equal(0, zero.FineExpanded);
    }

    [Fact]
    public void CoarsePlanner_HasATimeBudget()
    {
        var b = new HeightGridBuilder(0, 0, 400, 16);
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 400; x++)
            {
                b.Set(x, z, ColumnKind.Land, 1);
            }
        }

        HeightGridSnapshot h = b.Build();
        var options = new CoarsePlannerOptions { MaxTime = TimeSpan.FromMilliseconds(5), TimeProvider = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)) };
        CoarseRoute r = CoarsePlanner.Plan(h, new GridPos(1, 1, 1), new GridPos(398, 1, 8), options);

        Assert.Equal(CoarseStatus.TimedOut, r.Status);
        Assert.Equal(new GridPos(398, 1, 8), r.Unknown!.Value.To);
        Assert.Single(r.Segments, s => s.Kind == SegmentKind.Unknown);
        Assert.True(r.Waypoints[^1].X > 1);
    }

    [Fact]
    public void CoarseBudgetPartial_EndsAtTheOpenNodeClosestToTheGoal_AndProgressesWithTheBudget()
    {
        var b = new HeightGridBuilder(0, 0, 400, 16);
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 400; x++)
            {
                b.Set(x, z, ColumnKind.Land, 1);
            }
        }

        HeightGridSnapshot h = b.Build();
        var goal = new GridPos(398, 1, 8);
        int previous = -1;
        foreach (int budget in new[] { 1, 3, 10, 30 })
        {
            CoarseRoute r = CoarsePlanner.Plan(h, new GridPos(1, 1, 1), goal, new CoarsePlannerOptions { MaxNodes = budget });
            Assert.Equal(CoarseStatus.BudgetExhausted, r.Status);
            int endX = r.Waypoints[^1].X;
            Assert.True(endX > previous, $"budget {budget}: ended at x {endX}, previous {previous}");
            previous = endX;
            // Deterministic.
            Assert.Equal(r.Waypoints, CoarsePlanner.Plan(h, new GridPos(1, 1, 1), goal, new CoarsePlannerOptions { MaxNodes = budget }).Waypoints);
        }

        // With one expansion the start is expanded and the end is its open neighbour nearest the goal.
        CoarseRoute one = CoarsePlanner.Plan(h, new GridPos(1, 1, 1), goal, new CoarsePlannerOptions { MaxNodes = 1 });
        Assert.Equal(2, one.Waypoints.Count);
        Assert.Equal(1, one.Waypoints[^1].X >> 3);
    }

    // -------------------------------------------------------------- window confinement and snapping

    [Fact]
    public void FallbackLegs_StayInsideTheWindow()
    {
        // The only gap in the fine wall is outside the window, so no refinement may use it.
        var (fine, heights) = MissedWall(gap: true);
        var window = new FineWindow(0, 0, 119, 40);
        var start = new GridPos(5, 1, 20);
        var goal = new GridPos(80, 1, 20);

        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, window, heights, start, goal);

        Assert.All(h.FineSteps, s => Assert.True(window.Contains(s.Pos), $"step {s.Pos} left the window"));
        Assert.False(h.RefinedToGoal);
        Assert.NotEqual(CoarseStatus.Cancelled, h.Status);

        // The same fine grid with a window that includes the gap refines to the goal through it.
        HierarchicalRoute wide = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 119, 59), heights, start, goal);
        Assert.True(wide.RefinedToGoal, $"status {wide.Status}");
        Assert.Contains(wide.FineSteps, s => s.Pos.X == 40 && s.Pos.Z >= 50);
    }

    [Fact]
    public void RepresentativesThatAreNotOccupiable_AreSnapped()
    {
        // A height grid that reports every surface two blocks too high (leaves, say): no waypoint is occupiable.
        var (fine, heights, start, goal, window) = LongRoute();
        var b = new HeightGridBuilder(0, 0, 300, 48);
        for (int z = 0; z < 48; z++)
        {
            for (int x = 0; x < 300; x++)
            {
                HeightColumn c = heights.GetColumn(x, z);
                b.Set(x, z, c.Kind, c.IsPassable ? c.FeetY + 2 : c.FeetY);
            }
        }

        HeightGridSnapshot high = b.Build();
        var options = new HierarchicalOptions { DirectWhenGoalInWindow = false };
        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, window, high, start, goal, options);
        Assert.Equal(CoarseStatus.Found, h.Status);
        Assert.True(h.StitchPoint!.Value.X > start.X + 24, $"stitched at {h.StitchPoint}");
        AssertValid(fine, new FineRoute(FineSearchStatus.Found, h.FineCost, h.FineSteps, h.BuildSites, null, 0, 0), Model);

        // Without snapping nothing is refined.
        HierarchicalRoute none = HierarchicalPlanner.Plan(fine, window, high, start, goal, options with { SnapRadius = 0 });
        Assert.Single(none.FineSteps);
    }

    // -------------------------------------------------------------- tail styling and cost

    [Fact]
    public void CoarseTail_KeepsSwimKinds_AndTheCoarseCost()
    {
        var b = new HeightGridBuilder(0, 0, 200, 16);
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 200; x++)
            {
                b.Set(x, z, x is >= 80 and < 120 ? ColumnKind.Water : ColumnKind.Land, 2);
            }
        }

        HeightGridSnapshot heights = b.Build();
        BlockGridSnapshot fine = FakeGrid.Flat(200, 16);
        var start = new GridPos(1, 1, 8);
        var goal = new GridPos(198, 2, 8);

        // A window that holds only the start: nothing is refined and the whole corridor is the tail.
        HierarchicalRoute h = HierarchicalPlanner.Plan(fine, new FineWindow(0, 0, 3, 15), heights, start, goal);
        Assert.Equal(CoarseStatus.Found, h.Status);
        Assert.Equal(h.Coarse!.Waypoints.Skip(1), h.CoarseTail);
        Assert.Equal(h.Coarse.WaypointKinds.Skip(1), h.CoarseTailKinds);
        Assert.Contains(SegmentKind.Swim, h.CoarseTailKinds);
        Assert.Equal(h.Coarse.Cost, h.CoarseTailCost, Eps);
        Assert.Contains(h.Segments, s => s.Kind == SegmentKind.Swim);
        Assert.Equal(h.Coarse.Segments.Select(s => s.Kind), h.Segments.Select(s => s.Kind));
    }

    // -------------------------------------------------------------- validation

    [Fact]
    public void Options_AreValidated()
    {
        BlockGridSnapshot fine = FakeGrid.Flat(20, 20);
        HeightGridSnapshot heights = HeightGrid.FromBlockGrid(fine, 0, 0, 20, 20, 0, 4);
        var window = new FineWindow(0, 0, 19, 19);
        var a = new GridPos(1, 1, 1);
        var g = new GridPos(18, 1, 18);
        HierarchicalRoute Plan(HierarchicalOptions o) => HierarchicalPlanner.Plan(fine, window, heights, a, g, o);

        var otherModel = new ClimbCostModel(ClimbCostOptions.Default with { MaxClimb = 2 });
        Assert.Throws<ArgumentException>(() => Plan(new HierarchicalOptions { Coarse = new CoarsePlannerOptions { Model = otherModel } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { CorridorMargin = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { LegLength = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { MaxFineNodes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { MaxGridReads = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { SnapRadius = 9 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { MaxTime = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new HierarchicalOptions { Coarse = new CoarsePlannerOptions { MaxNodes = 0 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoarsePlanner.Plan(heights, a, g, new CoarsePlannerOptions { MaxTime = TimeSpan.FromSeconds(-1) }));

        // Both models matching (with a non-default value) is fine.
        Assert.True(Plan(new HierarchicalOptions { Fine = new FineSearchOptions { Model = otherModel }, Coarse = new CoarsePlannerOptions { Model = otherModel } }).RefinedToGoal);
    }

    // -------------------------------------------------------------- height grid nit

    [Fact]
    public void HeightGrid_UnloadedSupport_IsUnknown_AndHazardIsBlocked()
    {
        // x = 0 ground; x = 1 air over an Unknown support; x = 2 a hazard (not lava) on solid.
        BlockGridSnapshot fine = FakeGrid.FromLayers([["#?#"], ["..!"], ["..."], ["..."], ["..."]]);
        HeightGridSnapshot h = HeightGrid.FromBlockGrid(fine, 0, 0, 3, 1, 1, 2);
        Assert.Equal(new HeightColumn(ColumnKind.Land, 1), h.GetColumn(0, 0));
        Assert.Equal(ColumnKind.Unknown, h.GetColumn(1, 0).Kind);
        Assert.Equal(ColumnKind.Blocked, h.GetColumn(2, 0).Kind);

        // A start next to that column is a frontier, so an unreachable goal past it is UnknownBoundary, not NoPath.
        CoarseRoute r = CoarsePlanner.Plan(h, new GridPos(0, 1, 0), new GridPos(50, 1, 0));
        Assert.Equal(CoarseStatus.UnknownBoundary, r.Status);
    }

    private static GridPos Ground(IBlockGrid grid, int x, int z)
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
}
