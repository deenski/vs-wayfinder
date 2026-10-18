using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class FakeGridTests
{
    [Fact]
    public void Layers_AreBottomUp_RowsAreZ_CharsAreX()
    {
        BlockGridSnapshot s = FakeGrid.FromLayers(
        [
            ["##C", "#~L"],   // y = 0
            ["..,", "?!."],   // y = 1
        ], origin: new GridPos(100, 50, -10), dimension: 2);

        Assert.Equal((3, 2, 2), (s.SizeX, s.SizeY, s.SizeZ));
        Assert.Equal(2, s.Dimension);
        Assert.Equal(CellClass.Solid, s.GetClass(100, 50, -10));
        Assert.Equal(new BlockCell(CellClass.Solid, CellFlags.BuildForbidden), s.GetCell(102, 50, -10));
        Assert.Equal(CellClass.Water, s.GetClass(101, 50, -9));
        Assert.Equal(CellClass.Lava, s.GetClass(102, 50, -9));
        Assert.Equal(CellClass.Walkable, s.GetClass(102, 51, -10));
        Assert.Equal(CellClass.Unknown, s.GetClass(100, 51, -9));
        Assert.Equal(CellClass.Hazard, s.GetClass(101, 51, -9));
    }

    [Fact]
    public void Layers_RejectRaggedArtAndUnknownCharacters()
    {
        Assert.Throws<ArgumentException>(() => FakeGrid.FromLayers([["##", "#"]]));
        Assert.Throws<ArgumentException>(() => FakeGrid.FromLayers([["##"], ["##", "##"]]));
        Assert.Throws<ArgumentException>(() => FakeGrid.FromLayers([["#Q"]]));
    }

    [Fact]
    public void Heightmap_ColumnsMatchTheirCharacters()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["02a", "~L!", "#?0"], airAbove: 3);

        Assert.Equal(10 + 1 + 3, s.SizeY);
        Assert.Equal(CellClass.Solid, s.GetClass(0, 0, 0));
        Assert.Equal(CellClass.Air, s.GetClass(0, 1, 0));
        Assert.Equal(CellClass.Solid, s.GetClass(1, 2, 0));
        Assert.Equal(CellClass.Air, s.GetClass(1, 3, 0));
        Assert.Equal(CellClass.Solid, s.GetClass(2, 10, 0));
        Assert.Equal(CellClass.Water, s.GetClass(0, 1, 1));
        Assert.Equal(CellClass.Lava, s.GetClass(1, 1, 1));
        Assert.Equal(CellClass.Hazard, s.GetClass(2, 1, 1));
        Assert.Equal(CellClass.Solid, s.GetClass(0, s.SizeY - 1, 2));
        Assert.Equal(CellClass.Unknown, s.GetClass(1, 0, 2));

        Assert.Equal(StandKind.Ground, Standability.Classify(s, new GridPos(0, 1, 0)));
        Assert.Equal(StandKind.Ground, Standability.Classify(s, new GridPos(2, 11, 0)));
        Assert.Equal(StandKind.Swim, Standability.Classify(s, new GridPos(0, 1, 1)));
        Assert.Equal(StandKind.Dangerous, Standability.Classify(s, new GridPos(1, 1, 1)));
        Assert.Equal(StandKind.Unknown, Standability.Classify(s, new GridPos(1, 1, 2)));
    }
}
