using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Testing;

/// <summary>
/// Test double factory: builds real <see cref="BlockGridSnapshot"/>s from ASCII art, so tests (and KAN-29's
/// SnapshotBuilder tests) exercise exactly the type the game produces. Not for use in the shipped mod.
/// </summary>
/// <remarks>
/// <para><b>Layers</b> (<see cref="FromLayers"/>): a list of horizontal slices, bottom first. In each slice,
/// row index is Z and character index is X, so a slice reads like a top-down map with north at the top.
/// Legend (<see cref="LayerLegend"/>):</para>
/// <code>
/// '#' Solid   '.' Air   ',' Walkable   '~' Water   'L' Lava   '!' Hazard
/// '?' or ' ' Unknown (left unwritten)   'C' Solid with CellFlags.BuildForbidden
/// </code>
/// <para><b>Heightmaps</b> (<see cref="FromHeightmap"/>): one string per Z row, one character per X column.
/// A height character <c>0-9</c>, <c>a-z</c> (10-35) gives the Y of the top solid block of the column (relative
/// to the origin Y): the column is solid from the bottom up to that Y and air above, so the player's feet stand at
/// <c>height + 1</c>. Special columns, all with their floor at height 0 so the feet level matches <c>'0'</c>:</para>
/// <code>
/// '~' one block of water on the floor (swim/wade at feet level 1)
/// 'L' one block of lava on the floor     '!' one hazard block on the floor
/// '#' solid all the way up (a wall)      '?' the whole column Unknown
/// </code>
/// </remarks>
public static class FakeGrid
{
    /// <summary>Character legend used by <see cref="FromLayers"/>.</summary>
    public static IReadOnlyDictionary<char, BlockCell> LayerLegend { get; } = new Dictionary<char, BlockCell>
    {
        ['#'] = new(CellClass.Solid),
        ['.'] = new(CellClass.Air),
        [','] = new(CellClass.Walkable),
        ['~'] = new(CellClass.Water),
        ['L'] = new(CellClass.Lava),
        ['!'] = new(CellClass.Hazard),
        ['?'] = BlockCell.Unknown,
        [' '] = BlockCell.Unknown,
        ['C'] = new(CellClass.Solid, CellFlags.BuildForbidden),
    };

    /// <summary>Builds a snapshot from ASCII layers. See the class remarks for the format.</summary>
    /// <param name="layersBottomUp">Slices from the lowest Y upward; every slice must have the same rows and width.</param>
    /// <param name="origin">World coordinate of character (0, 0) of the bottom slice.</param>
    /// <param name="dimension">Dimension id of the snapshot.</param>
    /// <exception cref="ArgumentException">The art is empty, ragged, or uses a character not in the legend.</exception>
    public static BlockGridSnapshot FromLayers(IReadOnlyList<string[]> layersBottomUp, GridPos origin = default, int dimension = 0) =>
        LayersBuilder(layersBottomUp, origin, dimension).Build();

    /// <summary>Like <see cref="FromLayers"/> but returns the unfinished builder so a test can add cells.</summary>
    /// <param name="layersBottomUp">Slices from the lowest Y upward.</param>
    /// <param name="origin">World coordinate of character (0, 0) of the bottom slice.</param>
    /// <param name="dimension">Dimension id of the snapshot.</param>
    public static BlockGridSnapshotBuilder LayersBuilder(IReadOnlyList<string[]> layersBottomUp, GridPos origin = default, int dimension = 0)
    {
        ArgumentNullException.ThrowIfNull(layersBottomUp);
        if (layersBottomUp.Count == 0)
        {
            throw new ArgumentException("At least one layer is required.", nameof(layersBottomUp));
        }

        int sizeZ = layersBottomUp[0].Length;
        int sizeX = sizeZ == 0 ? 0 : layersBottomUp[0][0].Length;
        if (sizeZ == 0 || sizeX == 0)
        {
            throw new ArgumentException("Layers must have at least one row and one column.", nameof(layersBottomUp));
        }

        var builder = new BlockGridSnapshotBuilder(origin, sizeX, layersBottomUp.Count, sizeZ, dimension);
        for (int ly = 0; ly < layersBottomUp.Count; ly++)
        {
            string[] rows = layersBottomUp[ly];
            if (rows.Length != sizeZ)
            {
                throw new ArgumentException($"Layer {ly} has {rows.Length} rows; expected {sizeZ}.", nameof(layersBottomUp));
            }

            for (int lz = 0; lz < sizeZ; lz++)
            {
                string row = rows[lz];
                if (row.Length != sizeX)
                {
                    throw new ArgumentException($"Layer {ly} row {lz} has width {row.Length}; expected {sizeX}.", nameof(layersBottomUp));
                }

                for (int lx = 0; lx < sizeX; lx++)
                {
                    if (!LayerLegend.TryGetValue(row[lx], out BlockCell cell))
                    {
                        throw new ArgumentException($"Unknown layer character '{row[lx]}' at layer {ly}, row {lz}, column {lx}.", nameof(layersBottomUp));
                    }

                    if (cell.Class != CellClass.Unknown || cell.Flags != CellFlags.None)
                    {
                        builder.Set(origin.X + lx, origin.Y + ly, origin.Z + lz, cell.Class, cell.Flags);
                    }
                }
            }
        }

        return builder;
    }

