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
    [InlineData(1f, (byte)8)]
    [InlineData(0.5f, (byte)4)]
    [InlineData(0.125f, (byte)1)]
    [InlineData(0f, (byte)0)]
    [InlineData(-0.25f, (byte)0)]
    [InlineData(40f, (byte)255)]
    public void StepHeight_QuantizesMaxCollisionY2ToEighths(float y2, byte expected)
    {
        var sample = new CellSample(true, ObservedFluid.None, BlockId: 3, TraversalCost: 0, HasCollision: true, MaxCollisionY2: y2);
        Assert.True(CellClassifier.TryClassify(sample, out BlockCell cell));
        Assert.Equal(CellClass.Solid, cell.Class);
        Assert.Equal(expected, cell.StepHeight);
        Assert.Equal(expected, CellClassifier.QuantizeStepHeight(y2));
    }

    [Fact]
    public void Slab_StaysSolid_AndRecordsHalfHeight()
    {
        var sample = new CellSample(true, ObservedFluid.None, BlockId: 11, TraversalCost: 0, HasCollision: true, MaxCollisionY2: 0.5f);
        Assert.True(CellClassifier.TryClassify(sample, out BlockCell cell));
        Assert.Equal(CellClass.Solid, cell.Class);
        Assert.Equal((byte)4, cell.StepHeight);
    }

    [Fact]
    public void NoCollision_LeavesStepHeightUnmeasured()
    {
        var air = new CellSample(true, ObservedFluid.None, BlockId: 0, TraversalCost: 0, HasCollision: false, MaxCollisionY2: 1f);
        Assert.True(CellClassifier.TryClassify(air, out BlockCell cell));
        Assert.Equal(CellClass.Air, cell.Class);
        Assert.Null(cell.StepHeight);
        Assert.Null(BlockCell.Unknown.StepHeight);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(1, 10, 32768 + 10)]
    [InlineData(2, 4, 65536 + 4)]
    [InlineData(-1, 3, -32768 + 3)]
    public void DimensionY_RoundTrips(int dimension, int y, int internalY)
    {
        Assert.Equal(internalY, DimensionY.ToInternal(dimension, y));
        Assert.Equal(y, DimensionY.ToInDimension(dimension, internalY));
        Assert.Equal(32768, DimensionY.Boundary);
    }

    [Fact]
    public void OutOfRangeY_IsOutsideTheDimension()
    {
        Assert.False(DimensionY.ContainsInDimensionY(-1));
        Assert.False(DimensionY.ContainsInDimensionY(DimensionY.Boundary));
        Assert.True(DimensionY.ContainsInDimensionY(0));
        Assert.True(DimensionY.ContainsInDimensionY(DimensionY.Boundary - 1));
        Assert.False(DimensionY.ContainsChunkY(-1, 32));
        Assert.False(DimensionY.ContainsChunkY(1024, 32));
        Assert.True(DimensionY.ContainsChunkY(0, 32));
        Assert.True(DimensionY.ContainsChunkY(1023, 32));
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
