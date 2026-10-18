using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

/// <summary>KAN-25 acceptance: the climb trade-off, water, lava, limits and Unknown, end to end.</summary>
public class ReferenceSearchTests
{
    private const double Eps = 1e-9;
    private const int HillHeight = 3;

    // 21 columns (x = 0..20). Start at x = 1, goal at x = 19, both on row z = 0, feet at y = 1.
    // A ridge "12321" (crest 3 blocks above the ground) at x = 8..12 covers rows 0..ridgeRows-1; the two rows
    // beyond it are flat, so the detour goes round the end of the ridge. Longer ridge = longer detour.
    private static readonly GridPos Start = new(1, 1, 0);
    private static readonly GridPos Goal = new(19, 1, 0);

    private static BlockGridSnapshot Ridge(int ridgeRows, string ridge = "12321") =>
        FakeGrid.FromHeightmap(Enumerable.Range(0, ridgeRows + 2)
            .Select(z => "00000000" + (z < ridgeRows ? ridge : "00000") + "00000000")
            .ToArray());

    /// <summary>Cost of going round: the same map with the ridge replaced by an impassable wall.</summary>
    private static double DetourCost(int ridgeRows, ClimbCostModel model)
    {
        ReferenceSearchResult r = ReferenceSearch.FindPath(Ridge(ridgeRows, "#####"), Start, Goal, model);
        Assert.True(r.Found);
        return r.Cost;
    }

    private const double StraightDistance = 18; // 19 - 1

    [Fact]
    public void Hill_IsAvoided_WhenDetourIsShorterThanPenaltyTimesClimb()
    {
        var model = new ClimbCostModel(); // uphill 4, downhill 1
        const int rows = 6;
        double detourExtra = DetourCost(rows, model) - StraightDistance;
        double crossingPenalty = model.Options.UphillPenalty * HillHeight + model.Options.DownhillPenalty * HillHeight;
        Assert.True(detourExtra < model.Options.UphillPenalty * HillHeight, $"premise: detour adds {detourExtra}");

        ReferenceSearchResult r = ReferenceSearch.FindPath(Ridge(rows), Start, Goal, model);

        Assert.True(r.Found);
        Assert.Equal(1, r.MaxY);          // never set foot on the hill
        Assert.Equal(0, r.TotalClimb);
        Assert.Equal(StraightDistance + detourExtra, r.Cost, Eps);
        Assert.True(r.Cost < StraightDistance + crossingPenalty);
    }

    [Fact]
    public void Hill_IsCrossed_WhenDetourIsLongerThanPenaltyTimesClimb()
    {
        var model = new ClimbCostModel();
        const int rows = 14;
        double detourExtra = DetourCost(rows, model) - StraightDistance;
        double crossingPenalty = model.Options.UphillPenalty * HillHeight + model.Options.DownhillPenalty * HillHeight;
        Assert.True(detourExtra > crossingPenalty, $"premise: detour adds {detourExtra}");

        ReferenceSearchResult r = ReferenceSearch.FindPath(Ridge(rows), Start, Goal, model);

        Assert.True(r.Found);
        Assert.Equal(1 + HillHeight, r.MaxY); // over the crest
        Assert.Equal(HillHeight, r.TotalClimb);
        Assert.Equal(StraightDistance + crossingPenalty, r.Cost, Eps); // 18 + 4×3 + 1×3 = 33
        Assert.All(r.Path, p => Assert.Equal(0, p.Z)); // straight over
    }

    [Fact]
    public void Hill_IsCrossed_WhenUphillPenaltyIsZero()
    {
        // Same short-detour map that the default penalties avoid.
        var model = new ClimbCostModel(new ClimbCostOptions { UphillPenalty = 0 });
        const int rows = 6;
        Assert.True(DetourCost(rows, model) - StraightDistance > model.Options.DownhillPenalty * HillHeight, "premise");

        ReferenceSearchResult r = ReferenceSearch.FindPath(Ridge(rows), Start, Goal, model);

        Assert.True(r.Found);
        Assert.Equal(1 + HillHeight, r.MaxY);
        Assert.Equal(StraightDistance + 1.0 * HillHeight, r.Cost, Eps); // only the descent is charged
    }

    [Fact]
    public void ZeroPenalties_GiveThePureShortestRoute()
    {
        var model = new ClimbCostModel(new ClimbCostOptions { UphillPenalty = 0, DownhillPenalty = 0 });
        ReferenceSearchResult r = ReferenceSearch.FindPath(Ridge(2), Start, Goal, model);
        Assert.Equal(StraightDistance, r.Cost, Eps);
    }