    /// <summary>Builds a snapshot from a heightmap. See the class remarks for the format.</summary>
    /// <param name="rows">One string per Z row; all the same width.</param>
    /// <param name="origin">World coordinate of row 0, column 0, height 0 (the bottom of the snapshot).</param>
    /// <param name="airAbove">Rows of air kept above the highest column (at least 3 so the top is standable).</param>
    /// <param name="dimension">Dimension id of the snapshot.</param>
    public static BlockGridSnapshot FromHeightmap(string[] rows, GridPos origin = default, int airAbove = 4, int dimension = 0) =>
        HeightmapBuilder(rows, origin, airAbove, dimension).Build();

    /// <summary>Like <see cref="FromHeightmap"/> but returns the unfinished builder so a test can add cells.</summary>
    /// <param name="rows">One string per Z row; all the same width.</param>
    /// <param name="origin">World coordinate of row 0, column 0, height 0.</param>
    /// <param name="airAbove">Rows of air kept above the highest column.</param>
    /// <param name="dimension">Dimension id of the snapshot.</param>
    public static BlockGridSnapshotBuilder HeightmapBuilder(string[] rows, GridPos origin = default, int airAbove = 4, int dimension = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(airAbove, 1);
        if (rows.Length == 0 || rows[0].Length == 0)
        {
            throw new ArgumentException("The heightmap needs at least one row and one column.", nameof(rows));
        }

        int sizeZ = rows.Length;
        int sizeX = rows[0].Length;
        int maxHeight = 0;
        foreach (string row in rows)
        {
            if (row.Length != sizeX)
            {
                throw new ArgumentException("All heightmap rows must have the same width.", nameof(rows));
            }

            foreach (char c in row)
            {
                if (TryParseHeight(c, out int h))
                {
                    maxHeight = Math.Max(maxHeight, h);
                }
                else if (c is not ('~' or 'L' or '!' or '#' or '?'))
                {
                    throw new ArgumentException($"Unknown heightmap character '{c}'.", nameof(rows));
                }
            }
        }

        int sizeY = maxHeight + 1 + airAbove;
        var builder = new BlockGridSnapshotBuilder(origin, sizeX, sizeY, sizeZ, dimension);
        int top = origin.Y + sizeY - 1;
        for (int lz = 0; lz < sizeZ; lz++)
        {
            for (int lx = 0; lx < sizeX; lx++)
            {
                int x = origin.X + lx;
                int z = origin.Z + lz;
                char c = rows[lz][lx];
                switch (c)
                {
                    case '?':
                        break;
                    case '#':
                        builder.SetColumn(x, z, origin.Y, top, CellClass.Solid);
                        break;
                    case '~':
                    case 'L':
                    case '!':
                        builder.Set(x, origin.Y, z, CellClass.Solid);
                        builder.Set(x, origin.Y + 1, z, c switch { '~' => CellClass.Water, 'L' => CellClass.Lava, _ => CellClass.Hazard });
                        builder.SetColumn(x, z, origin.Y + 2, top, CellClass.Air);
                        break;
                    default:
                        TryParseHeight(c, out int h);
                        builder.SetColumn(x, z, origin.Y, origin.Y + h, CellClass.Solid);
                        builder.SetColumn(x, z, origin.Y + h + 1, top, CellClass.Air);
                        break;
                }
            }
        }

        return builder;
    }

    /// <summary>Parses a heightmap height character: <c>0-9</c> are 0-9, <c>a-z</c> are 10-35.</summary>
    /// <param name="c">The character.</param>
    /// <param name="height">The parsed height, or 0.</param>
    /// <returns>True for a height character.</returns>
    public static bool TryParseHeight(char c, out int height)
    {
        if (c is >= '0' and <= '9')
        {
            height = c - '0';
            return true;
        }

        if (c is >= 'a' and <= 'z')
        {
            height = c - 'a' + 10;
            return true;
        }

        height = 0;
        return false;
    }

    /// <summary>A flat, fully known floor: solid at Y = <paramref name="origin"/>.Y, air above.</summary>
    /// <param name="sizeX">Columns along X.</param>
    /// <param name="sizeZ">Rows along Z.</param>
    /// <param name="origin">World coordinate of the floor's minimum corner.</param>
    public static BlockGridSnapshot Flat(int sizeX, int sizeZ, GridPos origin = default) =>
        FromHeightmap(Enumerable.Repeat(new string('0', sizeX), sizeZ).ToArray(), origin);
}
