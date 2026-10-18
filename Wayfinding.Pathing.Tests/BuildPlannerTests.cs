using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;
using static Wayfinding.Pathing.Tests.SearchTestSupport;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-28 (WF-14): build moves under the user's policy, the carry-blocks rule and the block budget.</summary>
public class BuildPlannerTests
{
    private static readonly ClimbCostModel Model = new();
    private static readonly BuildPolicy On = BuildPolicy.EnabledDefaults;

    // A flat strip at height 1 (feet y = 2) with lava trenches; walls all round.
    private static BlockGridSnapshot Strip(string middle) => FakeGrid.FromHeightmap(
        [new string('#', middle.Length + 2), "#" + middle + "#", new string('#', middle.Length + 2)]);

    // A one-wide lava trench across rows 1..trenchRows, open below it; start west of it, goal east of it.
    private static BlockGridSnapshot Trench(int trenchRows)
    {
        var rows = new List<string> { "#########" };
        for (int i = 0; i < trenchRows; i++)
        {
            rows.Add("#111L111#");
        }

        rows.Add("#1111111#");
        rows.Add("#########");
        return FakeGrid.FromHeightmap(rows.ToArray());
    }

    private static readonly GridPos TrenchStart = new(1, 2, 1);
    private static readonly GridPos TrenchGoal = new(7, 2, 1);

