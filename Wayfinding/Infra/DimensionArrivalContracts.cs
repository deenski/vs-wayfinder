using System;

namespace Wayfinding.Infra;

public enum ReplanReason
{
    Teleport = 0,
    DimensionChanged = 1,
    Death = 2,
    Spawn = 3,
}

public readonly struct ReplanRequest
{
    public ReplanRequest(ReplanReason reason, long elapsedMilliseconds)
    {
        Reason = reason;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public ReplanReason Reason { get; }

    public long ElapsedMilliseconds { get; }
}

/// <summary>
/// Live waypoint position. <see cref="PositionY"/> is <c>Waypoint.Position.Y</c>
/// (internal Y).
/// </summary>
public readonly struct WaypointPosition
{
    public WaypointPosition(double x, double positionY, double z)
    {
        X = x;
        PositionY = positionY;
        Z = z;
    }

    public double X { get; }

    public double PositionY { get; }

    public double Z { get; }
}

public interface IWaypointPositionSource
{
    bool TryGet(Guid waypointId, out WaypointPosition position);
}

/// <summary>
/// False when the version gate has not passed or the client map is not allowed.
/// Callers fail closed: no arrival and an unconfirmed dimension.
/// </summary>
public interface INavigationPermission
{
    bool AllowsNavigation { get; }
}

public interface IArrivalNotice
{
    void Arrived();
}

public sealed class DimensionArrivalHandlers
{
    public DimensionArrivalHandlers(
        Action onTick,
        Action onDimensionChanged,
        Action onDeath,
        Action<string> onEntitySpawn)
    {
        OnTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
        OnDimensionChanged = onDimensionChanged ?? throw new ArgumentNullException(nameof(onDimensionChanged));
        OnDeath = onDeath ?? throw new ArgumentNullException(nameof(onDeath));
        OnEntitySpawn = onEntitySpawn ?? throw new ArgumentNullException(nameof(onEntitySpawn));
    }

    public Action OnTick { get; }

    public Action OnDimensionChanged { get; }

    public Action OnDeath { get; }

    public Action<string> OnEntitySpawn { get; }
}

/// <summary>
/// One game-tick listener plus the dimension, death, and entity-spawn events.
/// <see cref="Unsubscribe"/> removes every subscription and is safe to call twice.
/// </summary>
public interface IDimensionArrivalEvents
{
    void Subscribe(DimensionArrivalHandlers handlers);

    void Unsubscribe();
}
