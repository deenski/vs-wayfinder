using System;
using Wayfinding.Pathing.Grid;

namespace Wayfinding;

/// <summary>
/// One drawn piece of a route. This is the renderer's seam for the KAN-26 result:
/// an ordered list of grid positions, each with a kind. KAN-26's walk and swim
/// steps map to <see cref="RouteSegmentKind.Known"/>; its build steps map to
/// <see cref="RouteSegmentKind.Build"/>; its unknown tail maps to
/// <see cref="RouteSegmentKind.Unknown"/>. The straight line in this ticket is
/// a single Unknown segment.
/// </summary>
public enum RouteSegmentKind
{
    Known = 0,
    Unknown = 1,
    Build = 2,
}

public readonly struct RouteSegment
{
    public GridPos From { get; init; }

    public GridPos To { get; init; }

    public RouteSegmentKind Kind { get; init; }

    public static GridPos CellOf(double x, double y, double z)
    {
        return new GridPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
    }

    public static RouteSegment StraightUnknown(GridPos from, GridPos to)
    {
        return new RouteSegment
        {
            From = from,
            To = to,
            Kind = RouteSegmentKind.Unknown,
        };
    }
}
