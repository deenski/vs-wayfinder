namespace Wayfinding.Infra;

/// <summary>
/// Waypoint dimension recovered from <c>Waypoint.Position.Y</c>. That Y is
/// <c>EntityPos.XYZ</c>, which stores internal Y. The boundary matches
/// <c>BlockPos.DimensionBoundary</c> in Vintage Story 1.22.7.
/// </summary>
public static class WaypointDimension
{
    public const int Boundary = 32768;

    public static int Decode(double positionY)
    {
        return (int)(positionY / Boundary);
    }

    public static double InternalY(double relativeY, int dimension)
    {
        return relativeY + dimension * (double)Boundary;
    }
}
