using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class SnapshotFillTests
{
    [Fact]
    public void UnknownCells_AreNeverCopiedAsAir()
    {
        BlockGridSnapshot source = FakeGrid.FromLayers(
        [
            ["..#", "?.#"],
        ]);
        BlockGridSnapshot snapshot = Fill(source, budgetReads: int.MaxValue, out _, out _);

        Assert.Equal(CellClass.Air, snapshot.GetClass(0, 0, 0));
        Assert.Equal(CellClass.Air, snapshot.GetClass(1, 0, 0));
        Assert.Equal(CellClass.Solid, snapshot.GetClass(2, 0, 0));
        Assert.Equal(CellClass.Unknown, snapshot.GetClass(0, 0, 1));
        Assert.NotEqual(CellClass.Air, snapshot.GetClass(0, 0, 1));
        Assert.Equal(CellClass.Air, snapshot.GetClass(1, 0, 1));
        Assert.Equal(CellClass.Solid, snapshot.GetClass(2, 0, 1));
        Assert.Equal(source.GetCell(0, 0, 1), BlockCell.Unknown);
    }

    [Fact]
    public void ChunkThatDoesNotOpen_StaysUnknown_NeverAir()
    {
        BlockGridSnapshot source = FakeGrid.Flat(2, 2);
        Assert.Equal(CellClass.Air, source.GetClass(0, 1, 0));

        var terrain = new RecordingTerrain(source, openChunks: false);
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ, source.Dimension);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);
        SnapshotSlice slice = fill.Advance(terrain, TimeSpan.FromSeconds(1));

        Assert.True(slice.Completed);
        Assert.Equal(0, slice.CellsVisited);
        Assert.Equal([ChunkReadStep.UnpackReadOnly], terrain.Log);
        Assert.Equal(0, terrain.Locks);
        Assert.Equal(CellClass.Unknown, builder.Build().GetClass(0, 1, 0));
    }

    [Fact]
    public void Advance_ResumesAfterTheBudget_WithoutTurningTheRestIntoAir()
    {
        // X fastest, then Z. The sixth cell (x=1, z=1) is air in the source.
        // The first slice stops before reading it, so it must stay Unknown, not Air.
        BlockGridSnapshot source = FakeGrid.FromLayers(
        [
            ["####", "#...", "....", "...."],
        ]);
        Assert.Equal(CellClass.Air, source.GetClass(1, 0, 1));

        long now = 0;
        int reads = 0;
        var terrain = new RecordingTerrain(source)
        {
            OnRead = () =>
            {
                reads++;
                if (reads == 5)
                {
                    now = 10_000;
                }
            },
        };
        var stats = new StatsSink();
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);

        SnapshotSlice first = fill.Advance(terrain, TimeSlicedSnapshotFill.DefaultBudget, stats, () => now, timestampFrequency: 1_000);
        Assert.False(first.Completed);
        Assert.True(first.BudgetExhausted);
        Assert.Equal(5, first.CellsVisited);
        Assert.Equal(10_000, first.ElapsedMilliseconds);
        Assert.False(terrain.IsOpen);
        Assert.Equal(ChunkReadStep.ReleaseLock, terrain.Log[^1]);
        Assert.Equal(CellClass.Solid, builder.GetClass(0, 0, 1));
        Assert.Equal(CellClass.Unknown, builder.GetClass(1, 0, 1));
        Assert.NotEqual(CellClass.Air, builder.GetClass(1, 0, 1));

        SnapshotSlice second = fill.Advance(terrain, TimeSlicedSnapshotFill.DefaultBudget, stats, () => now, timestampFrequency: 1_000);
        Assert.True(second.Completed);
        Assert.False(second.BudgetExhausted);
        Assert.Equal(0, second.ElapsedMilliseconds);
        BlockGridSnapshot snapshot = builder.Build();
        AssertEqual(source, snapshot);
        Assert.Equal(CellClass.Air, snapshot.GetClass(1, 0, 1));
        Assert.Equal([10_000, 0], stats.Samples);
        Assert.Equal(
            [ChunkReadStep.UnpackReadOnly, ChunkReadStep.AcquireLock],
            terrain.Log.Take(2));
    }

    [Fact]
    public void Fill_ReleasesTheLockWhenAReadThrows()
    {
        BlockGridSnapshot source = FakeGrid.FromLayers([["##"]]);
        var terrain = new RecordingTerrain(source)
        {
            OnRead = () => throw new InvalidOperationException("boom"),
        };
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);

        Assert.Throws<InvalidOperationException>(() => fill.Advance(terrain, TimeSpan.FromSeconds(1)));
        Assert.False(terrain.IsOpen);
        Assert.Equal(0, terrain.Locks);
        Assert.Equal(ChunkReadStep.ReleaseLock, terrain.Log[^1]);
        Assert.Equal(CellClass.Unknown, builder.GetClass(0, 0, 0));
    }

    [Fact]
    public void NegativeOrigin_CopiesTheFakeGrid()
    {
        var origin = new GridPos(-2, -3, 5);
        BlockGridSnapshot source = FakeGrid.FromLayers([["#.", ".~"]], origin, dimension: 1);
        BlockGridSnapshot snapshot = Fill(source, int.MaxValue, out _, out _);
        Assert.Equal(1, snapshot.Dimension);
        AssertEqual(source, snapshot);
    }

    private static BlockGridSnapshot Fill(BlockGridSnapshot source, int budgetReads, out RecordingTerrain terrain, out StatsSink stats)
    {
        int reads = 0;
        terrain = new RecordingTerrain(source)
        {
            OnRead = () =>
            {
                reads++;
            },
        };
        stats = new StatsSink();
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ, source.Dimension);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);
        long now = 0;
        SnapshotSlice slice = fill.Advance(
            terrain,
            TimeSpan.FromMilliseconds(2),
            stats,
            () =>
            {
                if (reads >= budgetReads)
                {
                    now = 10_000;
                }

                return now;
            },
            timestampFrequency: 1_000);
        Assert.True(slice.Completed);
        return builder.Build();
    }

    private static void AssertEqual(IBlockGrid expected, IBlockGrid actual)
    {
        var snapshot = Assert.IsType<BlockGridSnapshot>(expected);
        for (int y = 0; y < snapshot.SizeY; y++)
        {
            for (int z = 0; z < snapshot.SizeZ; z++)
            {
                for (int x = 0; x < snapshot.SizeX; x++)
                {
                    int wx = snapshot.Origin.X + x;
                    int wy = snapshot.Origin.Y + y;
                    int wz = snapshot.Origin.Z + z;
                    Assert.Equal(expected.GetCell(wx, wy, wz), actual.GetCell(wx, wy, wz));
                }
            }
        }
    }

    private sealed class StatsSink : ISnapshotFrameStats
    {
        public List<double> Samples { get; } = [];

        public void RecordSnapshotMs(double milliseconds) => Samples.Add(milliseconds);
    }

    private sealed class RecordingTerrain : ITerrainSlice
    {
        private readonly IBlockGrid _grid;
        private readonly Probe _probe = new();
        private readonly StepLog _log = new();

        public RecordingTerrain(IBlockGrid grid, bool openChunks = true)
        {
            _grid = grid;
            _probe.Readable = openChunks;
        }

        public int ChunkSize => 32;

        public List<ChunkReadStep> Log => _log.Steps;

        public int Locks => _probe.Locks;

        public bool IsOpen { get; private set; }

        public Action? OnRead { get; init; }

        public bool TryOpenChunk(int chunkX, int chunkY, int chunkZ)
        {
            bool opened = ChunkReadProtocol.TryOpen(_probe, _log);
            IsOpen = opened;
            return opened;
        }

        public void CloseChunk()
        {
            if (!IsOpen)
            {
                return;
            }

            ChunkReadProtocol.Close(_probe, _log);
            IsOpen = false;
        }

        public bool TryReadCell(int x, int y, int z, out BlockCell cell)
        {
            ChunkReadProtocol.Read(_log);
            OnRead?.Invoke();
            cell = _grid.GetCell(x, y, z);
            if (cell.Class == CellClass.Unknown)
            {
                cell = BlockCell.Unknown;
                return false;
            }

            return true;
        }

        private sealed class Probe : IChunkReader
        {
            public bool Readable { get; set; }

            public int Locks { get; private set; }

            public bool UnpackReadOnly() => Readable;

            public void AcquireLock() => Locks++;

            public void ReleaseLock() => Locks--;
        }

        private sealed class StepLog : IChunkReadLog
        {
            public List<ChunkReadStep> Steps { get; } = [];

            public void Record(ChunkReadStep step) => Steps.Add(step);
        }
    }
}
