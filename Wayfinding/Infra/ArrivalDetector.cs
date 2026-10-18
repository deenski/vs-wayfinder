using System;

namespace Wayfinding.Infra;

/// <summary>
/// Arrival radii. Horizontal is <c>dx² + dz² &lt;= 16</c> (4 blocks). Vertical is
/// <c>|internalY| &lt;= 3</c>. Both bounds are inclusive.
/// </summary>
public static class ArrivalLimits
{
    public const double HorizontalBlocks = 4;

    public const double HorizontalSquared = HorizontalBlocks * HorizontalBlocks;

    public const double VerticalBlocks = 3;
}

/// <summary>
/// Where the active target sits for an arrival check. Waypoint
/// <see cref="InternalY"/> is <c>Waypoint.Position.Y</c>. A coordinate with no
/// Y sets <see cref="HasVertical"/> false and is checked on X/Z only.
/// </summary>
public readonly struct TargetPlace
{
    public TargetPlace(
        bool hasPosition,
        double x,
        double z,
        bool hasVertical,
        double internalY,
        bool hasDimension,
        int dimension)
    {
        HasPosition = hasPosition;
        X = x;
        Z = z;
        HasVertical = hasVertical;
        InternalY = internalY;
        HasDimension = hasDimension;
        Dimension = dimension;
    }

    public static TargetPlace Unknown => default;

    public bool HasPosition { get; }

    public double X { get; }

    public double Z { get; }

    public bool HasVertical { get; }

    public double InternalY { get; }

    public bool HasDimension { get; }

    public int Dimension { get; }

    public static TargetPlace Coordinate(AbsoluteCoord coordinate, int? dimension)
    {
        var hasDimension = dimension.HasValue;
        var dim = dimension ?? 0;
        var hasVertical = hasDimension && coordinate.Y.HasValue;
        var internalY = hasVertical
            ? WaypointDimension.InternalY(coordinate.Y!.Value, dim)
            : 0;
        return new TargetPlace(true, coordinate.X, coordinate.Z, hasVertical, internalY, hasDimension, dim);
    }

    public static TargetPlace Waypoint(double x, double positionY, double z)
    {
        return new TargetPlace(true, x, z, true, positionY, true, WaypointDimension.Decode(positionY));
    }
}

/// <summary>
/// True once from <see cref="Observe"/>, when the player is in arrival range of
/// our own waypoint or coordinate and at least one out-of-range sample has been
/// seen since the target was set or <see cref="Reset"/>. Player targets never arrive.
/// </summary>
public sealed class ArrivalDetector
{
    private bool armed;
    private bool tracking;
    private NavTargetKind kind;
    private Guid waypointId;
    private string? name;
    private int x;
    private int z;
    private int? y;
    private int? dimension;

    public void Reset()
    {
        armed = false;
    }

    public bool Observe(PlayerFix player, NavTarget target, TargetPlace place)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsSame(target))
        {
            armed = false;
            Remember(target);
        }

        if (target.Kind != NavTargetKind.Waypoint && target.Kind != NavTargetKind.Coordinate)
        {
            return false;
        }

        if (!player.IsAvailable || !place.HasPosition)
        {
            return false;
        }

        if (!Within(player, place))
        {
            armed = true;
            return false;
        }

        if (!armed)
        {
            return false;
        }

        armed = false;
        return true;
    }

    private static bool Within(PlayerFix player, TargetPlace place)
    {
        if (!place.HasDimension || player.Dimension != place.Dimension)
        {
            return false;
        }

        var dx = player.X - place.X;
        var dz = player.Z - place.Z;
        if (dx * dx + dz * dz > ArrivalLimits.HorizontalSquared)
        {
            return false;
        }

        if (!place.HasVertical)
        {
            return true;
        }

        var dy = player.InternalY - place.InternalY;
        return Math.Abs(dy) <= ArrivalLimits.VerticalBlocks;
    }

    private bool IsSame(NavTarget target)
    {
        if (!tracking || target.Kind != kind)
        {
            return false;
        }

        switch (target.Kind)
        {
            case NavTargetKind.Waypoint:
                return target.WaypointId == waypointId;
            case NavTargetKind.Player:
                return string.Equals(target.Name, name, StringComparison.Ordinal);
            case NavTargetKind.Coordinate:
                if (target.Coordinate is not AbsoluteCoord coordinate || target.Dimension != dimension)
                {
                    return false;
                }

                return coordinate.X == x && coordinate.Z == z && coordinate.Y == y;
            default:
                return true;
        }
    }

    private void Remember(NavTarget target)
    {
        tracking = true;
        kind = target.Kind;
        waypointId = target.WaypointId;
        name = target.Name;
        dimension = target.Dimension;
        if (target.Coordinate is AbsoluteCoord coordinate)
        {
            x = coordinate.X;
            y = coordinate.Y;
            z = coordinate.Z;
        }
        else
        {
            x = 0;
            y = null;
            z = 0;
        }
    }
}
