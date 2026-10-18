namespace Wayfinding.Pathing.Map;

/// <summary>
/// Half-open block cuboid, the same test as <c>Cuboidi.Contains</c>:
/// <c>x &gt;= min &amp;&amp; x &lt; max</c> on each axis. Built from either corner order.
/// Client <c>TestAccess</c> is not used; callers test areas themselves.
/// </summary>
public readonly struct ClaimBox
{
    /// <summary>Inclusive minimum block X.</summary>
    public int MinX { get; init; }

    /// <summary>Inclusive minimum block Y.</summary>
    public int MinY { get; init; }

    /// <summary>Inclusive minimum block Z.</summary>
    public int MinZ { get; init; }

    /// <summary>Exclusive maximum block X.</summary>
    public int MaxX { get; init; }

    /// <summary>Exclusive maximum block Y.</summary>
    public int MaxY { get; init; }

    /// <summary>Exclusive maximum block Z.</summary>
    public int MaxZ { get; init; }

    /// <summary>Builds a box from two opposite corners, in either order.</summary>
    /// <param name="x1">First corner X.</param>
    /// <param name="y1">First corner Y.</param>
    /// <param name="z1">First corner Z.</param>
    /// <param name="x2">Opposite corner X.</param>
    /// <param name="y2">Opposite corner Y.</param>
    /// <param name="z2">Opposite corner Z.</param>
    public static ClaimBox FromCorners(int x1, int y1, int z1, int x2, int y2, int z2)
    {
        return new ClaimBox
        {
            MinX = Math.Min(x1, x2),
            MinY = Math.Min(y1, y2),
            MinZ = Math.Min(z1, z2),
            MaxX = Math.Max(x1, x2),
            MaxY = Math.Max(y1, y2),
            MaxZ = Math.Max(z1, z2),
        };
    }

    /// <summary>True when the block is inside the half-open cuboid.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    public bool Contains(int x, int y, int z)
    {
        return x >= MinX && x < MaxX && y >= MinY && y < MaxY && z >= MinZ && z < MaxZ;
    }

    /// <summary>True when the column is inside the box, ignoring Y.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="z">Block Z.</param>
    public bool ContainsXZ(int x, int z)
    {
        return x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;
    }
}