    [Fact]
    public void Water_IsAvoidedWhenLandIsNearby()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(
        [
            "000~~~~~000",
            "000~~~~~000",
            "000~~~~~000",
            "00000000000",
        ]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(10, 1, 0));
        Assert.True(r.Found);
        Assert.DoesNotContain(r.Path, p => s.GetClass(p) == CellClass.Water);
    }

    [Fact]
    public void Water_IsCrossedAtExtraCostWhenThereIsNoLandRoute()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0000~~0000", "0000~~0000"]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(9, 1, 0));
        Assert.True(r.Found);
        Assert.Equal(2, r.Path.Count(p => s.GetClass(p) == CellClass.Water));
        Assert.Equal(9 + 2 * 4.0, r.Cost, Eps); // two blocks swum at 5 instead of 1
    }

    [Fact]
    public void Lava_IsNeverEntered_EvenForALongDetour()
    {
        string[] rows = Enumerable.Range(0, 12).Select(z => z < 11 ? "00000L00000" : "00000000000").ToArray();
        BlockGridSnapshot s = FakeGrid.FromHeightmap(rows);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(10, 1, 0));
        Assert.True(r.Found);
        Assert.DoesNotContain(r.Path, p => p.X == 5 && p.Z < 11);
        Assert.True(r.Cost > 20);
    }

    [Fact]
    public void Lava_RiverWithNoCrossing_MeansNoPath()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["000L000", "000L000", "000L000"]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 1), new GridPos(6, 1, 1));
        Assert.Equal(ReferenceSearchStatus.NoPath, r.Status);
        Assert.Empty(r.Path);
    }

    [Fact]
    public void MaxClimb_TwoBlockCliffNeedsAHigherLimit()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0000022222", "0000022222"]);
        var start = new GridPos(0, 1, 0);
        var goal = new GridPos(9, 3, 0);

        Assert.Equal(ReferenceSearchStatus.NoPath, ReferenceSearch.FindPath(s, start, goal).Status);

        ReferenceSearchResult r = ReferenceSearch.FindPath(s, start, goal, new ClimbCostModel(new ClimbCostOptions { MaxClimb = 2 }));
        Assert.True(r.Found);
        Assert.Equal(9 + 4 * 2.0, r.Cost, Eps);
    }

    [Fact]
    public void MaxClimb_StairsOfOneAreFine()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0001222"]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(6, 3, 0));
        Assert.True(r.Found);
        Assert.Equal(6 + 4 * 2.0, r.Cost, Eps);
    }

    [Fact]
    public void MaxDrop_FourBlockCliffNeedsAHigherLimit()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["4444000000"]);
        var start = new GridPos(0, 5, 0);
        var goal = new GridPos(9, 1, 0);

        Assert.Equal(ReferenceSearchStatus.NoPath, ReferenceSearch.FindPath(s, start, goal).Status);

        ReferenceSearchResult r = ReferenceSearch.FindPath(s, start, goal, new ClimbCostModel(new ClimbCostOptions { MaxDrop = 4 }));
        Assert.True(r.Found);
        Assert.Equal(9 + 1 * 4.0, r.Cost, Eps);
    }

    [Fact]
    public void MaxDrop_ThreeBlockDropIsAllowedByDefault()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["333000"]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 4, 0), new GridPos(5, 1, 0));
        Assert.True(r.Found);
        Assert.Equal(5 + 3.0, r.Cost, Eps);
    }

    [Fact]
    public void UnknownBand_StopsTheSearch_WithoutThrowing()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["000???000", "000???000"]);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(8, 1, 0));
        Assert.Equal(ReferenceSearchStatus.NoPath, r.Status);
        Assert.True(r.UnknownMoves > 0);
    }

    [Fact]
    public void GoalOutsideTheSnapshot_IsNoPath_NotAnException()
    {
        BlockGridSnapshot s = FakeGrid.Flat(5, 5);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(2, 1, 2), new GridPos(50, 1, 50));
        Assert.Equal(ReferenceSearchStatus.NoPath, r.Status);
        Assert.True(r.UnknownMoves > 0); // the snapshot edges read as Unknown, not air
    }

    [Fact]
    public void StartInUnknownTerrain_IsInvalidStart()
    {
        BlockGridSnapshot s = new BlockGridSnapshotBuilder(default, 4, 4, 4).Build();
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(1, 1, 1), new GridPos(2, 1, 2));
        Assert.Equal(ReferenceSearchStatus.InvalidStart, r.Status);
    }

    [Fact]
    public void Budget_IsRespected()
    {
        BlockGridSnapshot s = FakeGrid.Flat(30, 30);
        ReferenceSearchResult r = ReferenceSearch.FindPath(s, new GridPos(0, 1, 0), new GridPos(29, 1, 29), maxExpanded: 10);
        Assert.Equal(ReferenceSearchStatus.BudgetExhausted, r.Status);
        Assert.Equal(10, r.Expanded);
    }
}