    [Fact]
    public void Gap_GetsBridged_WithinBudget()
    {
        BlockGridSnapshot grid = Strip("111LLL111");
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On, carriedBlocks: 64);

        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.Equal(BuildBudgetStatus.WithinBudget, r.Budget);
        Assert.False(r.Reran);
        BuildSite site = Assert.Single(r.Route.BuildSites);
        Assert.Equal(BuildKind.Bridge, site.Kind);
        Assert.Equal(new GridPos(3, 2, 1), site.From);
        Assert.Equal(new GridPos(7, 2, 1), site.To);
        Assert.Equal([new GridPos(4, 1, 1), new GridPos(5, 1, 1), new GridPos(6, 1, 1)], site.Blocks);
        Assert.Equal(3, r.BlocksNeeded);
        Assert.Equal(0, r.BlocksShort);
        AssertValid(grid, r.Route, Model);
    }

    [Fact]
    public void Cliff_GetsAPillar()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["#######", "#00044#", "#######"]);
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 1, 1), new GridPos(5, 5, 1), On, carriedBlocks: 64);

        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        BuildSite site = Assert.Single(r.Route.BuildSites);
        Assert.Equal(BuildKind.Pillar, site.Kind);
        Assert.Equal(new GridPos(3, 1, 1), site.From);
        Assert.Equal(new GridPos(4, 5, 1), site.To);
        Assert.Equal([new GridPos(3, 1, 1), new GridPos(3, 2, 1), new GridPos(3, 3, 1)], site.Blocks); // climb 4 → 3 blocks
        Assert.Equal(BuildBudgetStatus.WithinBudget, r.Budget);
        Assert.Equal(3, r.BlocksNeeded);
        AssertValid(grid, r.Route, Model);
    }

    [Fact]
    public void RouteOverBudget_IsMarkedOverBudget_WithTheExactCount()
    {
        // Three 4-wide lava trenches in a walled strip: 12 blocks is the only way across, over the default 8.
        BlockGridSnapshot grid = Strip("1LLLL11LLLL11LLLL1");
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(18, 2, 1), On, carriedBlocks: 5);

        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.True(r.Reran);
        Assert.Equal(BuildBudgetStatus.OverBudget, r.Budget);
        Assert.True(r.OverBudget);
        Assert.Equal(12, r.BlocksNeeded);
        Assert.Equal(12, r.Route.BlocksNeeded);
        Assert.Equal(3, r.Route.BuildSites.Count);
        Assert.All(r.Route.BuildSites, s => Assert.Equal((BuildKind.Bridge, 4), (s.Kind, s.Count)));
        Assert.Equal(7, r.BlocksShort);

        // The re-run priced each block at 24 × 4.
        Assert.Equal(17 + 12 * 24 * 4, r.Route.Cost, Eps);
        Assert.True(r.UsedRerun);
        AssertValid(grid, r.Route, Model, buildPenalty: 24 * 4);

        // A smaller budget: one 3-wide bridge against maxBlocks 2 reports exactly 3.
        BudgetedRoute small = BuildPlanner.FindPath(Strip("111LLL111"), new GridPos(1, 2, 1), new GridPos(9, 2, 1), On with { MaxBlocks = 2 }, 64);
        Assert.Equal((BuildBudgetStatus.OverBudget, 3, true), (small.Budget, small.BlocksNeeded, small.Reran));

        // Exactly at the budget is within it, with no re-run.
        BudgetedRoute exact = BuildPlanner.FindPath(Strip("111LLL111"), new GridPos(1, 2, 1), new GridPos(9, 2, 1), On with { MaxBlocks = 3 }, 64);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 3, false), (exact.Budget, exact.BlocksNeeded, exact.Reran));
    }

    [Fact]
    public void OverBudgetRoute_IsReplannedOnce_AtFourTimesThePenalty()
    {
        // The trench is 20 rows long: going round adds ~36 blocks of walking. At 24 per block the 1-block bridge
        // wins; with maxBlocks 0 the re-run prices it at 96 and walks round instead.
        BlockGridSnapshot grid = Trench(20);
        double optimalWalk = ReferenceSearch.FindPath(grid, TrenchStart, TrenchGoal).Cost;

        BudgetedRoute first = BuildPlanner.FindPath(grid, TrenchStart, TrenchGoal, On, 64);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 1, false), (first.Budget, first.BlocksNeeded, first.Reran));
        Assert.Equal(6 + 24, first.Route.Cost, Eps);

        BudgetedRoute rerun = BuildPlanner.FindPath(grid, TrenchStart, TrenchGoal, On with { MaxBlocks = 0 }, 64);
        Assert.True(rerun.Reran);
        Assert.Equal(BuildBudgetStatus.NoBuild, rerun.Budget);
        Assert.Equal(0, rerun.BlocksNeeded);
        Assert.Equal(optimalWalk, rerun.Route.Cost, Eps);
        Assert.InRange(rerun.Route.Cost - 6, 24, 96);
        Assert.True(rerun.UsedRerun);
        AssertValid(grid, rerun.Route, Model);

        // A custom factor too small to change the choice leaves it over budget.
        BudgetedRoute stubborn = BuildPlanner.FindPath(grid, TrenchStart, TrenchGoal, On with { MaxBlocks = 0, RerunPenaltyFactor = 1 }, 64);
        Assert.Equal((BuildBudgetStatus.OverBudget, 1, true), (stubborn.Budget, stubborn.BlocksNeeded, stubborn.Reran));
    }

    [Fact]
    public void NoBuildDetour_IsPreferred_WhenShorterThanTheBuildPenalty()
    {
        // A 5-row trench: going round adds ~6.5 blocks, less than one block's 24.
        BlockGridSnapshot grid = Trench(5);
        BudgetedRoute r = BuildPlanner.FindPath(grid, TrenchStart, TrenchGoal, On, 64);

        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.Equal(BuildBudgetStatus.NoBuild, r.Budget);
        Assert.False(r.Reran);
        Assert.Empty(r.Route.BuildSites);
        Assert.Equal(ReferenceSearch.FindPath(grid, TrenchStart, TrenchGoal).Cost, r.Route.Cost, Eps);
        Assert.True(r.Route.Cost - 6 < On.BuildPenalty);

        // A penalty below the detour's extra makes the bridge win.
        BudgetedRoute cheap = BuildPlanner.FindPath(grid, TrenchStart, TrenchGoal, On with { BuildPenalty = 2 }, 64);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 1), (cheap.Budget, cheap.BlocksNeeded));
        Assert.Equal(6 + 2, cheap.Route.Cost, Eps);
    }

    [Fact]
    public void NoBlocksInInventory_MeansNoBuildMoves()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["#######", "#00022#", "#######"]);
        var start = new GridPos(1, 1, 1);
        var top = new GridPos(5, 3, 1);

        BudgetedRoute none = BuildPlanner.FindPath(grid, start, top, On, carriedBlocks: 0);
        Assert.Equal(FineSearchStatus.NoPath, none.Route.Status);
        Assert.Equal(BuildBudgetStatus.NoRoute, none.Budget);
        Assert.False(On.BuildingAllowed(0));

        // Where walking works, the empty inventory just means walking.
        BlockGridSnapshot trench = Trench(20);
        BudgetedRoute walk = BuildPlanner.FindPath(trench, TrenchStart, TrenchGoal, On, carriedBlocks: 0);
        Assert.Equal((FineSearchStatus.Found, 0), (walk.Route.Status, walk.BlocksNeeded));

        // One block carried is enough to plan building (the count is shown, not enforced).
        BudgetedRoute one = BuildPlanner.FindPath(grid, start, top, On, carriedBlocks: 1);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 1, 0), (one.Budget, one.BlocksNeeded, one.BlocksShort));

        // requireBlocksInInventory off: planned even with nothing carried, and the shortfall is reported.
        BudgetedRoute unchecked_ = BuildPlanner.FindPath(grid, start, top, On with { RequireBlocksInInventory = false }, carriedBlocks: 0);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 1, 1), (unchecked_.Budget, unchecked_.BlocksNeeded, unchecked_.BlocksShort));
        Assert.True((On with { RequireBlocksInInventory = false }).BuildingAllowed(0));

        // allowBuild off wins over everything.
        BudgetedRoute off = BuildPlanner.FindPath(grid, start, top, BuildPolicy.Disabled with { RequireBlocksInInventory = false }, carriedBlocks: 64);
        Assert.Equal(FineSearchStatus.NoPath, off.Route.Status);
        Assert.False(BuildPolicy.Disabled.BuildingAllowed(64));
    }

    [Fact]
    public void MaxBridge_LimitsBridgeLength()
    {
        BlockGridSnapshot grid = Strip("111LLL111");
        BudgetedRoute shortBridges = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On with { MaxBridge = 2 }, 64);
        Assert.NotEqual(FineSearchStatus.Found, shortBridges.Route.Status);
        Assert.Equal(0, shortBridges.BlocksNeeded);
    }

    [Fact]
    public void Policy_MapsToTheSearchOptions()
    {
        BuildMoveOptions m = (On with { BuildPenalty = 10, MaxBridge = 3, MaxPillarHeight = 3 }).ToMoveOptions(carriedBlocks: 2, penaltyFactor: 4);
        Assert.True(m.Allowed);
        Assert.Equal(40, m.BuildPenalty, Eps);
        Assert.Equal(3, m.MaxBridgeLength);
        Assert.Equal(3, m.MaxPillarHeight);
        Assert.False(On.ToMoveOptions(carriedBlocks: 0).Allowed);
        Assert.False(BuildPolicy.Disabled.ToMoveOptions(64).Allowed);

        // Design defaults (§5): allowBuild false, requireBlocksInInventory true, buildPenalty 24, maxBlocks 8, maxBridge 4.
        BuildPolicy d = new();
        Assert.Equal((false, true, 24.0, 8, 4, 4.0), (d.AllowBuild, d.RequireBlocksInInventory, d.BuildPenalty, d.MaxBlocks, d.MaxBridge, d.RerunPenaltyFactor));
    }

    [Fact]
    public void InvalidInputs_Throw()
    {
        BlockGridSnapshot grid = Strip("111");
        var a = new GridPos(1, 2, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPlanner.FindPath(grid, a, a, On, carriedBlocks: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPlanner.FindPath(grid, a, a, On with { MaxBlocks = -1 }, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPlanner.FindPath(grid, a, a, On with { RerunPenaltyFactor = 0.5 }, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPlanner.FindPath(grid, a, a, On with { BuildPenalty = double.NaN }, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPlanner.FindPath(grid, a, a, On with { MaxBridge = 17 }, 0));
        Assert.Throws<ArgumentNullException>(() => BuildPlanner.FindPath(grid, a, a, null!, 0));
    }

    [Fact]
    public void SearchOptions_ArePassedThrough_ButTheirBuildSettingIsReplaced()
    {
        BlockGridSnapshot grid = Strip("111LLL111");
        var search = new FineSearchOptions { Build = BuildMoveOptions.Disabled, MaxNodes = 100_000 };
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On, 64, search);
        Assert.Equal(3, r.BlocksNeeded);

        BudgetedRoute tiny = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On, 64, search with { MaxNodes = 1 });
        Assert.Equal(FineSearchStatus.BudgetExhausted, tiny.Route.Status);
    }

    // -------------------------------------------------------------- PR #10 review: cancellation in the re-run

    [Fact]
    public void CancellationDuringTheRerun_ReturnsCancelled_WithNoRoute()
    {
        BlockGridSnapshot grid = Strip("1LLLL11LLLL11LLLL1");
        var start = new GridPos(1, 2, 1);
        var goal = new GridPos(18, 2, 1);

        // Reads taken by the first search and its whole-route check: cancel shortly after.
        var probe = new CountingGrid(grid, long.MaxValue, () => { });
        BudgetedRoute uncancelled = BuildPlanner.FindPath(probe, start, goal, On, 64);
        Assert.True(uncancelled.Reran);
        long firstReads = new CountingGrid(grid, long.MaxValue, () => { }) is var c1
            && BuildPlanner.FindPath(c1, start, goal, On with { MaxBlocks = 64 }, 64) is { } _ ? c1.Reads : 0;
        Assert.InRange(firstReads, 1, probe.Reads - 1);

        using var cts = new CancellationTokenSource();
        var counting = new CountingGrid(grid, firstReads + 5, cts.Cancel);
        BudgetedRoute r = BuildPlanner.FindPath(counting, start, goal, On, 64, null, cts.Token);

        Assert.Equal(FineSearchStatus.Cancelled, r.Route.Status);
        Assert.False(r.Route.HasRoute);
        Assert.Equal(BuildBudgetStatus.NoRoute, r.Budget);
        Assert.Equal(0, r.BlocksNeeded);

        using var pre = new CancellationTokenSource();
        pre.Cancel();
        Assert.Equal(FineSearchStatus.Cancelled, BuildPlanner.FindPath(grid, start, goal, On, 64, null, pre.Token).Route.Status);
    }

    // -------------------------------------------------------------- PR #10 review: carried blocks cap the route

    // A 3-wide lava trench across rows 1..rows, open below: bridging costs 3 blocks, walking round ~2 × rows.
    private static BlockGridSnapshot WideTrench(int rows)
    {
        var lines = new List<string> { "###########" };
        for (int i = 0; i < rows; i++)
        {
            lines.Add("#111LLL111#");
        }

        lines.Add("#111111111#");
        lines.Add("###########");
        return FakeGrid.FromHeightmap(lines.ToArray());
    }

    [Fact]
    public void CarriedBlocks_CapTheRoute_WhenRequired()
    {
        BlockGridSnapshot grid = WideTrench(50);
        var start = new GridPos(1, 2, 1);
        var goal = new GridPos(9, 2, 1);

        // 3 carried: the 3-block bridge is within the cap.
        BudgetedRoute three = BuildPlanner.FindPath(grid, start, goal, On, carriedBlocks: 3);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 3, false, 0), (three.Budget, three.BlocksNeeded, three.Reran, three.BlocksShort));

        // 2 carried: the cap is min(8, 2), so the bridge is re-planned at ×4 and the walk round wins.
        BudgetedRoute two = BuildPlanner.FindPath(grid, start, goal, On, carriedBlocks: 2);
        Assert.Equal((BuildBudgetStatus.NoBuild, 0, true, true), (two.Budget, two.BlocksNeeded, two.Reran, two.UsedRerun));
        Assert.Equal(ReferenceSearch.FindPath(grid, start, goal).Cost, two.Route.Cost, Eps);

        // Rule off: the carried count is informational; the bridge stays and the shortfall is reported.
        BudgetedRoute off = BuildPlanner.FindPath(grid, start, goal, On with { RequireBlocksInInventory = false }, carriedBlocks: 2);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 3, false, 1), (off.Budget, off.BlocksNeeded, off.Reran, off.BlocksShort));

        Assert.Equal(2, On.BlockCap(2));
        Assert.Equal(8, On.BlockCap(64));
        Assert.Equal(8, (On with { RequireBlocksInInventory = false }).BlockCap(0));
    }

    [Fact]
    public void ShortOfCarriedBlocks_WithNoAlternative_IsReturnedFlagged()
    {
        BlockGridSnapshot grid = Strip("111LLL111");
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On, carriedBlocks: 2);

        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.Equal(BuildBudgetStatus.ShortOfBlocks, r.Budget);
        Assert.True(r.Reran);
        Assert.Equal(3, r.BlocksNeeded);
        Assert.Equal(1, r.BlocksShort);
        Assert.False(r.OverBudget);

        // While the rule is on, WithinBudget always means nothing is short.
        foreach (int carried in new[] { 1, 2, 3, 4, 64 })
        {
            BudgetedRoute b = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(9, 2, 1), On, carried);
            if (b.Budget == BuildBudgetStatus.WithinBudget)
            {
                Assert.Equal(0, b.BlocksShort);
            }
        }
    }

    // -------------------------------------------------------------- PR #10 review: whole-route re-check

    /// <summary>Random 24×24 maps with holes, walls, lava and 9-high pillars (the review's probe family).</summary>
    private static string[] HoleMap(int seed)
    {
        var rng = new Random(seed);
        var rows = new char[24][];
        double holes = 0.1 + 0.3 * rng.NextDouble();
        for (int z = 0; z < 24; z++)
        {
            rows[z] = new char[24];
        }

        for (int z = 0; z < 24; z++)
        {
            for (int x = 0; x < 24; x++)
            {
                if (x == 0 || z == 0 || x == 23 || z == 23)
                {
                    rows[z][x] = '#';
                    continue;
                }

                double u = rng.NextDouble();
                rows[z][x] = u < holes ? '0' : u < holes + 0.06 ? '#' : u < holes + 0.12 ? 'L' : u < holes + 0.16 ? '9' : u < holes + 0.22 ? '3' : '1';
            }
        }

        return rows.Select(r => new string(r)).ToArray();
    }

    private static bool Endpoints(IBlockGrid grid, int seed, out GridPos start, out GridPos goal)
    {
        var rng = new Random(seed * 7 + 1);
        start = goal = default;
        try
        {
            start = Feet(grid, rng.Next(1, 23), rng.Next(1, 23));
            goal = Feet(grid, rng.Next(1, 23), rng.Next(1, 23));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Replays the route with every block placed so far (independent of BuildPlanner.FindConflicts).</summary>
    /// <remarks>Placing into a cell an earlier site already filled is a no-op (round 2); planner routes have none.</remarks>
    private static void AssertWalkableWithAllBlocksPlaced(IBlockGrid grid, FineRoute route, bool requireDistinct = true)
    {
        var placed = new List<GridPos>();
        int site = 0;
        for (int i = 1; i < route.Steps.Count; i++)
        {
            RouteStep s = route.Steps[i];
            GridPos from = route.Steps[i - 1].Pos;
            if (site < route.BuildSites.Count && from == route.BuildSites[site].From && s.Kind is MoveKind.Step or MoveKind.Pillar or MoveKind.Bridge)
            {
                if (requireDistinct)
                {
                    Assert.DoesNotContain(route.BuildSites[site].Blocks, placed.Contains);
                }

                placed.AddRange(route.BuildSites[site].Blocks);
                site++;
            }

            var view = new OverlayGrid(grid, placed);
            bool ok = from.X == s.Pos.X && from.Z == s.Pos.Z
                ? Standability.Classify(view, s.Pos).IsOccupiable()
                : Model.Evaluate(view, from, s.Pos).IsOk;
            Assert.True(ok, $"step {i} {from} -> {s.Pos} runs into a placed block");
        }
    }

    [Theory]
    [InlineData(1691, 1.0)]
    [InlineData(515, 0.0)]
    [InlineData(668, 0.0)]
    [InlineData(2486, 0.0)]
    public void RouteThatStepsIntoItsOwnBlock_IsRepaired(int seed, double penalty)
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(HoleMap(seed));
        Assert.True(Endpoints(grid, seed, out GridPos start, out GridPos goal));
        BuildPolicy policy = On with { BuildPenalty = penalty, MaxBlocks = 64 };

        // The raw search really does produce a route that walks into one of its own blocks.
        FineRoute raw = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Build = policy.ToMoveOptions(64) });
        Assert.NotEmpty(BuildPlanner.FindConflicts(grid, raw, Model));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertWalkableWithAllBlocksPlaced(grid, raw, requireDistinct: false)); // steps into an earlier block

        BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64);
        Assert.True(r.Repairs > 0);
        Assert.True(r.Route.HasRoute, $"status {r.Route.Status}");
        Assert.Empty(BuildPlanner.FindConflicts(grid, r.Route, Model));
        AssertWalkableWithAllBlocksPlaced(grid, r.Route);
        AssertValid(grid, r.Route, Model, buildPenalty: penalty, cumulative: true);
    }

    [Fact]
    public void EveryRoute_IsWalkableWithAllItsBlocksPlaced()
    {
        // Seeds 1-4000 (round 2 widened this from 700 to reach 3524, 2597, 1879 and 1135).
        foreach (double penalty in new[] { 0.0, 0.5, 1.0, 24.0 })
        {
            for (int seed = 1; seed <= 4000; seed++)
            {
                BlockGridSnapshot grid = FakeGrid.FromHeightmap(HoleMap(seed));
                if (!Endpoints(grid, seed, out GridPos start, out GridPos goal))
                {
                    continue;
                }

                BuildPolicy policy = On with { BuildPenalty = penalty, MaxBlocks = 64 };
                BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64);
                if (r.Route.Status == FineSearchStatus.NoPath)
                {
                    // A false NoPath (round 2): the raw search, which ignores the route's own blocks, cannot do better.
                    FineRoute raw = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Build = policy.ToMoveOptions(64) });
                    Assert.False(raw.Found, $"seed {seed} penalty {penalty}: NoPath but the raw search found {raw.Steps.Count} steps");
                }

                if (r.Route.HasRoute)
                {
                    AssertWalkableWithAllBlocksPlaced(grid, r.Route);
                    Assert.Equal(r.Route.BuildSites.SelectMany(b => b.Blocks).Count(), r.BlocksNeeded);
                }
            }
        }
    }

    // -------------------------------------------------------------- PR #10 round 2: repeated placements

    /// <summary>
    /// HoleMap seeds whose raw <see cref="FineAStar"/> route places the same cell twice (a later site re-fills a cell an
    /// earlier site already filled) and has no other conflict. Found by sweeping seeds 1-4000; 3524 is Senior's report
    /// (same generator, start (10,1,4), goal (3,1,22), raw 11 placements of 10 distinct cells at penalty 0 and 0.5).
    /// </summary>
    [Theory]
    [InlineData(3524, 0.0, 11, 10)]
    [InlineData(3524, 0.5, 11, 10)]
    [InlineData(3524, 1.0, 9, 8)]
    [InlineData(1357, 0.0, 5, 4)]
    [InlineData(1357, 0.5, 2, 1)]
    [InlineData(2597, 0.0, 8, 7)]
    [InlineData(2597, 0.5, 5, 4)]
    [InlineData(1879, 0.0, 4, 3)]
    [InlineData(1135, 0.0, 4, 3)]
    public void RepeatedPlacement_IsANoOp_NotAConflict(int seed, double penalty, int rawPlacements, int distinct)
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(HoleMap(seed));
        Assert.True(Endpoints(grid, seed, out GridPos start, out GridPos goal));
        BuildPolicy policy = On with { BuildPenalty = penalty, MaxBlocks = 64 };

        FineRoute raw = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Build = policy.ToMoveOptions(64) });
        List<GridPos> placements = raw.BuildSites.SelectMany(b => b.Blocks).ToList();
        Assert.Equal(rawPlacements, placements.Count);
        Assert.Equal(distinct, placements.Distinct().Count());
        Assert.Empty(BuildPlanner.FindConflicts(grid, raw, Model)); // a repeat alone is not a conflict
        AssertWalkableWithAllBlocksPlaced(grid, raw, requireDistinct: false);

        BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64);
        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.Equal(0, r.Repairs);
        Assert.Equal(distinct, r.BlocksNeeded);
        Assert.Equal(distinct, r.Route.BlocksNeeded);
        Assert.Equal(r.Route.Steps.Select(s => s.Pos), raw.Steps.Select(s => s.Pos)); // same path, just fewer blocks
        Assert.Equal(raw.Cost - penalty * (rawPlacements - distinct), r.Route.Cost, 1e-9);
        AssertWalkableWithAllBlocksPlaced(grid, r.Route); // distinct
        AssertValid(grid, r.Route, Model, buildPenalty: penalty, cumulative: true);
    }

    /// <summary>
    /// Hand-built 9x9 lava map whose only crossing re-fills a cell an earlier bridge already filled: with that cell
    /// forbidden there is no complete route at all, so treating the repeat as a conflict (forbid it, re-plan) can only
    /// fail. Start (1,2,1), goal (7,2,7).
    /// </summary>
    private static readonly string[] OnlyCrossingRepeats =
    [
        "#########",
        "#1#1L1LL#",
        "#1LL1#LL#",
        "##LL#LL1#",
        "#L1111LL#",
        "#LLLLLL1#",
        "#L111111#",
        "#111#111#",
        "#########",
    ];

    [Fact]
    public void OnlyCrossingThatRepeatsAPlacement_IsFound()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(OnlyCrossingRepeats);
        GridPos start = new(1, 2, 1), goal = new(7, 2, 7), repeated = new(3, 1, 2);
        BuildPolicy policy = On with { BuildPenalty = 0, MaxBlocks = 64 };

        FineRoute raw = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Build = policy.ToMoveOptions(64) });
        List<GridPos> placements = raw.BuildSites.SelectMany(b => b.Blocks).ToList();
        Assert.Equal(2, placements.Count(c => c == repeated));
        Assert.Equal(placements.Count - 1, placements.Distinct().Count());

        // Forbidding the repeated cell (what round 1 did) leaves no complete route.
        BlockGridSnapshotBuilder b = FakeGrid.HeightmapBuilder(OnlyCrossingRepeats);
        b.AddFlags(repeated.X, repeated.Y, repeated.Z, CellFlags.BuildForbidden);
        Assert.False(FineAStar.FindPath(b.Build(), start, goal, new FineSearchOptions { Build = policy.ToMoveOptions(64) }).Found);

        BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64);
        Assert.Equal(FineSearchStatus.Found, r.Route.Status);
        Assert.Equal(0, r.Repairs);
        Assert.Equal(placements.Count - 1, r.BlocksNeeded);
        Assert.Single(r.Route.BuildSites.SelectMany(s => s.Blocks), c => c == repeated);
        AssertWalkableWithAllBlocksPlaced(grid, r.Route);
        AssertValid(grid, r.Route, Model, buildPenalty: 0, cumulative: true);
    }

    // -------------------------------------------------------------- PR #10 round 2: budget status after a conflict

    /// <summary>
    /// A node cap that runs out during (or right at the start of) a conflict re-plan is BudgetExhausted, never NoPath;
    /// any partial route still follows the usual rules (no conflicts, valid moves). The caps equal to the first
    /// search's expansions leave the re-plan nothing, so the planner holds only a conflicting route (round 1 said
    /// NoPath there). The 50-node caps are Senior's case: the re-plan starts and runs out part-way.
    /// </summary>
    [Theory]
    [InlineData(1691, 1.0, 22, 1)]
    [InlineData(1691, 0.5, 17, 1)]
    [InlineData(1691, 0.0, 8, 1)]
    [InlineData(2486, 0.0, 11, 1)]
    [InlineData(515, 0.0, 93, 1)]
    [InlineData(668, 0.0, 265, 1)]
    [InlineData(1691, 1.0, 50, 1)]
    [InlineData(1691, 0.5, 50, 1)]
    [InlineData(1357, 0.5, 50, 0)]
    public void NodeBudgetAfterConflict_IsBudgetExhausted(int seed, double penalty, int maxNodes, int repairs)
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(HoleMap(seed));
        Assert.True(Endpoints(grid, seed, out GridPos start, out GridPos goal));
        BuildPolicy policy = On with { BuildPenalty = penalty, MaxBlocks = 64 };

        BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64, new FineSearchOptions { MaxNodes = maxNodes });
        Assert.Equal(FineSearchStatus.BudgetExhausted, r.Route.Status);
        Assert.Equal(repairs, r.Repairs);
        Assert.InRange(r.Expanded, 1, maxNodes);
        AssertPartialFollowsTheRules(grid, r, penalty);
    }

    /// <summary>
    /// Same for a time budget, on an injected clock (each read advances it by <paramref name="tickMs"/>), so the result
    /// does not depend on the machine: 5 ms runs out after the first search found a conflicting route.
    /// </summary>
    [Theory]
    [InlineData(515, 0.0, 1.0, 1)]
    [InlineData(1691, 1.0, 2.0, 1)]
    [InlineData(1691, 0.5, 2.0, 1)]
    [InlineData(2486, 0.0, 2.0, 1)]
    [InlineData(668, 0.0, 0.5, 1)]
    [InlineData(1357, 0.5, 2.0, 0)]
    public void TimeBudgetAfterConflict_IsTimedOut(int seed, double penalty, double tickMs, int repairs)
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(HoleMap(seed));
        Assert.True(Endpoints(grid, seed, out GridPos start, out GridPos goal));
        BuildPolicy policy = On with { BuildPenalty = penalty, MaxBlocks = 64 };
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(tickMs));

        BudgetedRoute r = BuildPlanner.FindPath(grid, start, goal, policy, 64, new FineSearchOptions { MaxTime = TimeSpan.FromMilliseconds(5), TimeProvider = clock });
        Assert.Equal(FineSearchStatus.TimedOut, r.Route.Status);
        Assert.Equal(repairs, r.Repairs);
        AssertPartialFollowsTheRules(grid, r, penalty);
    }

    private static void AssertPartialFollowsTheRules(IBlockGrid grid, BudgetedRoute r, double penalty)
    {
        if (!r.Route.HasRoute)
        {
            return;
        }

        Assert.Empty(BuildPlanner.FindConflicts(grid, r.Route, Model));
        AssertWalkableWithAllBlocksPlaced(grid, r.Route);
        AssertValid(grid, r.Route, Model, buildPenalty: penalty, cumulative: true);
    }

    // -------------------------------------------------------------- PR #10 review: other tests

    [Fact]
    public void BuildForbidden_IsHonouredThroughThePlanner()
    {
        BlockGridSnapshotBuilder b = FakeGrid.HeightmapBuilder(["#######", "#00022#", "#######"]);
        b.AddFlags(3, 1, 1, CellFlags.BuildForbidden);
        BudgetedRoute r = BuildPlanner.FindPath(b.Build(), new GridPos(1, 1, 1), new GridPos(5, 3, 1), On, 64);
        Assert.Equal(FineSearchStatus.NoPath, r.Route.Status);
        Assert.Equal(BuildBudgetStatus.NoRoute, r.Budget);
    }

    [Fact]
    public void OverBudgetPartialRoute_SharesOneNodeBudget()
    {
        BlockGridSnapshot grid = Strip("1LLLL11LLLL11LLLL1");
        var search = new FineSearchOptions { MaxNodes = 4 };
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 2, 1), new GridPos(18, 2, 1), On with { MaxBlocks = 3 }, 64, search);

        Assert.InRange(r.Expanded, 1, 4); // first search, re-run and any repairs together
        Assert.Equal(FineSearchStatus.BudgetExhausted, r.Route.Status);
        Assert.True(r.Route.HasRoute);
        Assert.Equal(r.Route.BlocksNeeded, r.BlocksNeeded);
        BuildBudgetStatus expected = r.BlocksNeeded == 0 ? BuildBudgetStatus.NoBuild : r.BlocksNeeded <= 3 ? BuildBudgetStatus.WithinBudget : BuildBudgetStatus.OverBudget;
        Assert.Equal(expected, r.Budget);
    }

    [Fact]
    public void PillarCounts_FollowMaxClimb()
    {
        // MaxClimb 2: a climb of 4 takes 4 − 2 = 2 blocks.
        var model = new ClimbCostModel(ClimbCostOptions.Default with { MaxClimb = 2 });
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["#######", "#00044#", "#######"]);
        var search = new FineSearchOptions { Model = model };
        BudgetedRoute r = BuildPlanner.FindPath(grid, new GridPos(1, 1, 1), new GridPos(5, 5, 1), On, 64, search);
        Assert.Equal((BuildBudgetStatus.WithinBudget, 2), (r.Budget, r.BlocksNeeded));

        BudgetedRoute over = BuildPlanner.FindPath(grid, new GridPos(1, 1, 1), new GridPos(5, 5, 1), On with { MaxBlocks = 1 }, 64, search);
        Assert.Equal((BuildBudgetStatus.OverBudget, 2, true), (over.Budget, over.BlocksNeeded, over.Reran));
    }
}
