namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Mutable writer for a <see cref="BlockGridSnapshot"/>. The game-side <c>SnapshotBuilder</c> (KAN-29) creates
/// one for the region it wants, writes cells over several frames on the main thread, then calls
/// <see cref="Build"/> once and hands the result to the planner thread.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Every cell starts as <see cref="CellClass.Unknown"/>. Only write cells whose chunk is loaded
/// and was actually read; leave the rest alone so they stay Unknown.</description></item>
/// <item><description>Coordinates are world block coordinates, the same as the snapshot's.</description></item>
/// <item><description>Writes outside the box throw <see cref="ArgumentOutOfRangeException"/> (a builder bug,
/// unlike reads, which never throw). Use <see cref="Contains"/> to clip first.</description></item>
/// <item><description>Not thread-safe: use one builder from one thread (the main thread).</description></item>
/// <item><description><see cref="Build"/> hands the arrays to the snapshot without copying, so the builder is
/// finished afterwards and any further write throws <see cref="InvalidOperationException"/>.</description></item>
/// </list>
/// </remarks>
public sealed class BlockGridSnapshotBuilder
{
    /// <summary>Largest number of cells one snapshot may hold (64 Mi cells = 64 MB of classes).</summary>
    public const long MaxCells = 64L * 1024 * 1024;

    private CellClass[]? _classes;
    private CellFlags[]? _flags;
    private byte?[]? _steps;
    private readonly int _strideZ;
    private readonly int _strideY;

