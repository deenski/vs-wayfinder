using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;
using static Wayfinding.Pathing.Tests.SearchTestSupport;

namespace Wayfinding.Pathing.Tests;

/// <summary>
/// KAN-64: an opt-in drop past <see cref="ClimbCostOptions.MaxDrop"/> into deep water.
/// With the option off, the KAN-26 parity seeds in <see cref="FineAStarTests"/> stay the behaviour check.
/// </summary>
public class WaterDropTests
{
    private const double Eps = 1e-9;

    private static ClimbCostModel Water(int maxWaterDrop = 8, int minDepth = 2, int maxDrop = 3) =>
        new(new ClimbCostOptions { MaxWaterDrop = maxWaterDrop, MinWaterDepth = minDepth, MaxDrop = maxDrop });

    /// <summary>
    /// A walled corridor: start, then a pool column, then the goal at the pool's surface height.
    /// <paramref name="waterDepth"/> 0 is dry land. The border is solid, so a blocked drop is
    /// <see cref="FineSearchStatus.NoPath"/> rather than an Unknown edge.
    /// </summary>
    private static (BlockGridSnapshot Grid, GridPos Start, GridPos Landing, GridPos Goal) Corridor(int drop, int waterDepth)
    {
        int surface = Math.Max(waterDepth, 1);
        int startY = surface + drop;
        int top = startY + 2;
        var b = new BlockGridSnapshotBuilder(default, 5, top + 1, 3);
        b.Fill(new GridPos(0, 0, 0), new GridPos(4, top, 2), CellClass.Solid);
        b.SetColumn(1, 1, startY, top, CellClass.Air);
        b.SetColumn(2, 1, 0, top, CellClass.Air);
        b.Set(2, 0, 1, CellClass.Solid);
        for (int y = 1; y <= waterDepth; y++)
        {
            b.Set(2, y, 1, CellClass.Water);
        }

        b.SetColumn(3, 1, surface, top, CellClass.Air);
        return (b.Build(), new GridPos(1, startY, 1), new GridPos(2, surface, 1), new GridPos(3, surface, 1));
    }

    private static int MaxStepDrop(IReadOnlyList<GridPos> path)
    {
        int drop = 0;
        for (int i = 1; i < path.Count; i++)
        {
            drop = Math.Max(drop, path[i - 1].Y - path[i].Y);
        }

        return drop;
    }

    [Fact]
    public void Defaults_DisableWaterDrops_AndMatchTheParitySeedShape()
    {
        ClimbCostOptions bare = new ClimbCostOptions { UphillPenalty = 4, DownhillPenalty = 1 };
        Assert.Equal(0, bare.MaxWaterDrop);
        Assert.Equal(2, bare.MinWaterDepth);
        Assert.Equal(bare.MaxDrop, bare.GeneratedDrop);

        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(6, 3);
        MoveCost move = new ClimbCostModel(bare).Evaluate(grid, start, landing);
        Assert.Equal((MoveVerdict.Blocked, BlockReason.TooDeep), (move.Verdict, move.Reason));
        Assert.True(double.IsPositiveInfinity(move.Cost));
        Assert.Equal(StandKind.Swim, Standability.Classify(grid, landing));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    [InlineData(8, 4)]
    public void DeepWater_AllowsTheDrop_AndKeepsTheDownhillPenalty(int drop, int depth)
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(drop, depth);
        MoveCost move = model.Evaluate(grid, start, landing);

        Assert.Equal(MoveVerdict.Ok, move.Verdict);
        Assert.True(move.Swim);
        Assert.Equal((0, drop), (move.Climb, move.Drop));
        Assert.Equal(model.Options.WaterCost + model.Options.DownhillPenalty * drop, move.Cost, Eps);

        var raised = new ClimbCostModel(new ClimbCostOptions { MaxDrop = drop });
        Assert.Equal(raised.Evaluate(grid, start, landing).Cost, move.Cost, Eps);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void ShallowWater_AndLand_StayTooDeep(int drop)
    {
        var model = Water();
        (BlockGridSnapshot shallowGrid, GridPos shallowStart, GridPos shallowLanding, _) = Corridor(drop, 1);
        (BlockGridSnapshot landGrid, GridPos landStart, GridPos landLanding, _) = Corridor(drop, 0);

        Assert.Equal(BlockReason.TooDeep, model.Evaluate(shallowGrid, shallowStart, shallowLanding).Reason);
        Assert.Equal(StandKind.Swim, Standability.Classify(shallowGrid, shallowLanding));
        Assert.Equal(BlockReason.TooDeep, model.Evaluate(landGrid, landStart, landLanding).Reason);
        Assert.Equal(StandKind.Ground, Standability.Classify(landGrid, landLanding));
    }

    [Fact]
    public void DropPastMaxWaterDrop_IsTooDeep_EvenIntoDeepWater()
    {
        var model = Water(maxWaterDrop: 8);
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(9, 4);
        Assert.Equal(BlockReason.TooDeep, model.Evaluate(grid, start, landing).Reason);
    }

    [Fact]
    public void DropWithinMaxDrop_DoesNotConsultDepth()
    {
        // A 3-block drop into a puddle with Unknown under it is already legal. The water rule must not
        // start rejecting it, and must not read that Unknown as a reason to abstain.
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(3, 2);
        var carved = Carve(grid, landing.X, landing.Y - 1, landing.Z, CellClass.Unknown);
        var model = Water();
        MoveCost move = model.Evaluate(carved, start, landing);
        Assert.Equal(MoveVerdict.Ok, move.Verdict);
        Assert.True(move.Swim);
        Assert.Equal(model.Options.WaterCost + model.Options.DownhillPenalty * 3, move.Cost, Eps);
    }

    [Fact]
    public void UnknownBelowTheLanding_IsUnknown_NeverOk()
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(6, 2);
        var carved = Carve(grid, landing.X, landing.Y - 1, landing.Z, CellClass.Unknown);
        MoveCost move = model.Evaluate(carved, start, landing);

        Assert.Equal(StandKind.Swim, Standability.Classify(carved, landing));
        Assert.Equal(MoveVerdict.Unknown, move.Verdict);
        Assert.Equal(BlockReason.Unknown, move.Reason);
        Assert.False(move.IsOk);
        Assert.True(double.IsPositiveInfinity(move.Cost));
    }

