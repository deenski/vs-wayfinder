using System.Diagnostics;

namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Reads one region of terrain into a <see cref="BlockGridSnapshotBuilder"/> a few milliseconds at a time.
/// </summary>
/// <remarks>
/// The cursor walks chunk by chunk so the game adapter can unpack and lock one chunk, read cells with
/// <c>GetBlockIdUnsafe</c>, and release the lock before the slice returns. A chunk that does not open
/// (unloaded, disposed, packed, or <c>GetBlockOrNull</c> returned null) is left untouched, so those cells
/// stay <see cref="CellClass.Unknown"/> and are never written as <see cref="CellClass.Air"/>.
/// Y coordinates passed to <see cref="ITerrainSlice"/> are in-dimension.
/// </remarks>
public interface ITerrainSlice
{
    /// <summary>Edge length of a chunk in blocks. Vintage Story uses 32.</summary>
    int ChunkSize { get; }

    /// <summary>
    /// Opens the chunk that contains the given chunk coordinates (floor of block / <see cref="ChunkSize"/>,
    /// in-dimension for Y). False when the chunk must stay unknown.
    /// </summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y in-dimension, not including <c>dimension * 1024</c>.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <returns>True when the chunk is open and <see cref="CloseChunk"/> must run.</returns>
    bool TryOpenChunk(int chunkX, int chunkY, int chunkZ);

    /// <summary>Releases the chunk opened by <see cref="TryOpenChunk"/>.</summary>
    void CloseChunk();

    /// <summary>
    /// Reads one cell of the open chunk.
    /// </summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">In-dimension block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <param name="cell">The classified cell when this returns true.</param>
    /// <returns>False when the cell was not actually read. The caller must not write it.</returns>
    bool TryReadCell(int x, int y, int z, out BlockCell cell);
}

/// <summary>What one call to <see cref="TimeSlicedSnapshotFill.Advance"/> did.</summary>
/// <param name="Completed">True when every chunk of the box has been visited.</param>
/// <param name="ElapsedMilliseconds">Main-thread time spent inside this call.</param>
/// <param name="CellsVisited">Cells read during this call. Cells in a chunk that did not open are not counted; they stay unknown.</param>
/// <param name="BudgetExhausted">True when the call stopped because the time budget ran out with work remaining.</param>
public readonly record struct SnapshotSlice(
    bool Completed,
    double ElapsedMilliseconds,
    int CellsVisited,
    bool BudgetExhausted);

/// <summary>
/// Fills a <see cref="BlockGridSnapshotBuilder"/> across ticks, staying inside a time budget on each call.
/// </summary>
public sealed class TimeSlicedSnapshotFill
{
    /// <summary>Main-thread budget per tick. Design §8.2: at or under about 2 ms per frame.</summary>
    public const double DefaultBudgetMilliseconds = 2;

    /// <summary>The same budget as a <see cref="TimeSpan"/>.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(DefaultBudgetMilliseconds);

    private readonly BlockGridSnapshotBuilder _builder;
    private readonly int _chunkSize;
    private readonly int _originX;
    private readonly int _originY;
    private readonly int _originZ;
    private readonly int _sizeX;
    private readonly int _sizeY;
    private readonly int _sizeZ;
    private readonly int _chunkOriginX;
    private readonly int _chunkOriginY;
    private readonly int _chunkOriginZ;
    private readonly int _chunksX;
    private readonly int _chunksZ;
    private readonly long _chunkCount;
    private long _chunkCursor;
    private int _cellCursor;

    /// <summary>Creates a fill over every cell of <paramref name="builder"/>.</summary>
    /// <param name="builder">The snapshot to write. Not built yet.</param>
    /// <param name="chunkSize">Chunk edge in blocks. Must match <see cref="ITerrainSlice.ChunkSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="builder"/> was already built.</exception>
    public TimeSlicedSnapshotFill(BlockGridSnapshotBuilder builder, int chunkSize = 32)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if (builder.IsBuilt)
        {
            throw new InvalidOperationException("The builder was already finished.");
        }

