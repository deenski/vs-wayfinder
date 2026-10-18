using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class SnapshotContractTests
{
    private static BlockGridSnapshot SmallBox(GridPos origin)
    {
        var b = new BlockGridSnapshotBuilder(origin, 4, 3, 5, dimension: 7);
        b.Fill(origin, origin.Offset(3, 0, 4), CellClass.Solid);
        b.Set(origin.Offset(1, 1, 1), CellClass.Water);
        return b.Build();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-100, -5, 2000)]
    [InlineData(int.MinValue, int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue - 4, int.MaxValue - 3, int.MaxValue - 5)]
    public void OutOfBounds_ReturnsUnknown_NeverThrows(int ox, int oy, int oz)
    {
        var origin = new GridPos(ox, oy, oz);
        BlockGridSnapshot s = SmallBox(origin);

        Assert.Equal(CellClass.Solid, s.GetClass(ox, oy, oz));
        Assert.Equal(CellClass.Water, s.GetClass(origin.Offset(1, 1, 1)));

        int[] probes = [int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue];
        foreach (int px in probes)
        foreach (int py in probes)
        foreach (int pz in probes)
        {
            CellClass c = s.GetClass(px, py, pz);
            if (!s.Contains(px, py, pz))
            {
                Assert.Equal(CellClass.Unknown, c);
                Assert.Equal(BlockCell.Unknown, s.GetCell(px, py, pz));
            }
        }

        // Just outside each face.
        if (ox > int.MinValue)
        {
            Assert.Equal(CellClass.Unknown, s.GetClass(ox - 1, oy, oz));
        }

        Assert.Equal(CellClass.Unknown, s.GetClass(origin.Offset(4, 0, 0)));
        Assert.Equal(CellClass.Unknown, s.GetClass(origin.Offset(0, 3, 0)));
        Assert.Equal(CellClass.Unknown, s.GetClass(origin.Offset(0, 0, 5)));
        Assert.Equal(7, s.Dimension);
    }

    [Fact]
    public void UnwrittenCells_AreUnknown_NotAir()
    {
        BlockGridSnapshot s = new BlockGridSnapshotBuilder(new GridPos(10, 20, 30), 2, 2, 2).Build();
        Assert.Equal(CellClass.Unknown, s.GetClass(10, 20, 30));
        Assert.Equal(CellClass.Unknown, default(CellClass));
        Assert.Equal(BlockCell.Unknown, default(BlockCell));
    }

    [Fact]
    public void GetClass_MatchesGetCell_AndFlagsRoundTrip()
    {
        var b = new BlockGridSnapshotBuilder(default, 3, 1, 1);
        b.Set(0, 0, 0, CellClass.Solid, CellFlags.BuildForbidden);
        b.Set(1, 0, 0, CellClass.Air);
        b.AddFlags(1, 0, 0, CellFlags.BuildForbidden);
        BlockGridSnapshot s = b.Build();

        Assert.True(s.HasFlags);
        Assert.Equal(new BlockCell(CellClass.Solid, CellFlags.BuildForbidden), s.GetCell(0, 0, 0));
        Assert.Equal(new BlockCell(CellClass.Air, CellFlags.BuildForbidden), s.GetCell(1, 0, 0));
        Assert.Equal(BlockCell.Unknown, s.GetCell(2, 0, 0));
        for (int x = -1; x <= 3; x++)
        {
            Assert.Equal(s.GetCell(x, 0, 0).Class, s.GetClass(x, 0, 0));
        }
    }

    [Fact]
    public void Builder_IsFinishedAfterBuild_AndRejectsOutOfBoxWrites()
    {
        var b = new BlockGridSnapshotBuilder(default, 2, 2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Set(2, 0, 0, CellClass.Air));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Set(-1, 0, 0, CellClass.Air));
        b.Build();
        Assert.True(b.IsBuilt);
        Assert.Throws<InvalidOperationException>(() => b.Set(0, 0, 0, CellClass.Air));
        Assert.Throws<InvalidOperationException>(() => b.Build());
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, -3)]
    public void Builder_RejectsBadSizes(int sx, int sy, int sz) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockGridSnapshotBuilder(default, sx, sy, sz));

    [Fact]
    public void Builder_RejectsBoxPastIntMax() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockGridSnapshotBuilder(new GridPos(int.MaxValue, 0, 0), 2, 1, 1));

    [Fact]
    public void Snapshot_IsSafeForConcurrentReads()
    {
        BlockGridSnapshot s = FakeGrid.FromHeightmap(["0123", "3210", "~L!#"]);
        CellClass expected = s.GetClass(1, 1, 0);
        Parallel.For(0, 64, _ =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                Assert.Equal(expected, s.GetClass(1, 1, 0));
                Assert.Equal(CellClass.Unknown, s.GetClass(-1, 1, 0));
            }
        });
    }
}
