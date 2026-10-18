using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Coarse;

/// <summary>What the coarse planner knows about one block column.</summary>
public enum ColumnKind : byte
{
    /// <summary>Unrevealed or not loaded: never planned through (design v2 §3). The default value (0).</summary>
    Unknown = 0,

    /// <summary>Dry ground to stand on at <see cref="HeightColumn.FeetY"/>.</summary>
    Land = 1,

    /// <summary>Swimmable water surface at <see cref="HeightColumn.FeetY"/>.</summary>
    Water = 2,

    /// <summary>Lava at the surface: never entered.</summary>
    Lava = 3,

    /// <summary>Known but with nowhere to stand (a wall, a hazard): never entered.</summary>
    Blocked = 4,
}

/// <summary>One column of a height grid: its kind and the feet Y of a player standing on its surface.</summary>
/// <param name="Kind">The column kind.</param>
/// <param name="FeetY">Feet Y of the surface position (in-dimension Y); meaningful for Land and Water.</param>
public readonly record struct HeightColumn(ColumnKind Kind, int FeetY)
{
    /// <summary>An Unknown column.</summary>
    public static HeightColumn Unknown => default;

    /// <summary>True for Land and Water: a player can be on this column's surface.</summary>
    public bool IsPassable => Kind is ColumnKind.Land or ColumnKind.Water;
}

/// <summary>
/// Read-only surface heights plus water/lava flags per block column: what the coarse planner (WF-13) plans over.
/// The game side fills it from the <c>HeightCache</c> (WF-16, downsampled <c>RainHeightMap</c>) for revealed terrain
/// and can derive it from the fine <see cref="IBlockGrid"/> for loaded terrain (<see cref="HeightGrid.FromBlockGrid"/>).
/// </summary>
/// <remarks>Same contract as <see cref="IBlockGrid"/>: thread-safe reads, never throws, Unknown outside known terrain.</remarks>
public interface IHeightGrid
{
    /// <summary>Returns the column at world block X/Z. Never throws.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="z">Block Z.</param>
    HeightColumn GetColumn(int x, int z);
}

/// <summary>Dense immutable <see cref="IHeightGrid"/> over a rectangle; Unknown outside it.</summary>
public sealed class HeightGridSnapshot : IHeightGrid
{
    private readonly ColumnKind[] _kinds;
    private readonly int[] _feet;

    internal HeightGridSnapshot(int originX, int originZ, int sizeX, int sizeZ, ColumnKind[] kinds, int[] feet)
    {
        OriginX = originX;
        OriginZ = originZ;
        SizeX = sizeX;
        SizeZ = sizeZ;
        _kinds = kinds;
        _feet = feet;
    }

    /// <summary>Lowest X covered.</summary>
    public int OriginX { get; }

    /// <summary>Lowest Z covered.</summary>
    public int OriginZ { get; }

    /// <summary>Columns along X.</summary>
    public int SizeX { get; }

    /// <summary>Columns along Z.</summary>
    public int SizeZ { get; }

    /// <inheritdoc />
    public HeightColumn GetColumn(int x, int z)
    {
        long lx = (long)x - OriginX;
        long lz = (long)z - OriginZ;
        if ((ulong)lx >= (ulong)SizeX || (ulong)lz >= (ulong)SizeZ)
        {
            return HeightColumn.Unknown;
        }

        int i = (int)lz * SizeX + (int)lx;
        return new HeightColumn(_kinds[i], _feet[i]);
    }
}

/// <summary>Mutable writer for a <see cref="HeightGridSnapshot"/>; every column starts Unknown. Single-threaded.</summary>
public sealed class HeightGridBuilder
{
    private ColumnKind[]? _kinds;
    private int[]? _feet;

