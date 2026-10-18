using Wayfinding.Pathing.Coarse;

namespace Wayfinding.Pathing.Cache;

/// <summary>What the game's surface block is, as captured for one column (the adapter decides from fluids).</summary>
public enum SurfaceKind : byte
{
    /// <summary>A block to stand on (ground, leaves, anything that is not a liquid). Feet one above it.</summary>
    Solid = 0,

    /// <summary>Water (or another swimmable liquid) at the surface. Feet in the top water cell.</summary>
    Water = 1,

    /// <summary>Lava at the surface: never entered.</summary>
    Lava = 2,

    /// <summary>Nothing usable (a void column, or a surface too high to encode).</summary>
    Blocked = 3,
}

/// <summary>
/// The cached surface of one 32×32 chunk column (WF-16): per block column the feet Y and kind, packed into one
/// <see cref="ushort"/> (2 bytes per column, 2 KB per chunk column, design v2 §3). Immutable.
/// </summary>
/// <remarks>
/// <para>Packing: bits 0–13 feet Y (0 to <see cref="MaxFeetY"/>), bits 14–15 the kind (Land, Water, Lava, Blocked).
/// Full resolution: one entry per block column, which is what the coarse planner's 8×8 cells read. There is no
/// Unknown entry: a stored chunk column is fully revealed.</para>
/// <para>Layout of <see cref="Packed"/>: index <c>localZ × 32 + localX</c>, like the game's <c>RainHeightMap</c>.</para>
/// </remarks>
public sealed class HeightChunk : IEquatable<HeightChunk>
{
    /// <summary>Chunk column size in blocks (the game's chunk size).</summary>
    public const int Size = 32;

    /// <summary>Block columns per chunk column.</summary>
    public const int Columns = Size * Size;

    /// <summary>Highest feet Y that can be stored; higher surfaces are stored as Blocked.</summary>
    public const int MaxFeetY = (1 << 14) - 1;

    private const int Shift = 5;
    private readonly ushort[] _packed;

    private HeightChunk(int chunkX, int chunkZ, ushort[] packed)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _packed = packed;
    }

    /// <summary>Chunk X (block X / 32, floored).</summary>
    public int ChunkX { get; }

    /// <summary>Chunk Z (block Z / 32, floored).</summary>
    public int ChunkZ { get; }

    /// <summary>The packed entries (read-only view).</summary>
    public ReadOnlySpan<ushort> Packed => _packed;

    /// <summary>Chunk coordinate of a block coordinate (floor division by 32).</summary>
    /// <param name="block">Block X or Z.</param>
    public static int ChunkOf(int block) => block >> Shift;

    /// <summary>
    /// Captures a chunk column from the game's rain height map: for each column the Y of the topmost
    /// non-rain-permeable block and what that block is.
    /// </summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <param name="rainHeights">1 024 surface Ys, index <c>localZ × 32 + localX</c>.</param>
    /// <param name="surface">What the surface block of column (localX, localZ) at Y is.</param>
    /// <returns>The chunk.</returns>
    public static HeightChunk FromRainHeightMap(int chunkX, int chunkZ, ReadOnlySpan<ushort> rainHeights, Func<int, int, int, SurfaceKind> surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (rainHeights.Length != Columns)
        {
            throw new ArgumentException($"A rain height map has {Columns} entries.", nameof(rainHeights));
        }

        var packed = new ushort[Columns];
        for (int i = 0; i < Columns; i++)
        {
            int y = rainHeights[i];
            SurfaceKind kind = surface(i & (Size - 1), i >> Shift, y);
            // Feet stand on a solid (one above it); in the top water cell; lava's Y only matters for drawing.
            int feet = kind == SurfaceKind.Water ? y : y + 1;
            packed[i] = Pack(kind, feet);
        }

        return new HeightChunk(chunkX, chunkZ, packed);
    }

    /// <summary>Builds a chunk from columns (tests, and the snapshot of loaded terrain). Unknown columns are not allowed.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <param name="column">The column at (localX, localZ).</param>
    /// <returns>The chunk.</returns>
    public static HeightChunk FromColumns(int chunkX, int chunkZ, Func<int, int, HeightColumn> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var packed = new ushort[Columns];
        for (int i = 0; i < Columns; i++)
        {
            HeightColumn c = column(i & (Size - 1), i >> Shift);
            SurfaceKind kind = c.Kind switch
            {
                ColumnKind.Land => SurfaceKind.Solid,
                ColumnKind.Water => SurfaceKind.Water,
                ColumnKind.Lava => SurfaceKind.Lava,
                ColumnKind.Blocked => SurfaceKind.Blocked,
                _ => throw new ArgumentException("A cached chunk column has no Unknown columns.", nameof(column)),
            };
            packed[i] = Pack(kind, c.FeetY);
        }

        return new HeightChunk(chunkX, chunkZ, packed);
    }

    /// <summary>Wraps already-packed entries (the file reader). The array is copied.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <param name="packed">1 024 packed entries.</param>
    /// <returns>The chunk.</returns>
    public static HeightChunk FromPacked(int chunkX, int chunkZ, ReadOnlySpan<ushort> packed)
    {
        if (packed.Length != Columns)
        {
            throw new ArgumentException($"A chunk column has {Columns} entries.", nameof(packed));
        }

        return new HeightChunk(chunkX, chunkZ, packed.ToArray());
    }

    /// <summary>The column at local (x, z), each 0..31.</summary>
    /// <param name="localX">Local X.</param>
    /// <param name="localZ">Local Z.</param>
    public HeightColumn GetLocal(int localX, int localZ)
    {
        ushort v = _packed[(localZ << Shift) | localX];
        int feet = v & MaxFeetY;
        return (v >> 14) switch
        {
            0 => new HeightColumn(ColumnKind.Land, feet),
            1 => new HeightColumn(ColumnKind.Water, feet),
            2 => new HeightColumn(ColumnKind.Lava, feet),
            _ => new HeightColumn(ColumnKind.Blocked, feet),
        };
    }

    /// <inheritdoc />
    public bool Equals(HeightChunk? other) =>
        other is not null && ChunkX == other.ChunkX && ChunkZ == other.ChunkZ && _packed.AsSpan().SequenceEqual(other._packed);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HeightChunk);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(ChunkX);
        h.Add(ChunkZ);
        h.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_packed.AsSpan()));
        return h.ToHashCode();
    }

    private static ushort Pack(SurfaceKind kind, int feet)
    {
        if (feet < 0 || feet > MaxFeetY)
        {
            return (ushort)(3 << 14); // Blocked: out of the encodable range
        }

        return (ushort)(((int)kind << 14) | feet);
    }
}
