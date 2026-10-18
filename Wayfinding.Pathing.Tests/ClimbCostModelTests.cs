using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class ClimbCostModelTests
{
    private const double Eps = 1e-9;
    private static readonly ClimbCostModel Defaults = new();

    private static GridPos Feet(BlockGridSnapshot s, int x, int z)
    {
        for (int y = s.Origin.Y + s.SizeY - 1; y >= s.Origin.Y; y--)
        {
            if (Standability.Classify(s, new GridPos(x, y, z)).IsOccupiable())
            {
                return new GridPos(x, y, z);
            }
        }

        throw new InvalidOperationException($"No occupiable cell in column ({x}, {z}).");
    }

    [Fact]
    public void Defaults_MatchTheDesign()
    {
        ClimbCostOptions o = ClimbCostOptions.Default;
        Assert.Equal((4.0, 1.0, 1, 3, 5.0, 2), (o.UphillPenalty, o.DownhillPenalty, o.MaxClimb, o.MaxDrop, o.WaterCost, o.HeadroomCells));
    }

    [Theory]
    [InlineData(1.0, 0, false, 1.0)]
    [InlineData(1.0, 1, false, 5.0)]   // 1 + 4×1
    [InlineData(1.0, -1, false, 2.0)]  // 1 + 1×1
    [InlineData(1.0, -3, false, 4.0)]  // 1 + 1×3
    [InlineData(1.0, 0, true, 5.0)]    // water ×5
    public void Formula_IsDistancePlusPenalties(double h, int dy, bool swim, double expected) =>
        Assert.Equal(expected, Defaults.Formula(h, dy, swim), Eps);

    [Theory]
    [InlineData(2)]
    [InlineData(-4)]
    public void Formula_BeyondLimits_IsInfinite(int dy) =>
        Assert.True(double.IsPositiveInfinity(Defaults.Formula(1, dy)));

    [Fact]
    public void FlatSteps_CostTheirHorizontalDistance()
    {
        BlockGridSnapshot s = FakeGrid.Flat(3, 3);
        var from = new GridPos(1, 1, 1);
        Assert.Equal(1.0, Defaults.Evaluate(s, from, new GridPos(2, 1, 1)).Cost, Eps);
        Assert.Equal(Math.Sqrt(2), Defaults.Evaluate(s, from, new GridPos(2, 1, 2)).Cost, Eps);
    }

    [Fact]
    public void ClimbAndDrop_ArePenalised()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0123"]);
        MoveCost up = Defaults.Evaluate(s, Feet(s, 0, 0), Feet(s, 1, 0));
        MoveCost down = Defaults.Evaluate(s, Feet(s, 1, 0), Feet(s, 0, 0));
        Assert.Equal(5.0, up.Cost, Eps);
        Assert.Equal((1, 0), (up.Climb, up.Drop));
        Assert.Equal(2.0, down.Cost, Eps);
        Assert.Equal((0, 1), (down.Climb, down.Drop));
    }

    [Fact]
    public void Water_CostsMoreThanLand()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["00~~", "00~~"]);
        MoveCost land = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 0));
        MoveCost intoWater = Defaults.Evaluate(s, new GridPos(1, 1, 0), new GridPos(2, 1, 0));
        MoveCost swimDiagonal = Defaults.Evaluate(s, new GridPos(2, 1, 0), new GridPos(3, 1, 1));

        Assert.True(intoWater.IsOk && intoWater.Swim);
        Assert.Equal(1.0, land.Cost, Eps);
        Assert.Equal(5.0, intoWater.Cost, Eps);
        Assert.Equal(5.0 * Math.Sqrt(2), swimDiagonal.Cost, Eps);
        Assert.True(intoWater.Cost > land.Cost);
    }

    [Fact]
    public void Lava_IsBlocked()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0L0"]);
        MoveCost m = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 0));
        Assert.Equal(MoveVerdict.Blocked, m.Verdict);
        Assert.Equal(BlockReason.Lava, m.Reason);
        Assert.True(double.IsPositiveInfinity(m.Cost));
    }

    [Fact]
    public void Lava_DiagonalCornerCut_IsBlocked_ButWalkingAlongsideIsFine()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0L", "00"]);
        MoveCost cut = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 1));
        MoveCost along = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(0, 1, 1));
        Assert.Equal(BlockReason.Lava, cut.Reason);
        Assert.True(along.IsOk);
    }

    [Fact]
    public void Hazard_IsBlocked()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0!"]);
        MoveCost m = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 0));
        Assert.Equal((MoveVerdict.Blocked, BlockReason.Hazard), (m.Verdict, m.Reason));
    }

    [Fact]
    public void MaxClimb_IsEnforced()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["02"]);
        var from = new GridPos(0, 1, 0);
        var to = new GridPos(1, 3, 0);
        Assert.Equal(BlockReason.TooHigh, Defaults.Evaluate(s, from, to).Reason);

        var climber = new ClimbCostModel(new ClimbCostOptions { MaxClimb = 2 });
        MoveCost m = climber.Evaluate(s, from, to);
        Assert.True(m.IsOk);
        Assert.Equal(1 + 4 * 2, m.Cost, Eps);
    }

    [Fact]
    public void MaxDrop_IsEnforced()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["40", "30"]);
        Assert.Equal(BlockReason.TooDeep, Defaults.Evaluate(s, new GridPos(0, 5, 0), new GridPos(1, 1, 0)).Reason);
        Assert.Equal(1 + 3.0, Defaults.Evaluate(s, new GridPos(0, 4, 1), new GridPos(1, 1, 1)).Cost, Eps);

        var jumper = new ClimbCostModel(new ClimbCostOptions { MaxDrop = 4 });
        Assert.Equal(1 + 4.0, jumper.Evaluate(s, new GridPos(0, 5, 0), new GridPos(1, 1, 0)).Cost, Eps);
    }

    [Fact]
    public void Climb_NeedsHeadroomAboveTheStart()
    {
        // Column 0: floor at y=0, ceiling at y=3. Column 1: floor up to y=1. Stepping up needs y=3 clear in column 0.
        BlockGridSnapshot s = FakeGrid.FromLayers(
        [
            ["##"],
            [".#"],
            [".."],
            ["#."],
            [".."],
        ]);
        MoveCost m = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 2, 0));
        Assert.Equal(BlockReason.Obstructed, m.Reason);
    }

    [Fact]
    public void NotStandableDestination_IsBlocked()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["01"]);
        Assert.Equal(BlockReason.NotStandable, Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 0)).Reason); // inside the block
        Assert.Equal(BlockReason.NotStandable, Defaults.Evaluate(s, new GridPos(1, 2, 0), new GridPos(0, 2, 0)).Reason); // mid-air
    }

    [Fact]
    public void Unknown_IsReportedWithoutThrowing()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0?"]);
        var from = new GridPos(0, 1, 0);
        Assert.Equal(MoveVerdict.Unknown, Defaults.Evaluate(s, from, new GridPos(1, 1, 0)).Verdict);   // '?' column
        Assert.Equal(MoveVerdict.Unknown, Defaults.Evaluate(s, from, new GridPos(-1, 1, 0)).Verdict);  // outside the snapshot
        Assert.Equal(MoveVerdict.Unknown, Defaults.Evaluate(s, from, new GridPos(0, 1, 1)).Verdict);
        Assert.True(double.IsPositiveInfinity(Defaults.Evaluate(s, from, new GridPos(1, 1, 0)).Cost));

        BlockGridSnapshot empty = new BlockGridSnapshotBuilder(default, 1, 1, 1).Build();
        Assert.Equal(MoveVerdict.Unknown, Defaults.Evaluate(empty, new GridPos(int.MaxValue - 1, int.MaxValue, 0), new GridPos(int.MaxValue, int.MaxValue, 1)).Verdict);
    }

    [Fact]
    public void KnownBlocker_WinsOverUnknown()
    {
        // Destination feet cell is solid; the head cell above is outside the grid (Unknown).
        BlockGridSnapshot s = FakeGrid.FromLayers([["##"], [".#"]]);
        Assert.Equal(BlockReason.NotStandable, Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 0)).Reason);
    }

    // Diagonal (0,1,0) -> (1,1,1): destination standable; corner column (1,·,0) holds a known blocker at
    // feet height and corner column (0,·,1) is Unknown. The sweep sees both; the known blocker must win.
    [Theory]
    [InlineData('#', BlockReason.Obstructed)]
    [InlineData('L', BlockReason.Lava)]
    [InlineData('!', BlockReason.Hazard)]
    public void DiagonalSweep_KnownBlockerInOneCorner_WinsOverUnknownInTheOther(char blocker, BlockReason expected)
    {
        BlockGridSnapshot s = FakeGrid.FromLayers(
        [
            ["##", "##"],
            ["." + blocker, "?."],
            ["..", "?."],
        ]);
        var to = new GridPos(1, 1, 1);
        Assert.Equal(StandKind.Ground, Standability.Classify(s, to));

        MoveCost move = Defaults.Evaluate(s, new GridPos(0, 1, 0), to);

        Assert.Equal(MoveVerdict.Blocked, move.Verdict);
        Assert.Equal(expected, move.Reason);
    }

    [Fact]
    public void DiagonalSweep_UnknownCornerAlone_IsUnknown()
    {
        BlockGridSnapshot s = FakeGrid.FromLayers([["##", "##"], ["..", "?."], ["..", "?."]]);
        Assert.Equal(MoveVerdict.Unknown, Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 1, 1)).Verdict);
    }

    // Climb (0,1,0) -> (1,2,0): the jump needs the source column clear up to y = 3 (one above the head).
    [Theory]
    [InlineData('L', MoveVerdict.Blocked, BlockReason.Lava)]
    [InlineData('!', MoveVerdict.Blocked, BlockReason.Hazard)]
    [InlineData('.', MoveVerdict.Ok, BlockReason.None)]
    public void StraightClimb_LavaAboveTheSourceHead_IsBlocked(char aboveSourceHead, MoveVerdict verdict, BlockReason reason)
    {
        BlockGridSnapshot s = FakeGrid.FromLayers([["##"], [".#"], [".."], [aboveSourceHead + "."], [".."]]);
        MoveCost move = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 2, 0));
        Assert.Equal((verdict, reason), (move.Verdict, move.Reason));
    }

    // Drop (0,2,0) -> (1,1,0): falling in needs the destination column clear up to y = 3 (one above the head).
    [Theory]
    [InlineData('L', MoveVerdict.Blocked, BlockReason.Lava)]
    [InlineData('!', MoveVerdict.Blocked, BlockReason.Hazard)]
    [InlineData('.', MoveVerdict.Ok, BlockReason.None)]
    public void StraightDrop_LavaAboveTheDestinationHead_IsBlocked(char aboveDestHead, MoveVerdict verdict, BlockReason reason)
    {
        BlockGridSnapshot s = FakeGrid.FromLayers([["##"], ["#."], [".."], ["." + aboveDestHead], [".."]]);
        var to = new GridPos(1, 1, 0);
        Assert.Equal(StandKind.Ground, Standability.Classify(s, to));
        MoveCost move = Defaults.Evaluate(s, new GridPos(0, 2, 0), to);
        Assert.Equal((verdict, reason), (move.Verdict, move.Reason));
    }

    [Fact]
    public void StraightClimb_LavaInTheSweep_WinsOverAnUnknownDestinationHead()
    {
        // Destination head (1,3,0) is Unknown, so the destination itself classifies as Unknown; the lava at (0,3,0)
        // in the source column's jump clearance must still make the move Blocked, not Unknown.
        BlockGridSnapshot s = FakeGrid.FromLayers([["##"], [".#"], [".."], ["L?"], [".."]]);
        Assert.Equal(StandKind.Unknown, Standability.Classify(s, new GridPos(1, 2, 0)));
        MoveCost move = Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(1, 2, 0));
        Assert.Equal((MoveVerdict.Blocked, BlockReason.Lava), (move.Verdict, move.Reason));
    }

    [Fact]
    public void Evaluate_RejectsNonNeighbourMoves()
    {
        BlockGridSnapshot s = FakeGrid.Flat(4, 1);
        Assert.Throws<ArgumentException>(() => Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(2, 1, 0)));
        Assert.Throws<ArgumentException>(() => Defaults.Evaluate(s, new GridPos(0, 1, 0), new GridPos(0, 2, 0)));
    }

    [Theory]
    [InlineData(-1, 1, 1, 3, 5)]
    [InlineData(4, -0.5, 1, 3, 5)]
    [InlineData(4, 1, -1, 3, 5)]
    [InlineData(4, 1, 1, -1, 5)]
    [InlineData(4, 1, 1, 3, 0.5)]
    [InlineData(double.NaN, 1, 1, 3, 5)]
    [InlineData(double.PositiveInfinity, 1, 1, 3, 5)]
    public void Options_RejectInvalidValues(double up, double down, int maxClimb, int maxDrop, double water) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimbCostModel(new ClimbCostOptions
        {
            UphillPenalty = up, DownhillPenalty = down, MaxClimb = maxClimb, MaxDrop = maxDrop, WaterCost = water,
        }));
}