    /// <summary>Creates a builder for the rectangle [originX, originX + sizeX) × [originZ, originZ + sizeZ).</summary>
    /// <param name="originX">Lowest X.</param>
    /// <param name="originZ">Lowest Z.</param>
    /// <param name="sizeX">Columns along X, ≥ 1.</param>
    /// <param name="sizeZ">Columns along Z, ≥ 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is below 1 or the rectangle exceeds 64 Mi columns.</exception>
    public HeightGridBuilder(int originX, int originZ, int sizeX, int sizeZ)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeZ, 1);
        long n = (long)sizeX * sizeZ;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, 64L * 1024 * 1024, nameof(sizeX));
        OriginX = originX;
        OriginZ = originZ;
        SizeX = sizeX;
        SizeZ = sizeZ;
        _kinds = new ColumnKind[n];
        _feet = new int[n];
    }

    /// <summary>Lowest X covered.</summary>
    public int OriginX { get; }

    /// <summary>Lowest Z covered.</summary>
    public int OriginZ { get; }

    /// <summary>Columns along X.</summary>
    public int SizeX { get; }

    /// <summary>Columns along Z.</summary>
    public int SizeZ { get; }

    /// <summary>Writes one column.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="z">Block Z.</param>
    /// <param name="kind">Column kind.</param>
    /// <param name="feetY">Feet Y of the surface (ignored for Unknown/Blocked).</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Outside the rectangle.</exception>
    /// <exception cref="InvalidOperationException">Already built.</exception>
    public HeightGridBuilder Set(int x, int z, ColumnKind kind, int feetY = 0)
    {
        ColumnKind[] kinds = _kinds ?? throw new InvalidOperationException("Build() was already called.");
        long lx = (long)x - OriginX;
        long lz = (long)z - OriginZ;
        if ((ulong)lx >= (ulong)SizeX || (ulong)lz >= (ulong)SizeZ)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {z}) is outside the height grid.");
        }

        int i = (int)lz * SizeX + (int)lx;
        kinds[i] = kind;
        _feet![i] = feetY;
        return this;
    }

    /// <summary>Finishes the builder (O(1); the arrays are transferred).</summary>
    /// <returns>The snapshot.</returns>
    public HeightGridSnapshot Build()
    {
        ColumnKind[] kinds = _kinds ?? throw new InvalidOperationException("Build() was already called.");
        var s = new HeightGridSnapshot(OriginX, OriginZ, SizeX, SizeZ, kinds, _feet!);
        _kinds = null;
        _feet = null;
        return s;
    }
}

/// <summary>Helpers for height grids.</summary>
public static class HeightGrid
{
    /// <summary>
    /// Derives a height grid from block terrain, column by column: scanning down from <paramref name="yTop"/>, the
    /// first occupiable position (<see cref="Standability"/>) gives Land or Water at that feet Y; a dangerous position
    /// met first gives Lava; a column with no occupiable position is Blocked if all of it is known, else Unknown.
    /// </summary>
    /// <param name="grid">Block terrain.</param>
    /// <param name="originX">Lowest X.</param>
    /// <param name="originZ">Lowest Z.</param>
    /// <param name="sizeX">Columns along X.</param>
    /// <param name="sizeZ">Columns along Z.</param>
    /// <param name="yBottom">Lowest feet Y to consider.</param>
    /// <param name="yTop">Highest feet Y to consider.</param>
    /// <param name="headroomCells">Body height in cells.</param>
    /// <returns>The height grid.</returns>
    public static HeightGridSnapshot FromBlockGrid(IBlockGrid grid, int originX, int originZ, int sizeX, int sizeZ, int yBottom, int yTop, int headroomCells = Standability.DefaultHeadroomCells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var b = new HeightGridBuilder(originX, originZ, sizeX, sizeZ);
        for (int lz = 0; lz < sizeZ; lz++)
        {
            for (int lx = 0; lx < sizeX; lx++)
            {
                int x = originX + lx, z = originZ + lz;
                bool sawUnknown = false;
                HeightColumn col = new(ColumnKind.Blocked, 0);
                for (long y = yTop; y >= yBottom; y--)
                {
                    var feet = new GridPos(x, (int)y, z);
                    StandKind k = Standability.Classify(grid, feet, headroomCells);
                    if (k == StandKind.Ground)
                    {
                        col = new HeightColumn(ColumnKind.Land, feet.Y);
                        break;
                    }

                    if (k == StandKind.Swim)
                    {
                        col = new HeightColumn(ColumnKind.Water, feet.Y);
                        break;
                    }

                    if (k == StandKind.Dangerous)
                    {
                        col = new HeightColumn(ColumnKind.Lava, feet.Y);
                        break;
                    }

                    // Unknown only at the very top (head above the window) is not evidence of an unrevealed column.
                    sawUnknown |= k == StandKind.Unknown && grid.GetClass(feet) == CellClass.Unknown;
                }

                if (col.Kind == ColumnKind.Blocked && sawUnknown)
                {
                    col = HeightColumn.Unknown;
                }

                b.Set(x, z, col.Kind, col.FeetY);
            }
        }

        return b.Build();
    }
}
