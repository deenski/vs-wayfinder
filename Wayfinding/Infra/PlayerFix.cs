namespace Wayfinding.Infra;

/// <summary>
/// One sample of the local player. <see cref="Y"/> is dimension-relative.
/// <see cref="InternalY"/> is <c>Y + Dimension * WaypointDimension.Boundary</c>,
/// matching <c>EntityPos.InternalY</c>, so a position in another dimension
/// cannot satisfy an arrival check.
/// </summary>
public readonly struct PlayerFix
{
    private PlayerFix(bool available, double x, double y, double internalY, double z, int dimension)
    {
        IsAvailable = available;
        X = x;
        Y = y;
        InternalY = internalY;
        Z = z;
        Dimension = dimension;
    }

    public static PlayerFix Unavailable { get; } = new(false, 0, 0, 0, 0, 0);

    public bool IsAvailable { get; }

    public double X { get; }

    public double Y { get; }

    public double InternalY { get; }

    public double Z { get; }

    public int Dimension { get; }

    public static PlayerFix At(double x, double y, double z, int dimension)
    {
        return new PlayerFix(true, x, y, WaypointDimension.InternalY(y, dimension), z, dimension);
    }
}

public interface IPlayerPositionSource
{
    PlayerFix Read();
}