    [Fact]
    public void UnknownOutsideTheSnapshot_BelowTheLanding_IsUnknown()
    {
        const int surface = 1;
        const int drop = 6;
        int startY = surface + drop;
        int top = startY + 2;
        var b = new BlockGridSnapshotBuilder(new GridPos(0, 1, 0), 2, top, 1);
        b.Fill(new GridPos(0, 1, 0), new GridPos(1, top, 0), CellClass.Solid);
        b.SetColumn(0, 0, startY, top, CellClass.Air);
        b.SetColumn(1, 0, surface, top, CellClass.Air);
        b.Set(1, surface, 0, CellClass.Water);
        BlockGridSnapshot grid = b.Build();

        Assert.Equal(CellClass.Unknown, grid.GetClass(1, 0, 0));
        MoveCost move = Water().Evaluate(grid, new GridPos(0, startY, 0), new GridPos(1, surface, 0));
        Assert.Equal(MoveVerdict.Unknown, move.Verdict);
        Assert.False(move.IsOk);
    }

    [Fact]
    public void UnknownDeeperThanTheMinimum_DoesNotRejectAConfirmedColumn()
    {
        // MinWaterDepth 2 reads only the cell under the feet. Water there is enough; Unknown further down is not read.
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(6, 3);
        var carved = Carve(grid, landing.X, 0, landing.Z, CellClass.Unknown);
        MoveCost move = Water().Evaluate(carved, start, landing);
        Assert.Equal(MoveVerdict.Ok, move.Verdict);
        Assert.Equal(Water().Options.WaterCost + 6, move.Cost, Eps);
    }

    [Fact]
    public void MinWaterDepthOne_AllowsAPuddle_AndStillBlocksLand()
    {
        var model = Water(minDepth: 1);
        (BlockGridSnapshot puddle, GridPos puddleStart, GridPos puddleLanding, _) = Corridor(6, 1);
        (BlockGridSnapshot land, GridPos landStart, GridPos landLanding, _) = Corridor(6, 0);
        MoveCost intoPuddle = model.Evaluate(puddle, puddleStart, puddleLanding);
        Assert.Equal(MoveVerdict.Ok, intoPuddle.Verdict);
        Assert.Equal(model.Options.WaterCost + model.Options.DownhillPenalty * 6, intoPuddle.Cost, Eps);
        Assert.Equal(BlockReason.TooDeep, model.Evaluate(land, landStart, landLanding).Reason);
    }

    [Fact]
    public void LavaInTheFall_WinsOverUnknownDepth()
    {
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(6, 2);
        var carved = Carve(grid, landing.X, landing.Y - 1, landing.Z, CellClass.Unknown);
        carved = Carve(carved, landing.X, landing.Y + 2, landing.Z, CellClass.Lava);
        MoveCost move = Water().Evaluate(carved, start, landing);
        Assert.Equal((MoveVerdict.Blocked, BlockReason.Lava), (move.Verdict, move.Reason));
    }

