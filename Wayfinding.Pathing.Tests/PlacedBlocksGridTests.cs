using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class PlacedBlocksGridTests
{
    [Fact]
    public void PlacedCell_IsAFullSolidBlock()
    {
        IBlockGrid inner = FakeGrid.FromLayers([["."]]);
        var placed = new PlacedBlocksGrid(inner, 1);
        placed.Add(new GridPos(0, 0, 0));

        BlockCell cell = placed.GetCell(0, 0, 0);
        Assert.Equal(CellClass.Solid, cell.Class);
        Assert.Equal((byte)8, cell.StepHeight);
        Assert.Equal(CellClass.Air, inner.GetCell(0, 0, 0).Class);
    }
}
