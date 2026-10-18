namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Immutable, dense, axis-aligned box of cells: the standard <see cref="IBlockGrid"/> implementation.
/// Created with <see cref="BlockGridSnapshotBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The box covers world coordinates <c>Origin.X .. Origin.X + SizeX - 1</c> (likewise Y and Z). Every coordinate
/// outside it returns <see cref="CellClass.Unknown"/>; so does every cell inside it that the builder never wrote.
/// </para>
/// <para>
/// Instances never change after construction, so they are safe to share between threads; reads are plain array
/// reads with no locking. Hand the snapshot to the planner thread through any normal publication mechanism
/// (<c>Task.Run</c>, a concurrent queue, a <c>volatile</c> field).
/// </para>
/// <para>
/// Memory: one byte per cell, plus two bytes per cell only if any <see cref="CellFlags"/> were set.
/// A 96 × 64 × 96 fine window is about 0.6 MB.
/// </para>
/// </remarks>
public sealed class BlockGridSnapshot : IBlockGrid
{
    private readonly CellClass[] _classes;
    private readonly CellFlags[]? _flags;
    private readonly int _strideZ;
    private readonly int _strideY;

    internal BlockGridSnapshot(int dimension, GridPos origin, int sizeX, int sizeY, int sizeZ, CellClass[] classes, CellFlags[]? flags)
    {
        Dimension = dimension;
        Origin = origin;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _classes = classes;
        _flags = flags;
        _strideZ = sizeX;
        _strideY = sizeX * sizeZ;
    }

    /// <inheritdoc />
    public int Dimension { get; }

    /// <summary>World coordinate of the minimum (lowest X, Y, Z) corner of the box.</summary>
    public GridPos Origin { get; }

    /// <summary>Number of cells along X. Always at least 1.</summary>
    public int SizeX { get; }

    /// <summary>Number of cells along Y. Always at least 1.</summary>
    public int SizeY { get; }

    /// <summary>Number of cells along Z. Always at least 1.</summary>
    public int SizeZ { get; }

    /// <summary>World coordinate of the maximum corner of the box (inclusive).</summary>
    public GridPos MaxInclusive => new(Origin.X + SizeX - 1, Origin.Y + SizeY - 1, Origin.Z + SizeZ - 1);

    /// <summary>True when any cell carries non-<see cref="CellFlags.None"/> flags.</summary>
    public bool HasFlags => _flags is not null;

    /// <summary>True when the world coordinate lies inside the box. Never throws.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    public bool Contains(int x, int y, int z) => TryIndex(x, y, z, out _);

    /// <inheritdoc />
    public CellClass GetClass(int x, int y, int z) =>
        TryIndex(x, y, z, out int i) ? _classes[i] : CellClass.Unknown;

    /// <inheritdoc />
    public BlockCell GetCell(int x, int y, int z)
    {
        if (!TryIndex(x, y, z, out int i))
        {
            return BlockCell.Unknown;
        }

        return new BlockCell(_classes[i], _flags is null ? CellFlags.None : _flags[i]);
    }

    private bool TryIndex(int x, int y, int z, out int index)
    {
        // 64-bit maths so that extreme coordinates cannot wrap around into the box.
        long lx = (long)x - Origin.X;
        long ly = (long)y - Origin.Y;
        long lz = (long)z - Origin.Z;
        if ((ulong)lx >= (ulong)SizeX || (ulong)ly >= (ulong)SizeY || (ulong)lz >= (ulong)SizeZ)
        {
            index = -1;
            return false;
        }

        index = (int)ly * _strideY + (int)lz * _strideZ + (int)lx;
        return true;
    }
}
