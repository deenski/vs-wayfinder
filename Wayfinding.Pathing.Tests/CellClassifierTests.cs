using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Tests;

public class CellClassifierTests
{
    [Fact]
    public void Unreadable_IsUnknown_NotAir()
    {
        var sample = new CellSample(false, ObservedFluid.None, BlockId: 0, TraversalCost: 0, HasCollision: false);
        Assert.False(CellClassifier.TryClassify(sample, out BlockCell cell));
        Assert.Equal(BlockCell.Unknown, cell);
        Assert.NotEqual(CellClass.Air, cell.Class);
    }

    [Fact]
    public void Classification_FollowsTheContractOrder()
    {
        Assert.Equal(CellClass.Lava, Classify(Fluid: ObservedFluid.Lava, BlockId: 9, TraversalCost: 99999, HasCollision: true));
        Assert.Equal(CellClass.Hazard, Classify(Fluid: ObservedFluid.Water, BlockId: 4, TraversalCost: 10000, HasCollision: false));
        Assert.Equal(CellClass.Water, Classify(Fluid: ObservedFluid.Water, BlockId: 4, TraversalCost: 5, HasCollision: false));
        Assert.Equal(CellClass.Hazard, Classify(Fluid: ObservedFluid.None, BlockId: 12, TraversalCost: 10000, HasCollision: true));
        Assert.Equal(CellClass.Walkable, Classify(Fluid: ObservedFluid.None, BlockId: 7, TraversalCost: 9999, HasCollision: false));
        Assert.Equal(CellClass.Air, Classify(Fluid: ObservedFluid.None, BlockId: 0, TraversalCost: 0, HasCollision: false));
        Assert.Equal(CellClass.Solid, Classify(Fluid: ObservedFluid.None, BlockId: 3, TraversalCost: 0, HasCollision: true));
        Assert.Equal(CellClass.Solid, Classify(Fluid: ObservedFluid.None, BlockId: 0, TraversalCost: 0, HasCollision: true));
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(1, 10, 32768 + 10)]
    [InlineData(2, -4, 65536 - 4)]
    [InlineData(-1, 3, -32768 + 3)]
    public void DimensionY_RoundTrips(int dimension, int y, int internalY)
    {
        Assert.Equal(internalY, DimensionY.ToInternal(dimension, y));
        Assert.Equal(y, DimensionY.ToInDimension(dimension, internalY));
        Assert.Equal(32768, DimensionY.Boundary);
    }

    [Fact]
    public void FloorDiv_MatchesTruncationForNonNegative_AndFloorsNegatives()
    {
        Assert.Equal(1, DimensionY.FloorDiv(32, 32));
        Assert.Equal(0, DimensionY.FloorDiv(31, 32));
        Assert.Equal(-1, DimensionY.FloorDiv(-1, 32));
        Assert.Equal(-1, DimensionY.FloorDiv(-32, 32));
    }

    private static CellClass Classify(ObservedFluid Fluid, int BlockId, float TraversalCost, bool HasCollision)
    {
        var sample = new CellSample(true, Fluid, BlockId, TraversalCost, HasCollision);
        Assert.True(CellClassifier.TryClassify(sample, out BlockCell cell));
        return cell.Class;
    }
}