        _builder = builder;
        _chunkSize = chunkSize;
        _originX = builder.Origin.X;
        _originY = builder.Origin.Y;
        _originZ = builder.Origin.Z;
        _sizeX = builder.SizeX;
        _sizeY = builder.SizeY;
        _sizeZ = builder.SizeZ;
        _chunkOriginX = DimensionY.FloorDiv(_originX, chunkSize);
        _chunkOriginY = DimensionY.FloorDiv(_originY, chunkSize);
        _chunkOriginZ = DimensionY.FloorDiv(_originZ, chunkSize);
        int lastX = DimensionY.FloorDiv(_originX + _sizeX - 1, chunkSize);
        int lastY = DimensionY.FloorDiv(_originY + _sizeY - 1, chunkSize);
        int lastZ = DimensionY.FloorDiv(_originZ + _sizeZ - 1, chunkSize);
        _chunksX = lastX - _chunkOriginX + 1;
        int chunksY = lastY - _chunkOriginY + 1;
        _chunksZ = lastZ - _chunkOriginZ + 1;
        _chunkCount = (long)_chunksX * chunksY * _chunksZ;
    }

    /// <summary>True after a call to <see cref="Advance"/> has visited every chunk.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Chunks in the box, including ones that did not open.</summary>
    public long ChunkCount => _chunkCount;

    /// <summary>
    /// Reads cells until <paramref name="budget"/> elapses or the box is finished.
    /// </summary>
    /// <param name="terrain">The chunk source. The game adapter is one; tests use a fake.</param>
    /// <param name="budget">Time allowed for this call. A non-positive budget still reads one cell so the cursor moves.</param>
    /// <param name="stats">Receives the elapsed milliseconds. May be null.</param>
    /// <param name="timestamp">
    /// Tick source. Null uses <see cref="Stopwatch.GetTimestamp"/>. Tests pass a clock that jumps
    /// once the budget should expire, which is how a resume is tested without waiting on the wall clock.
    /// </param>
    /// <param name="timestampFrequency">
    /// Ticks per second of <paramref name="timestamp"/>. Null uses <see cref="Stopwatch.Frequency"/>.
    /// </param>
    /// <returns>What this call did.</returns>
    public SnapshotSlice Advance(
        ITerrainSlice terrain,
        TimeSpan budget,
        ISnapshotFrameStats? stats = null,
        Func<long>? timestamp = null,
        long? timestampFrequency = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (terrain.ChunkSize != _chunkSize)
        {
            throw new ArgumentException("The terrain chunk size does not match this fill.", nameof(terrain));
        }

        if (_builder.IsBuilt)
        {
            throw new InvalidOperationException("The builder was already finished.");
        }

        long frequency = timestampFrequency ?? Stopwatch.Frequency;
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        Func<long> now = timestamp ?? Stopwatch.GetTimestamp;
        long start = now();
        long budgetTicks = budget <= TimeSpan.Zero ? 0 : (long)(budget.TotalSeconds * frequency);
        if (budgetTicks < 0)
        {
            budgetTicks = 0;
        }

        int visited = 0;
        int worked = 0;
        bool exhausted = false;
        if (!IsComplete)
        {
            bool stop = false;
            while (!stop && _chunkCursor < _chunkCount)
            {
                if (worked > 0 && now() - start >= budgetTicks)
                {
                    exhausted = true;
                    break;
                }

                worked++;
                ChunkAt(_chunkCursor, out int chunkX, out int chunkY, out int chunkZ);
                Overlap(_originX, _sizeX, chunkX, out int x0, out int dx);
                Overlap(_originY, _sizeY, chunkY, out int y0, out int dy);
                Overlap(_originZ, _sizeZ, chunkZ, out int z0, out int dz);
                int cells = dx * dy * dz;
                if (_cellCursor >= cells)
                {
                    _chunkCursor++;
                    _cellCursor = 0;
                    continue;
                }

                if (!terrain.TryOpenChunk(chunkX, chunkY, chunkZ))
                {
                    // Unloaded, disposed, packed, or GetBlockOrNull returned null.
                    // Leave every cell in the chunk unknown. Do not write air.
                    _chunkCursor++;
                    _cellCursor = 0;
                    continue;
                }

                try
                {
                    while (_cellCursor < cells)
                    {
                        if (visited > 0 && now() - start >= budgetTicks)
                        {
                            exhausted = true;
                            stop = true;
                            break;
                        }

                        Decode(_cellCursor, dx, dz, x0, y0, z0, out int x, out int y, out int z);
                        if (terrain.TryReadCell(x, y, z, out BlockCell cell))
                        {
                            _builder.Set(x, y, z, cell);
                        }

                        _cellCursor++;
                        visited++;
                    }
                }
                finally
                {
                    terrain.CloseChunk();
                }

                if (stop)
                {
                    break;
                }

                _chunkCursor++;
                _cellCursor = 0;
            }

            if (!stop && _chunkCursor >= _chunkCount)
            {
                IsComplete = true;
            }
        }

        double elapsedMs = (now() - start) * 1000.0 / frequency;
        stats?.RecordSnapshotMs(elapsedMs);
        return new SnapshotSlice(IsComplete, elapsedMs, visited, exhausted);
    }

    private void ChunkAt(long index, out int chunkX, out int chunkY, out int chunkZ)
    {
        int plane = _chunksX * _chunksZ;
        int iy = (int)(index / plane);
        int rem = (int)(index % plane);
        int iz = rem / _chunksX;
        int ix = rem % _chunksX;
        chunkX = _chunkOriginX + ix;
        chunkZ = _chunkOriginZ + iz;
        chunkY = _chunkOriginY + iy;
    }

    private void Overlap(int origin, int size, int chunk, out int start, out int count)
    {
        long chunkStart = (long)chunk * _chunkSize;
        long chunkEnd = chunkStart + _chunkSize - 1;
        long boxStart = origin;
        long boxEnd = (long)origin + size - 1;
        long from = Math.Max(chunkStart, boxStart);
        long to = Math.Min(chunkEnd, boxEnd);
        start = (int)from;
        count = (int)(to - from + 1);
    }

    private static void Decode(int index, int dx, int dz, int x0, int y0, int z0, out int x, out int y, out int z)
    {
        int lx = index % dx;
        int tmp = index / dx;
        int lz = tmp % dz;
        int ly = tmp / dz;
        x = x0 + lx;
        z = z0 + lz;
        y = y0 + ly;
    }
}