    [Fact]
    public void SubmergedLanding_IsNotStandable()
    {
        (BlockGridSnapshot grid, GridPos start, GridPos landing, _) = Corridor(6, 3);
        var submerged = Carve(grid, landing.X, landing.Y + 1, landing.Z, CellClass.Water);
        Assert.Equal(BlockReason.NotStandable, Water().Evaluate(submerged, start, landing).Reason);
    }

    [Fact]
    public void DiagonalDeepWaterDrop_PaysWaterCostAndTheDownhillPenalty()
    {
        const int drop = 6;
        const int surface = 2;
        int startY = surface + drop;
        int top = startY + 2;
        var b = new BlockGridSnapshotBuilder(default, 2, top + 1, 2);
        b.Fill(default, new GridPos(1, top, 1), CellClass.Air);
        b.SetColumn(0, 0, 0, startY - 1, CellClass.Solid);
        b.Set(1, 0, 1, CellClass.Solid);
        b.Set(1, 1, 1, CellClass.Water);
        b.Set(1, surface, 1, CellClass.Water);
        BlockGridSnapshot grid = b.Build();
        var from = new GridPos(0, startY, 0);
        var to = new GridPos(1, surface, 1);

        MoveCost move = Water().Evaluate(grid, from, to);
        Assert.Equal(MoveVerdict.Ok, move.Verdict);
        Assert.Equal(5.0 * Math.Sqrt(2) + drop, move.Cost, Eps);

        b = new BlockGridSnapshotBuilder(default, 2, top + 1, 2);
        b.Fill(default, new GridPos(1, top, 1), CellClass.Air);
        b.SetColumn(0, 0, 0, startY - 1, CellClass.Solid);
        b.Set(1, 0, 1, CellClass.Solid);
        b.Set(1, 1, 1, CellClass.Water);
        b.Set(1, surface, 1, CellClass.Water);
        b.Set(1, startY, 0, CellClass.Lava); // corner of the diagonal, at the height the body passes
        MoveCost cut = Water().Evaluate(b.Build(), from, to);
        Assert.Equal(BlockReason.Lava, cut.Reason);
    }

