namespace Wayfinding;

public enum NavTargetKind
{
    None = 0,
    Coordinate,
    Waypoint,
}

/// <summary>
/// The current draw target. Coordinates and a clicked waypoint are stored here
/// so the line has somewhere to go. Persistence, named lookup, and teammates
/// belong to later tickets.
/// </summary>
public sealed class NavTarget
{
    public bool Has { get; private set; }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double Z { get; private set; }

    public NavTargetKind Kind { get; private set; }

    public string? WaypointGuid { get; private set; }

    /// <summary>Increments on every <see cref="Set"/> and <see cref="Clear"/> so a cached label can be dropped.</summary>
    public int Revision { get; private set; }

    public void Set(double x, double y, double z, NavTargetKind kind, string? waypointGuid)
    {
        Has = true;
        X = x;
        Y = y;
        Z = z;
        Kind = kind;
        WaypointGuid = waypointGuid;
        Revision++;
    }

    public void Clear()
    {
        Has = false;
        Kind = NavTargetKind.None;
        WaypointGuid = null;
        Revision++;
    }
}
