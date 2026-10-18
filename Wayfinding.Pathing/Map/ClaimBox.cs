namespace Wayfinding.Pathing.Map;

/// <summary>
/// Half-open block cuboid, the same test as <c>Cuboidi.Contains</c>:
/// <c>x &gt;= min &amp;&amp; x &lt; max</c> on each axis. Built from either corner order.
/// Client <c>TestAccess</c> is not used; callers test areas themselves.
/// </summary>
public readonly struct ClaimBox
{
    public int MinX { get; init; }
    public int MinY { get; init; }
    public int MinZ { get; init; }
    public int MaxX { get; init; }
    public int MaxY { get; init; }
    public int MaxZ { get; init; }

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

    public bool Contains(int x, int y, int z)
    {
        return x >= MinX && x < MaxX && y >= MinY && y < MaxY && z >= MinZ && z < MaxZ;
    }

    public bool ContainsXZ(int x, int z)
    {
        return x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;
    }
}
