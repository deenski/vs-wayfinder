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
        AssertValid(grid, r.Route, Model);

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
        Assert.Equal(BuildBudgetStatus.NoBuild, none.Budget);
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
}
