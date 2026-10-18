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
    public void Fill_CopiesStepHeight()
    {
        var sourceBuilder = new BlockGridSnapshotBuilder(default, 1, 1, 1);
        sourceBuilder.Set(0, 0, 0, new BlockCell(CellClass.Solid, CellFlags.None, 4));
        BlockGridSnapshot source = sourceBuilder.Build();

        BlockGridSnapshot snapshot = Fill(source, int.MaxValue, out _, out _);
        Assert.Equal(new BlockCell(CellClass.Solid, CellFlags.None, 4), snapshot.GetCell(0, 0, 0));
    }

    [Fact]
    public void NegativeY_InDimension1_StaysUnknown()
    {
        var origin = new GridPos(0, -1, 0);
        var sourceBuilder = new BlockGridSnapshotBuilder(origin, 1, 2, 1, dimension: 1);
        sourceBuilder.Set(0, -1, 0, CellClass.Air);
        sourceBuilder.Set(0, 0, 0, CellClass.Air);
        var terrain = new InDimensionGuard(new RecordingTerrain(sourceBuilder.Build()));
        var builder = new BlockGridSnapshotBuilder(origin, 1, 2, 1, dimension: 1);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);

        SnapshotSlice slice = fill.Advance(terrain, TimeSpan.FromSeconds(1));

        Assert.True(slice.Completed);
        Assert.Equal(1, slice.CellsVisited);
        BlockGridSnapshot snapshot = builder.Build();
        Assert.Equal(CellClass.Unknown, snapshot.GetClass(0, -1, 0));
        Assert.NotEqual(CellClass.Air, snapshot.GetClass(0, -1, 0));
        Assert.Equal(CellClass.Air, snapshot.GetClass(0, 0, 0));
        Assert.Equal(1, snapshot.Dimension);
    }

    [Fact]
    public void ThrowingGrid_DropsTheJob_AndTheNextTickDoesNotRethrow()
    {
        BlockGridSnapshot source = FakeGrid.FromLayers([["##"]]);
        var terrain = new RecordingTerrain(source)
        {
            OnRead = () => throw new InvalidOperationException("boom"),
        };
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ);
        var fill = new TimeSlicedSnapshotFill(builder, terrain.ChunkSize);
        Exception? logged = null;
        bool job = true;

        void Tick()
        {
            if (!job)
            {
                return;
            }

            bool keep = SnapshotBuildStep.TryAdvance(
                fill,
                terrain,
                builder,
                TimeSpan.FromSeconds(1),
                stats: null,
                out _,
                out Exception? error);
            if (error != null)
            {
                logged ??= error;
                job = false;
                return;
            }

            if (!keep)
            {
                job = false;
            }
        }

        Tick();
        Tick();

        Assert.False(job);
        Assert.NotNull(logged);
        Assert.Equal("boom", logged.Message);
        Assert.False(terrain.IsOpen);
        Assert.Equal(0, terrain.Locks);
        Assert.Equal(ChunkReadStep.ReleaseLock, terrain.Log[^1]);
        Assert.Equal(CellClass.Unknown, builder.GetClass(0, 0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void MultiChunkResume_MatchesOneShot_AndLeavesUnloadedUnknown(int readsPerSlice)
    {
        const int chunkSize = 4;
        var origin = new GridPos(-5, -3, -7);
        const int sizeX = 11;
        const int sizeY = 6;
        const int sizeZ = 13;
        BlockGridSnapshot source = Pattern(origin, sizeX, sizeY, sizeZ);
        BlockGridSnapshot oneShot = FillChunked(source, chunkSize, readsPerSlice: int.MaxValue, out Dictionary<GridPos, int> oneReads);
        BlockGridSnapshot sliced = FillChunked(source, chunkSize, readsPerSlice, out Dictionary<GridPos, int> slicedReads);

        AssertEqual(oneShot, sliced);
        Assert.Equal(oneReads.Count, slicedReads.Count);
        int loaded = 0;
        int unloaded = 0;
        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int wx = origin.X + x;
                    int wy = origin.Y + y;
                    int wz = origin.Z + z;
                    var pos = new GridPos(wx, wy, wz);
                    if (IsUnloaded(wx, wy, wz, chunkSize))
                    {
                        unloaded++;
                        Assert.Equal(CellClass.Unknown, sliced.GetClass(wx, wy, wz));
                        Assert.False(slicedReads.ContainsKey(pos));
                    }
                    else
                    {
                        loaded++;
                        Assert.Equal(source.GetCell(wx, wy, wz), sliced.GetCell(wx, wy, wz));
                        Assert.Equal(1, slicedReads[pos]);
                        Assert.Equal(1, oneReads[pos]);
                    }
                }
            }
        }

        Assert.True(loaded > 0);
        Assert.True(unloaded > 0);
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

    private static BlockGridSnapshot Pattern(GridPos origin, int sizeX, int sizeY, int sizeZ)
    {
        var builder = new BlockGridSnapshotBuilder(origin, sizeX, sizeY, sizeZ);
        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int wx = origin.X + x;
                    int wy = origin.Y + y;
                    int wz = origin.Z + z;
                    CellClass cls = ((wx + wy + wz) & 1) == 0 ? CellClass.Solid : CellClass.Air;
                    builder.Set(wx, wy, wz, cls);
                }
            }
        }

        return builder.Build();
    }

    private static bool IsUnloaded(int x, int y, int z, int chunkSize)
    {
        int cx = DimensionY.FloorDiv(x, chunkSize);
        int cy = DimensionY.FloorDiv(y, chunkSize);
        int cz = DimensionY.FloorDiv(z, chunkSize);
        return (cx + cy + cz) % 4 == 0;
    }

    private static BlockGridSnapshot FillChunked(
        BlockGridSnapshot source,
        int chunkSize,
        int readsPerSlice,
        out Dictionary<GridPos, int> reads)
    {
        var terrain = new ChunkWindowTerrain(source, chunkSize);
        var builder = new BlockGridSnapshotBuilder(source.Origin, source.SizeX, source.SizeY, source.SizeZ, source.Dimension);
        var fill = new TimeSlicedSnapshotFill(builder, chunkSize);
        long now = 0;
        int seen = 0;
        terrain.OnRead = () =>
        {
            seen++;
            if (readsPerSlice < int.MaxValue && seen % readsPerSlice == 0)
            {
                now += 10_000;
            }
        };
        int guard = 0;
        while (!fill.IsComplete)
        {
            int before = terrain.Reads.Count;
            fill.Advance(
                terrain,
                TimeSlicedSnapshotFill.DefaultBudget,
                timestamp: () => now,
                timestampFrequency: 1_000);
            if (readsPerSlice < int.MaxValue && terrain.Reads.Count - before > readsPerSlice)
            {
                throw new InvalidOperationException("A slice read past its budget.");
            }

            if (++guard > 100_000)
            {
                throw new InvalidOperationException("The fill did not finish.");
            }
        }

        reads = terrain.Reads;
        return builder.Build();
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

    /// <summary>
    /// Applies the same in-dimension Y window as <c>GameTerrainSlice</c>: chunk Y and block Y outside
    /// <see cref="DimensionY.Boundary"/> stay unknown and are not read.
    /// </summary>
    private sealed class InDimensionGuard(ITerrainSlice inner) : ITerrainSlice
    {
        public int ChunkSize => inner.ChunkSize;

        public bool TryOpenChunk(int chunkX, int chunkY, int chunkZ)
        {
            if (!DimensionY.ContainsChunkY(chunkY, ChunkSize))
            {
                return false;
            }

            return inner.TryOpenChunk(chunkX, chunkY, chunkZ);
        }

        public void CloseChunk() => inner.CloseChunk();

        public bool TryReadCell(int x, int y, int z, out BlockCell cell)
        {
            if (!DimensionY.ContainsInDimensionY(y))
            {
                cell = default;
                return false;
            }

            return inner.TryReadCell(x, y, z, out cell);
        }
    }

    private sealed class ChunkWindowTerrain : ITerrainSlice
    {
        private readonly IBlockGrid _grid;
        private int _openX;
        private int _openY;
        private int _openZ;
        private bool _open;

        public ChunkWindowTerrain(IBlockGrid grid, int chunkSize)
        {
            _grid = grid;
            ChunkSize = chunkSize;
        }

        public int ChunkSize { get; }

        public Dictionary<GridPos, int> Reads { get; } = [];

        public Action? OnRead { get; set; }

        public bool TryOpenChunk(int chunkX, int chunkY, int chunkZ)
        {
            if (IsUnloaded(chunkX * ChunkSize, chunkY * ChunkSize, chunkZ * ChunkSize, ChunkSize))
            {
                return false;
            }

            _openX = chunkX;
            _openY = chunkY;
            _openZ = chunkZ;
            _open = true;
            return true;
        }

        public void CloseChunk() => _open = false;

        public bool TryReadCell(int x, int y, int z, out BlockCell cell)
        {
            if (!_open)
            {
                throw new InvalidOperationException("Read without an open chunk.");
            }

            int cx = DimensionY.FloorDiv(x, ChunkSize);
            int cy = DimensionY.FloorDiv(y, ChunkSize);
            int cz = DimensionY.FloorDiv(z, ChunkSize);
            if (cx != _openX || cy != _openY || cz != _openZ)
            {
                throw new InvalidOperationException($"Read ({x},{y},{z}) is outside the open chunk ({_openX},{_openY},{_openZ}).");
            }

            var pos = new GridPos(x, y, z);
            Reads[pos] = Reads.GetValueOrDefault(pos) + 1;
            OnRead?.Invoke();
            cell = _grid.GetCell(x, y, z);
            return cell.Class != CellClass.Unknown;
        }
    }
}