    /// <summary>Creates a builder for a box of cells, all initially <see cref="CellClass.Unknown"/>.</summary>
    /// <param name="origin">World coordinate of the minimum corner of the box.</param>
    /// <param name="sizeX">Cells along X; at least 1.</param>
    /// <param name="sizeY">Cells along Y; at least 1.</param>
    /// <param name="sizeZ">Cells along Z; at least 1.</param>
    /// <param name="dimension">Game dimension id of the region (0 = normal world).</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is below 1, the box would exceed
    /// <see cref="MaxCells"/>, or the box would extend past <see cref="int.MaxValue"/> on an axis.</exception>
    public BlockGridSnapshotBuilder(GridPos origin, int sizeX, int sizeY, int sizeZ, int dimension = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeZ, 1);
        long cells = (long)sizeX * sizeY * sizeZ;
        if (cells > MaxCells)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeX), $"Snapshot of {cells} cells exceeds the maximum of {MaxCells}.");
        }

        if ((long)origin.X + sizeX - 1 > int.MaxValue || (long)origin.Y + sizeY - 1 > int.MaxValue || (long)origin.Z + sizeZ - 1 > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "The box must not extend past int.MaxValue on any axis.");
        }

        Origin = origin;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        Dimension = dimension;
        _strideZ = sizeX;
        _strideY = sizeX * sizeZ;
        _classes = new CellClass[cells]; // zero-filled == CellClass.Unknown
    }

    /// <summary>World coordinate of the minimum corner of the box.</summary>
    public GridPos Origin { get; }

    /// <summary>Cells along X.</summary>
    public int SizeX { get; }

    /// <summary>Cells along Y.</summary>
    public int SizeY { get; }

    /// <summary>Cells along Z.</summary>
    public int SizeZ { get; }

    /// <summary>Game dimension id the snapshot will report.</summary>
    public int Dimension { get; }

    /// <summary>True once <see cref="Build"/> has been called.</summary>
    public bool IsBuilt => _classes is null;

    /// <summary>True when the world coordinate lies inside the box.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    public bool Contains(int x, int y, int z)
    {
        long lx = (long)x - Origin.X;
        long ly = (long)y - Origin.Y;
        long lz = (long)z - Origin.Z;
        return (ulong)lx < (ulong)SizeX && (ulong)ly < (ulong)SizeY && (ulong)lz < (ulong)SizeZ;
    }

    /// <summary>Writes one cell.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <param name="cellClass">The class to store.</param>
    /// <param name="flags">Extra flags to store (replaces any previous flags of the cell).</param>
    /// <param name="stepHeight">
    /// Measured <see cref="BlockCell.StepHeight"/> in eighths, or null when this write does not measure one.
    /// Replaces any previous step height of the cell.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is outside the box.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> was already called.</exception>
    public BlockGridSnapshotBuilder Set(int x, int y, int z, CellClass cellClass, CellFlags flags = CellFlags.None, byte? stepHeight = null)
    {
        CellClass[] classes = Live();
        int i = Index(x, y, z);
        classes[i] = cellClass;
        if (flags != CellFlags.None)
        {
            (_flags ??= new CellFlags[classes.Length])[i] = flags;
        }
        else if (_flags is not null)
        {
            _flags[i] = CellFlags.None;
        }

        if (stepHeight is not null)
        {
            (_steps ??= new byte?[classes.Length])[i] = stepHeight;
        }
        else if (_steps is not null)
        {
            _steps[i] = null;
        }

        return this;
    }

    /// <summary>Writes one cell, including its flags and step height.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <param name="cell">The cell to store.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlockGridSnapshotBuilder Set(int x, int y, int z, BlockCell cell) =>
        Set(x, y, z, cell.Class, cell.Flags, cell.StepHeight);

    /// <summary>Writes one cell.</summary>
    /// <param name="pos">World block position.</param>
    /// <param name="cellClass">The class to store.</param>
    /// <param name="flags">Extra flags to store.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlockGridSnapshotBuilder Set(GridPos pos, CellClass cellClass, CellFlags flags = CellFlags.None) =>
        Set(pos.X, pos.Y, pos.Z, cellClass, flags);

    /// <summary>Adds flags to a cell without changing its class or its other flags.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <param name="flags">Flags to OR in.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlockGridSnapshotBuilder AddFlags(int x, int y, int z, CellFlags flags)
    {
        CellClass[] classes = Live();
        int i = Index(x, y, z);
        if (flags != CellFlags.None)
        {
            (_flags ??= new CellFlags[classes.Length])[i] |= flags;
        }

        return this;
    }

    /// <summary>
    /// Writes a vertical run of cells in one column, from <paramref name="yMin"/> to <paramref name="yMaxInclusive"/>.
    /// Convenient when copying a chunk column.
    /// </summary>
    /// <param name="x">Block X of the column.</param>
    /// <param name="z">Block Z of the column.</param>
    /// <param name="yMin">Lowest Y to write.</param>
    /// <param name="yMaxInclusive">Highest Y to write; nothing is written if it is below <paramref name="yMin"/>.</param>
    /// <param name="cellClass">The class to store.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlockGridSnapshotBuilder SetColumn(int x, int z, int yMin, int yMaxInclusive, CellClass cellClass)
    {
        for (long y = yMin; y <= yMaxInclusive; y++)
        {
            Set(x, (int)y, z, cellClass);
        }

        return this;
    }

    /// <summary>Writes every cell of an inclusive world-coordinate box, which must lie inside the snapshot box.</summary>
    /// <param name="min">Minimum corner, inclusive.</param>
    /// <param name="maxInclusive">Maximum corner, inclusive.</param>
    /// <param name="cellClass">The class to store.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlockGridSnapshotBuilder Fill(GridPos min, GridPos maxInclusive, CellClass cellClass)
    {
        for (long y = min.Y; y <= maxInclusive.Y; y++)
        {
            for (long z = min.Z; z <= maxInclusive.Z; z++)
            {
                for (long x = min.X; x <= maxInclusive.X; x++)
                {
                    Set((int)x, (int)y, (int)z, cellClass);
                }
            }
        }

        return this;
    }

    /// <summary>Reads back a cell written so far (Unknown if never written or outside the box). Throws <see cref="InvalidOperationException"/> after <see cref="Build"/>.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    public CellClass GetClass(int x, int y, int z) =>
        Contains(x, y, z) ? Live()[Index(x, y, z)] : CellClass.Unknown;

    /// <summary>
    /// Finishes the builder and returns the immutable snapshot. The arrays are transferred, not copied, so this
    /// is O(1) and the builder cannot be used afterwards.
    /// </summary>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> was already called.</exception>
    public BlockGridSnapshot Build()
    {
        CellClass[] classes = Live();
        var snapshot = new BlockGridSnapshot(Dimension, Origin, SizeX, SizeY, SizeZ, classes, _flags, _steps);
        _classes = null;
        _flags = null;
        _steps = null;
        return snapshot;
    }

    private CellClass[] Live() =>
        _classes ?? throw new InvalidOperationException("Build() was already called; create a new builder.");

    private int Index(int x, int y, int z)
    {
        long lx = (long)x - Origin.X;
        long ly = (long)y - Origin.Y;
        long lz = (long)z - Origin.Z;
        if ((ulong)lx >= (ulong)SizeX || (ulong)ly >= (ulong)SizeY || (ulong)lz >= (ulong)SizeZ)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}, {z}) is outside the snapshot box {Origin} + ({SizeX}, {SizeY}, {SizeZ}).");
        }

        return (int)ly * _strideY + (int)lz * _strideZ + (int)lx;
    }
}