    [Fact]
    public void Formula_StaysInfinitePastMaxDrop_WhenTheWaterRuleIsOn()
    {
        var model = Water(maxWaterDrop: 10);
        Assert.True(double.IsPositiveInfinity(model.Formula(1, -4)));
        Assert.True(double.IsPositiveInfinity(model.Formula(1, -6)));
        Assert.Equal(1 + 3.0, model.Formula(1, -3), Eps);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(257, 2)]
    [InlineData(8, 0)]
    [InlineData(8, 257)]
    public void WaterDropOptions_RejectInvalidValues(int maxWaterDrop, int minDepth) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Water(maxWaterDrop, minDepth));

    [Fact]
    public void GeneratedDrop_IsTheWiderOfTheTwoLimits()
    {
        Assert.Equal(3, new ClimbCostOptions().GeneratedDrop);
        Assert.Equal(3, new ClimbCostOptions { MaxWaterDrop = 2 }.GeneratedDrop);
        Assert.Equal(8, new ClimbCostOptions { MaxWaterDrop = 8 }.GeneratedDrop);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    [InlineData(8, 4)]
    public void DeepWater_FineAStarMatchesReferenceSearch_AndTheHeuristicIsAdmissible(int drop, int depth)
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos landing, GridPos goal) = Corridor(drop, depth);

        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, model);
        FineRoute route = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = model });

        Assert.Equal(ReferenceSearchStatus.Found, oracle.Status);
        Assert.Equal(FineSearchStatus.Found, route.Status);
        Assert.Equal(oracle.Cost, route.Cost, Eps);
        Assert.Equal(goal, route.Steps[^1].Pos);
        Assert.Contains(landing, route.Path);
        Assert.True(MaxStepDrop(route.Path) > model.Options.MaxDrop);
        Assert.True(MaxStepDrop(oracle.Path) > model.Options.MaxDrop);
        AssertValid(grid, route, model);
        AssertAdmissible(grid, model, goal);
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(6, 0)]
    [InlineData(8, 1)]
    public void ShallowOrLand_NeitherSearchTakesTheDrop(int drop, int depth)
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos _, GridPos goal) = Corridor(drop, depth);
        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, model);
        FineRoute route = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = model });
        Assert.Equal(ReferenceSearchStatus.NoPath, oracle.Status);
        Assert.Equal(FineSearchStatus.NoPath, route.Status);
        Assert.Equal(0, route.UnknownMoves);
    }

    [Fact]
    public void RuleOff_CorridorAgreesWithReferenceSearch_AndDoesNotDrop()
    {
        var model = new ClimbCostModel();
        (BlockGridSnapshot grid, GridPos start, GridPos _, GridPos goal) = Corridor(6, 4);
        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, model);
        FineRoute route = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = model });
        Assert.Equal(ReferenceSearchStatus.NoPath, oracle.Status);
        Assert.Equal(FineSearchStatus.NoPath, route.Status);
    }

    [Fact]
    public void UnknownBelowTheLanding_IsNotAShortcut()
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos landing, GridPos goal) = Corridor(6, 2);
        var carved = Carve(grid, landing.X, landing.Y - 1, landing.Z, CellClass.Unknown);

        ReferenceSearchResult oracle = ReferenceSearch.FindPath(carved, start, goal, model);
        FineRoute route = FineAStar.FindPath(carved, start, goal, new FineSearchOptions { Model = model });

        Assert.Equal(ReferenceSearchStatus.NoPath, oracle.Status);
        Assert.Contains(route.Status, new[] { FineSearchStatus.NoPath, FineSearchStatus.UnknownBoundary });
        Assert.DoesNotContain(landing, oracle.Path);
        Assert.DoesNotContain(landing, route.Path);
        Assert.True(route.UnknownMoves > 0);
    }

    [Fact]
    public void Heuristic_OnTheWaterDrop_StaysAtOrBelowTheRouteCost()
    {
        var model = Water();
        (BlockGridSnapshot grid, GridPos start, GridPos _, GridPos goal) = Corridor(6, 3);
        FineRoute route = FineAStar.FindPath(grid, start, goal, new FineSearchOptions { Model = model });
        ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, start, goal, model);
        Assert.Equal(oracle.Cost, route.Cost, Eps);
        double h = FineAStar.Heuristic(model, start, goal);
        Assert.True(h <= route.Cost + Eps, $"heuristic {h} exceeded route cost {route.Cost}");
        Assert.True(model.Heuristic(start, goal) <= route.Cost + Eps);
    }

    private static void AssertAdmissible(BlockGridSnapshot grid, ClimbCostModel model, GridPos goal)
    {
        for (int z = 0; z < grid.SizeZ; z++)
        {
            for (int x = 0; x < grid.SizeX; x++)
            {
                for (int y = grid.Origin.Y; y < grid.Origin.Y + grid.SizeY; y++)
                {
                    var pos = new GridPos(x, y, z);
                    if (!Standability.Classify(grid, pos, model.Options.HeadroomCells).IsOccupiable())
                    {
                        continue;
                    }

                    ReferenceSearchResult oracle = ReferenceSearch.FindPath(grid, pos, goal, model);
                    if (!oracle.Found)
                    {
                        continue;
                    }

                    double h = FineAStar.Heuristic(model, pos, goal);
                    Assert.True(h <= oracle.Cost + Eps, $"{pos} heuristic {h} exceeded cost {oracle.Cost}");
                }
            }
        }
    }

    /// <summary>Copies a snapshot with one cell overwritten. The original is immutable.</summary>
    private static BlockGridSnapshot Carve(BlockGridSnapshot grid, int x, int y, int z, CellClass cell)
    {
        var b = new BlockGridSnapshotBuilder(grid.Origin, grid.SizeX, grid.SizeY, grid.SizeZ, grid.Dimension);
        for (int zz = 0; zz < grid.SizeZ; zz++)
        {
            for (int yy = 0; yy < grid.SizeY; yy++)
            {
                for (int xx = 0; xx < grid.SizeX; xx++)
                {
                    int wx = grid.Origin.X + xx;
                    int wy = grid.Origin.Y + yy;
                    int wz = grid.Origin.Z + zz;
                    CellClass c = wx == x && wy == y && wz == z ? cell : grid.GetClass(wx, wy, wz);
                    if (c != CellClass.Unknown)
                    {
                        b.Set(wx, wy, wz, c);
                    }
                }
            }
        }

        if (cell == CellClass.Unknown)
        {
            // Left unwritten above, which is Unknown. A written Unknown is the same value; skip it.
        }
        else
        {
            b.Set(x, y, z, cell);
        }

        return b.Build();
    }
}
