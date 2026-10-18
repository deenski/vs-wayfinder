namespace Wayfinding.Infra;

public enum PlayerPositionAvailability
{
    Unavailable = 0,
    Available = 1,
}

/// <summary>
/// One sample of the local player. <see cref="Y"/> is dimension-relative.
/// <see cref="InternalY"/> is <c>Y + Dimension * WaypointDimension.Boundary</c>,
/// matching <c>EntityPos.InternalY</c>, so a position in another dimension
/// cannot satisfy an arrival check.
/// </summary>
public readonly struct PlayerFix
{
    private PlayerFix(
        PlayerPositionAvailability availability,
        double x,
        double y,
        double internalY,
        double z,
        int dimension)
    {
        Availability = availability;
        X = x;
        Y = y;
        InternalY = internalY;
        Z = z;
        Dimension = dimension;
    }

    public static PlayerFix Unavailable { get; } = new(PlayerPositionAvailability.Unavailable, 0, 0, 0, 0, 0);

    public PlayerPositionAvailability Availability { get; }

    public double X { get; }

    public double Y { get; }

    public double InternalY { get; }

    public double Z { get; }

    public int Dimension { get; }

    public bool IsAvailable => Availability == PlayerPositionAvailability.Available;

    /// <summary>
    /// Phase 2b can replace the client reader. The sample is unavailable when
    /// the player or the entity is missing.
    /// </summary>
    public static PlayerFix Capture(
        bool playerPresent,
        bool entityPresent,
        double x,
        double y,
        double internalY,
        double z,
        int dimension)
    {
        if (!playerPresent || !entityPresent)
        {
            return Unavailable;
        }

        return new PlayerFix(PlayerPositionAvailability.Available, x, y, internalY, z, dimension);
    }

    public static PlayerFix At(double x, double y, double z, int dimension)
    {
        return new PlayerFix(
            PlayerPositionAvailability.Available,
            x,
            y,
            WaypointDimension.InternalY(y, dimension),
            z,
            dimension);
    }
}

public interface IPlayerPositionSource
{
    PlayerFix Read();
}
