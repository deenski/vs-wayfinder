using System.Diagnostics;
using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;
using static Wayfinding.Pathing.Tests.SearchTestSupport;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-26 (WF-12): the fine A*, checked against <see cref="ReferenceSearch"/> as the oracle.</summary>
public class FineAStarTests
{
    private static readonly ClimbCostModel Model = new();

    private static FineSearchOptions WithBuild(BuildMoveOptions? build = null, ClimbCostModel? model = null) =>
        new() { Model = model ?? Model, Build = build ?? BuildMoveOptions.EnabledDefaults };

    private static GridPos[] Standable(IBlockGrid grid, string[] rows)
    {
        var result = new List<GridPos>();
        for (int z = 0; z < rows.Length; z++)
        {
            for (int x = 0; x < rows[z].Length; x++)
            {
                if (FakeGrid.TryParseHeight(rows[z][x], out int h) || rows[z][x] == '~')
                {
                    var p = new GridPos(x, rows[z][x] == '~' ? 1 : h + 1, z);
                    if (Standability.Classify(grid, p).IsOccupiable())
                    {
                        result.Add(p);
                    }
                }
            }
        }

        return result.ToArray();
    }

    // ---------------------------------------------------------------- optimality parity with the oracle

    public static TheoryData<int, double, double> ParitySeeds()
    {
        var data = new TheoryData<int, double, double>();
        for (int seed = 1; seed <= 24; seed++)
        {
            data.Add(seed, seed % 3 == 0 ? 0 : 4, seed % 4 == 0 ? 0 : 1); // also uphill 0 / downhill 0 variants
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ParitySeeds))]
    public void RandomTerrain_SameCostAsReferenceSearch(int seed, double uphill, double downhill)
    {
        var model = new ClimbCostModel(new ClimbCostOptions { UphillPenalty = uphill, DownhillPenalty = downhill });
        string[] rows = RandomHeightmap(seed, 28, 28, closed: seed % 2 == 0, features: 0.12);
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        GridPos[] cells = Standable(grid, rows);
        var rng = new Random(seed * 7919);
        for (int pair = 0; pair < 6; pair++)
        {
            GridPos start = cells[rng.Next(cells.Length)];
            GridPos goal = cells[rng.Next(cells.Length)];

            ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, model);
            FineRoute route = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = model });

            if (oracle.Found)
            {
                Assert.Equal(FineSearchStatus.Found, route.Status);
                Assert.Equal(oracle.Cost, route.Cost, Eps);
                Assert.Equal(goal, route.Steps[^1].Pos);
                AssertValid(grid, route, model);
                Assert.True(route.Expanded <= oracle.Expanded + 1, $"A* expanded {route.Expanded}, Dijkstra {oracle.Expanded}");
            }
            else
            {
                Assert.Equal(ReferenceSearchStatus.NoPath, oracle.Status);
                Assert.Contains(route.Status, new[] { FineSearchStatus.NoPath, FineSearchStatus.UnknownBoundary });
            }
        }
    }

    public static TheoryData<string, string[], int, int, int, int> NamedGrids() => new()
    {
        { "ridge-short (detour)", Ridge(6), 1, 0, 19, 0 },
        { "ridge-long (cross)", Ridge(14), 1, 0, 19, 0 },
        { "lake", ["#########", "#0000000#", "#0~~~~~0#", "#0~~~~~0#", "#0~~~~~0#", "#0000000#", "#########"], 1, 3, 7, 3 },
        { "maze", ["###########", "#0#000#000#", "#0#0#0#0#0#", "#000#000#0#", "#########0#", "#000000000#", "###########"], 1, 1, 1, 5 },
        { "terraces", ["0123432100", "0123432100", "0000000000"], 0, 0, 9, 0 },
        { "lava field", ["000000000", "0LLLLLLL0", "0L00000L0", "0LLLL0LL0", "000000000"], 3, 2, 8, 0 },
    };

    private static string[] Ridge(int ridgeRows) =>
        Enumerable.Range(0, ridgeRows + 2).Select(z => "00000000" + (z < ridgeRows ? "12321" : "00000") + "00000000").ToArray();

    [Theory]
    [MemberData(nameof(NamedGrids))]
    public void NamedGrids_SameCostAsReferenceSearch(string name, string[] rows, int sx, int sz, int gx, int gz)
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        GridPos start = Feet(grid, sx, sz);
        GridPos goal = Feet(grid, gx, gz);

        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, Model);
        FineRoute route = FineAStar.FindPath(grid, start, goal);

        Assert.True(oracle.Found, name);
        Assert.Equal(FineSearchStatus.Found, route.Status);
        Assert.Equal(oracle.Cost, route.Cost, Eps);
        AssertValid(grid, route, Model);
    }

    [Fact]
    public void OpenField_ExpandsFarFewerNodesThanDijkstra()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(64, 64);
        var start = new GridPos(2, 1, 2);
        var goal = new GridPos(61, 1, 40);

        FineRoute route = FineAStar.FindPath(grid, start, goal);
        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal);

        Assert.Equal(oracle.Cost, route.Cost, Eps);
        Assert.True(route.Expanded * 10 < oracle.Expanded, $"A* {route.Expanded} vs Dijkstra {oracle.Expanded}");
    }

    [Fact]
    public void Heuristic_NeverOverestimates_TheOracleCost()
    {
        string[] rows = RandomHeightmap(99, 24, 24, closed: true);
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        GridPos[] cells = Standable(grid, rows);
        GridPos goal = cells[cells.Length / 2];
        foreach (GridPos p in cells.Where((_, i) => i % 7 == 0))
        {
            ReferenceSearchResult r = ReferenceSearch.FindPath(grid, p, goal);
            if (r.Found)
            {
                Assert.True(FineAStar.Heuristic(Model, p, goal) <= r.Cost + Eps);
            }
        }
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void EqualCostAlternatives_AlwaysGiveTheSameRoute()
    {
        // A symmetric field: many equal-cost routes.
        string[] rows = Enumerable.Repeat("0000000000000", 9).ToArray();
        rows[4] = "000000#000000";
        GridPos start = new(0, 1, 4), goal = new(12, 1, 4);

        GridPos[] first = FineAStar.FindPath(FakeGrid.FromHeightmap(rows), start, goal).Path.ToArray();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(first, FineAStar.FindPath(FakeGrid.FromHeightmap(rows), start, goal).Path);
        }
    }

    // ---------------------------------------------------------------- endpoints and unreachable goals

    [Fact]
    public void StartEqualsGoal_IsAZeroCostRoute()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(3, 3);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(1, 1, 1));
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.Equal(0, r.Cost);
        Assert.Single(r.Steps);
    }

    [Fact]
    public void InvalidStartAndGoal_AreReported()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["0000", "0000"]);
        Assert.Equal(FineSearchStatus.InvalidStart, FineAStar.FindPath(grid, new GridPos(0, 3, 0), new GridPos(3, 1, 0)).Status); // mid-air
        Assert.Equal(FineSearchStatus.InvalidGoal, FineAStar.FindPath(grid, new GridPos(0, 1, 0), new GridPos(3, 0, 0)).Status);  // inside the floor
        Assert.Equal(FineSearchStatus.InvalidGoal, FineAStar.FindPath(grid, new GridPos(0, 1, 0), new GridPos(3, 3, 1)).Status);  // mid-air
    }

    [Fact]
    public void WalledOffGoal_InClosedWorld_IsNoPath()
    {
        string[] rows =
        [
            "#########",
            "#000#000#",
            "#000#000#",
            "#000#000#",
            "#########",
        ];
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);

        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 2), new GridPos(7, 1, 2));
        Assert.Equal(FineSearchStatus.NoPath, r.Status);
        Assert.Equal(ReferenceSearchStatus.NoPath, ReferenceSearch.FindPath(grid, new GridPos(1, 1, 2), new GridPos(7, 1, 2)).Status);
        Assert.False(r.HasRoute);
        Assert.Equal(9, r.Expanded); // the 3x3 room
        Assert.Equal(0, r.UnknownMoves);
    }

    [Fact]
    public void GoalBeyondUnknownBand_EndsAtTheBoundaryWithOneUnknownSegment()
    {
        string[] rows = Enumerable.Repeat("00000000???00000", 5).ToArray();
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        var start = new GridPos(1, 1, 2);
        var goal = new GridPos(14, 1, 2);

        FineRoute r = FineAStar.FindPath(grid, start, goal);

        Assert.Equal(FineSearchStatus.UnknownBoundary, r.Status);
        Assert.NotNull(r.Unknown);
        Assert.Equal(goal, r.Unknown!.Value.To);
        Assert.Equal(new GridPos(7, 1, 2), r.Unknown.Value.From); // the last known column, straight towards the goal
        Assert.Equal(r.Steps[^1].Pos, r.Unknown.Value.From);
        Assert.All(r.Path, p => Assert.True(p.X <= 7, $"walked into Unknown at {p}"));
        Assert.True(r.UnknownMoves > 0);
        AssertValid(grid, r, Model);
        Assert.Equal(6, r.Cost, Eps);
    }

    [Fact]
    public void GoalOutsideTheGrid_IsUnknownBoundary_NeverAir()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(10, 10);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(40, 1, 1));
        Assert.Equal(FineSearchStatus.UnknownBoundary, r.Status);
        Assert.Equal(new GridPos(9, 1, 1), r.Unknown!.Value.From);
    }

    // ---------------------------------------------------------------- budgets and cancellation

    [Fact]
    public void NodeBudget_ReturnsTheBestPartialRouteMarkedUnknown()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(80, 20);
        var start = new GridPos(1, 1, 10);
        var goal = new GridPos(78, 1, 10);

        FineRoute r = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { MaxNodes = 25 });

        Assert.Equal(FineSearchStatus.BudgetExhausted, r.Status);
        Assert.Equal(25, r.Expanded);
        Assert.Equal(start, r.Steps[0].Pos);
        Assert.Equal(goal, r.Unknown!.Value.To);
        Assert.Equal(r.Steps[^1].Pos, r.Unknown.Value.From);
        Assert.True(r.Steps[^1].Pos.X > start.X + 10, "the partial route should make progress towards the goal");
        AssertValid(grid, r, Model);
    }

    [Fact]
    public void ReferenceBudgetSemantics_Match()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(30, 30);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 0), new GridPos(29, 1, 29), new FineSearchOptions { MaxNodes = 1 });
        Assert.Equal(FineSearchStatus.BudgetExhausted, r.Status);
        Assert.Equal(1, r.Expanded);
    }

    [Fact]
    public void TimeBudget_StopsTheSearch()
    {
        // The goal lies outside the grid, so the search floods the whole 60 x 60 field.
        BlockGridSnapshot grid = FakeGrid.Flat(60, 60);
        var goal = new GridPos(200, 1, 30);
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var options = new FineSearchOptions { MaxTime = TimeSpan.FromMilliseconds(25), TimeProvider = clock };

        FineRoute r = FineAStar.FindPath(grid, new GridPos(0, 1, 0), goal, options);

        Assert.Equal(FineSearchStatus.TimedOut, r.Status);
        Assert.Equal(2 * FineSearchOptions.TimeCheckInterval, r.Expanded); // the clock moves 10 ms per read: elapsed 10, 20, then 30 ms > 25 at the third check
        Assert.NotNull(r.Unknown);
        AssertValid(grid, r, Model);

        FineRoute generous = FineAStar.FindPath(grid, new GridPos(0, 1, 0), goal, options with { MaxTime = TimeSpan.FromHours(1) });
        Assert.Equal(FineSearchStatus.UnknownBoundary, generous.Status);
        Assert.Equal(3600, generous.Expanded);
    }

    [Fact]
    public void Cancellation_BeforeAndDuringTheSearch()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(60, 60);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(FineSearchStatus.Cancelled, FineAStar.FindPath(grid, new GridPos(0, 1, 0), new GridPos(59, 1, 59), null, cancelled.Token).Status);

        using var later = new CancellationTokenSource();
        var counting = new CountingGrid(grid, 5_000, later.Cancel);
        FineRoute r = FineAStar.FindPath(counting, new GridPos(0, 1, 0), new GridPos(59, 1, 59), null, later.Token);
        Assert.Equal(FineSearchStatus.Cancelled, r.Status);
        Assert.False(r.HasRoute);
        Assert.InRange(r.Expanded, 1, 3_000);
    }

    // ---------------------------------------------------------------- climb / drop limits

    [Fact]
    public void StepUpOne_IsWalked_ButATwoBlockCliffIsNot()
    {
        BlockGridSnapshot oneStep = FakeGrid.FromHeightmap(["######", "#0011#", "######"]);
        FineRoute ok = FineAStar.FindPath(oneStep, new GridPos(1, 1, 1), new GridPos(4, 2, 1));
        Assert.Equal(FineSearchStatus.Found, ok.Status);
        Assert.Equal(1, ok.TotalClimb);

        BlockGridSnapshot cliff = FakeGrid.FromHeightmap(["######", "#0022#", "######"]);
        Assert.Equal(FineSearchStatus.NoPath, FineAStar.FindPath(cliff, new GridPos(1, 1, 1), new GridPos(4, 3, 1)).Status);
    }

    [Fact]
    public void DropOfThree_IsAllowed_DropOfFour_IsNot()
    {
        BlockGridSnapshot three = FakeGrid.FromHeightmap(["######", "#3300#", "######"]);
        FineRoute r = FineAStar.FindPath(three, new GridPos(1, 4, 1), new GridPos(4, 1, 1));
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.Equal(1 + (1 + 1 * 3) + 1, r.Cost, Eps); // walk, drop 3 (downhill 1 each), walk

        BlockGridSnapshot four = FakeGrid.FromHeightmap(["######", "#4400#", "######"]);
        Assert.Equal(FineSearchStatus.NoPath, FineAStar.FindPath(four, new GridPos(1, 5, 1), new GridPos(4, 1, 1)).Status);
    }

    [Fact]
    public void ClimbLimits_FollowTheOptions()
    {
        var lenient = new ClimbCostModel(new ClimbCostOptions { MaxClimb = 2 });
        BlockGridSnapshot cliff = FakeGrid.FromHeightmap(["######", "#0022#", "######"]);
        FineRoute r = FineAStar.FindPath(cliff, new GridPos(1, 1, 1), new GridPos(4, 3, 1), new FineSearchOptions { Model = lenient });
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.Equal(ReferenceSearch.FindPath(cliff, new GridPos(1, 1, 1), new GridPos(4, 3, 1), lenient).Cost, r.Cost, Eps);
    }

    // ---------------------------------------------------------------- water and headroom

    [Fact]
    public void Water_CostsFiveTimesDistance_AndLandIsPreferredWhenShorter()
    {
        // Straight across the pond costs 5 per water cell; the dry way round is cheaper.
        string[] rows =
        [
            "#########",
            "#0000000#",
            "#000~000#",
            "#00~~~00#",
            "#000~000#",
            "#0000000#",
            "#########",
        ];
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        var start = new GridPos(2, 1, 3);
        var goal = new GridPos(6, 1, 3);
        FineRoute r = FineAStar.FindPath(grid, start, goal);

        Assert.Equal(ReferenceSearch.FindPath(grid, start, goal).Cost, r.Cost, Eps);
        Assert.DoesNotContain(r.Steps, s => s.Kind == MoveKind.Swim);

        // A moat with no way round: must swim, and pays the water multiplier.
        BlockGridSnapshot moat = FakeGrid.FromHeightmap(["#######", "#00~00#", "#######"]);
        FineRoute swim = FineAStar.FindPath(moat, new GridPos(1, 1, 1), new GridPos(5, 1, 1));
        Assert.Equal(FineSearchStatus.Found, swim.Status);
        Assert.Equal(1 + 5 + 1 + 1, swim.Cost, Eps); // walk, swim into the moat (x5), walk out, walk
        Assert.Contains(swim.Steps, s => s.Kind == MoveKind.Swim);
    }

    [Fact]
    public void LowCeiling_BlocksTheTunnel_UnlessHeadroomIsOne()
    {
        // A corridor at feet level 1 whose middle cell has a ceiling at y = 2 (only 1 cell of headroom).
        BlockGridSnapshot grid = FakeGrid.FromLayers(
        [
            ["#####"],
            ["....."],
            ["..#.."],
            ["....."],
        ]);
        var start = new GridPos(0, 1, 0);
        var goal = new GridPos(4, 1, 0);

        Assert.NotEqual(FineSearchStatus.Found, FineAStar.FindPath(grid, start, goal).Status);

        var shortPlayer = new ClimbCostModel(new ClimbCostOptions { HeadroomCells = 1 });
        FineRoute r = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = shortPlayer });
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.Equal(4, r.Cost, Eps);
    }

    [Fact]
    public void Lava_IsNeverEntered()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["#######", "#00L00#", "#00L00#", "#00000#", "#######"]);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(5, 1, 1));
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.All(r.Path, p =>
        {
            Assert.NotEqual(CellClass.Lava, grid.GetClass(p));
            Assert.NotEqual(CellClass.Lava, grid.GetClass(p.Down));
        });
        AssertValid(grid, r, Model);
    }

    // ---------------------------------------------------------------- build moves (soft cost)

    private static readonly string[] Cliff2 = ["#######", "#00022#", "#######"];
    private static readonly GridPos CliffStart = new(1, 1, 1);
    private static readonly GridPos CliffTop = new(5, 3, 1);

    [Fact]
    public void TwoBlockCliff_GetsAStep_WithOneBlock()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(Cliff2);
        FineRoute r = FineAStar.FindPath(grid, CliffStart, CliffTop, WithBuild());

        Assert.Equal(FineSearchStatus.Found, r.Status);
        BuildSite site = Assert.Single(r.BuildSites);
        Assert.Equal(BuildKind.Step, site.Kind);
        Assert.Equal(1, site.Count);
        Assert.Equal(new GridPos(3, 1, 1), site.Blocks[0]); // in the feet cell at the cliff foot
        Assert.Equal(1, r.BlocksNeeded);
        // 2 flat steps, then climb 2 (uphill 4 each) over 1 horizontal block, plus 24 for the block, then 1 flat.
        Assert.Equal(2 + (1 + 4 * 2) + 24 + 1, r.Cost, Eps);
        AssertValid(grid, r, Model);
    }

    [Fact]
    public void ThreeBlockCliff_GetsAPillar_WithTwoBlocks()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(["######", "#0033#", "######"]);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(4, 4, 1), WithBuild());

        Assert.Equal(FineSearchStatus.Found, r.Status);
        BuildSite site = Assert.Single(r.BuildSites);
        Assert.Equal(BuildKind.Pillar, site.Kind);
        Assert.Equal([new GridPos(2, 1, 1), new GridPos(2, 2, 1)], site.Blocks);
        Assert.Equal(1 + (1 + 4 * 3 + 24 * 2) + 1, r.Cost, Eps);
        AssertValid(grid, r, Model);
    }

    [Fact]
    public void Chasm_GetsABridge_OverAirAndLava()
    {
        // Two cliffs at height 3 with a 2-wide gap down to the floor (air) or to lava.
        foreach (char floor in new[] { '0', 'L' })
        {
            string mid = new(floor, 2);
            BlockGridSnapshot grid = FakeGrid.FromHeightmap(["#########", "#333" + mid + "33#", "#########"]);
            var start = new GridPos(1, 4, 1);
            var goal = new GridPos(7, 4, 1);

            FineRoute r = FineAStar.FindPath(grid, start, goal, WithBuild());

            Assert.Equal(FineSearchStatus.Found, r.Status);
            BuildSite site = Assert.Single(r.BuildSites);
            Assert.Equal(BuildKind.Bridge, site.Kind);
            Assert.Equal([new GridPos(4, 3, 1), new GridPos(5, 3, 1)], site.Blocks);
            Assert.Equal(6 + 2 * 24, r.Cost, Eps);
            AssertValid(grid, r, Model);
        }
    }

    [Fact]
    public void Bridge_PlacesBlocksIntoLava()
    {
        BlockGridSnapshot grid = FakeGrid.FromLayers([["######"], ["##LL##"], ["......"], ["......"]]);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 2, 0), new GridPos(4, 2, 0), WithBuild());

        Assert.Equal(FineSearchStatus.Found, r.Status);
        BuildSite site = Assert.Single(r.BuildSites);
        Assert.Equal([new GridPos(2, 1, 0), new GridPos(3, 1, 0)], site.Blocks);
        Assert.Equal(3 + 2 * 24, r.Cost, Eps);
        Assert.NotEqual(FineSearchStatus.Found, FineAStar.FindPath(grid, new GridPos(1, 2, 0), new GridPos(4, 2, 0)).Status);
    }

    [Fact]
    public void NoBuildDetour_IsPreferred_WhenShorterThanTheBuildPenalty()
    {
        // The cliff has a ramp a few rows away: the detour adds less than 24, so no block is used.
        string[] rows =
        [
            "#########",
            "#0000222#",
            "#0000222#",
            "#0000122#",
            "#########",
        ];
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        FineRoute r = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(7, 3, 1), WithBuild());
        Assert.Equal(FineSearchStatus.Found, r.Status);
        Assert.Empty(r.BuildSites);
        Assert.Equal(ReferenceSearch.FindPath(grid, new GridPos(1, 1, 1), new GridPos(7, 3, 1)).Cost, r.Cost, Eps);

        // With a tiny penalty the step is cheaper than the detour and is used.
        FineRoute cheap = FineAStar.FindPath(grid, new GridPos(1, 1, 1), new GridPos(7, 3, 1),
            WithBuild(BuildMoveOptions.EnabledDefaults with { BuildPenalty = 0.5 }));
        Assert.NotEmpty(cheap.BuildSites);
        Assert.True(cheap.Cost < r.Cost);
    }

    [Fact]
    public void NoBlocksInInventory_MeansNoBuildMoves()
    {
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(Cliff2);
        var noBlocks = BuildMoveOptions.EnabledDefaults with { BlocksAvailable = false };
        Assert.Equal(FineSearchStatus.NoPath, FineAStar.FindPath(grid, CliffStart, CliffTop, WithBuild(noBlocks)).Status);
        Assert.Equal(FineSearchStatus.NoPath, FineAStar.FindPath(grid, CliffStart, CliffTop).Status); // disabled by default
    }

    [Fact]
    public void BuildForbidden_CellsNeverGetABlock()
    {
        // Forbid every placement cell at the cliff foot (feet level, column x = 3) and the bridge cells.
        BlockGridSnapshotBuilder b = FakeGrid.HeightmapBuilder(Cliff2);
        b.AddFlags(3, 1, 1, CellFlags.BuildForbidden);
        BlockGridSnapshot grid = b.Build();
        FineRoute r = FineAStar.FindPath(grid, CliffStart, CliffTop, WithBuild());
        // The only Step site is forbidden, and nothing else reaches the top of the cliff.
        Assert.Equal(FineSearchStatus.NoPath, r.Status);

        // A chasm whose bridge support cell is forbidden: no bridge; the route drops in and pillars out instead.
        BlockGridSnapshotBuilder chasm = FakeGrid.HeightmapBuilder(["########", "#330033#", "########"]);
        chasm.AddFlags(4, 3, 1, CellFlags.BuildForbidden);
        BlockGridSnapshot forbidden = chasm.Build();
        FineRoute detour = FineAStar.FindPath(forbidden, new GridPos(1, 4, 1), new GridPos(6, 4, 1), WithBuild());
        Assert.Equal(FineSearchStatus.Found, detour.Status);
        Assert.DoesNotContain(detour.BuildSites, s => s.Kind == BuildKind.Bridge);
        Assert.DoesNotContain(detour.BuildSites.SelectMany(s => s.Blocks), p => p == new GridPos(4, 3, 1));
        AssertValid(forbidden, detour, Model);

        BlockGridSnapshot allowed = FakeGrid.FromHeightmap(["########", "#330033#", "########"]);
        Assert.Equal(BuildKind.Bridge, Assert.Single(FineAStar.FindPath(allowed, new GridPos(1, 4, 1), new GridPos(6, 4, 1), WithBuild()).BuildSites).Kind);
    }

    [Fact]
    public void BuildRoutes_StayOptimal_ComparedWithBuildFreeRoutes()
    {
        // Enabling build moves can only lower (or keep) the cost.
        for (int seed = 1; seed <= 10; seed++)
        {
            string[] rows = RandomHeightmap(seed, 20, 20, closed: true, features: 0.15);
            BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
            GridPos[] cells = Standable(grid, rows);
            GridPos start = cells[0], goal = cells[^1];
            FineRoute walk = FineAStar.FindPath(grid, start, goal);
            FineRoute build = FineAStar.FindPath(grid, start, goal, WithBuild());
            if (walk.Found)
            {
                Assert.True(build.Found);
                Assert.True(build.Cost <= walk.Cost + Eps);
            }

            if (build.Found)
            {
                AssertValid(grid, build, Model);
            }
        }
    }

    // ---------------------------------------------------------------- performance (WF-12 AC: 96-block window < 30 ms)

    [Fact]
    public void NinetySixBlockWindow_PlansQuickly()
    {
        string[] rows = RandomHeightmap(2026, 96, 96, closed: true, features: 0.05);
        BlockGridSnapshot grid = FakeGrid.FromHeightmap(rows);
        GridPos[] cells = Standable(grid, rows);
        GridPos start = cells.MinBy(p => p.X + p.Z);
        GridPos goal = cells.MaxBy(p => p.X + p.Z);
        // Best of several runs, so tiered JIT warm-up is not what gets measured.
        FineRoute r = FineAStar.FindPath(grid, start, goal);
        TimeSpan best = TimeSpan.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var timer = Stopwatch.StartNew();
            r = FineAStar.FindPath(grid, start, goal);
            best = timer.Elapsed < best ? timer.Elapsed : best;
        }

        Assert.Equal(FineSearchStatus.Found, r.Status);
        // 30 ms is the target on a desktop; allow headroom for shared CI runners and Debug builds.
        Console.WriteLine($"96-block window: {best.TotalMilliseconds:F1} ms, {r.Expanded} expanded, cost {r.Cost:F1}");
        Assert.True(best.TotalMilliseconds < 100, $"took {best.TotalMilliseconds:F1} ms, expanded {r.Expanded}");
    }

    [Fact]
    public void Options_AreValidated()
    {
        BlockGridSnapshot grid = FakeGrid.Flat(3, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => FineAStar.FindPath(grid, default, default, new FineSearchOptions { MaxNodes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => FineAStar.FindPath(grid, default, default, new FineSearchOptions { MaxTime = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => FineAStar.FindPath(grid, default, default, WithBuild(BuildMoveOptions.EnabledDefaults with { BuildPenalty = double.NaN })));
    }
}
