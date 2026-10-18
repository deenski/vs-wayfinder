using System;

namespace Wayfinding.Infra;

/// <summary>
/// Client-side owner of dimension status, teleport detection, and arrival for
/// our own target. Game types stay outside this type. Teammate positions are
/// not read here.
/// </summary>
public sealed class DimensionArrivalSession : IDisposable
{
    public const string ArrivedMessage = "Arrived";

    private readonly NavTargets targets;
    private readonly IPlayerPositionSource positions;
    private readonly Func<long> elapsedMilliseconds;
    private readonly OwnTargetDimensionDetector dimensions = new();
    private readonly TeleportDetector teleports = new();
    private readonly ArrivalDetector arrival = new();
    private readonly IWaypointPositionSource waypoints;
    private readonly IDimensionArrivalEvents events;
    private readonly IArrivalNotice notice;
    private readonly IWayfindingLog log;
    private readonly Func<string?> ownPlayerUid;
    private bool started;

    public DimensionArrivalSession(
        NavTargets targets,
        IPlayerPositionSource positions,
        Func<long> elapsedMilliseconds,
        IWaypointPositionSource waypoints,
        IDimensionArrivalEvents events,
        IArrivalNotice notice,
        IWayfindingLog log,
        Func<string?> ownPlayerUid)
    {
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        this.elapsedMilliseconds = elapsedMilliseconds ?? throw new ArgumentNullException(nameof(elapsedMilliseconds));
        this.waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.notice = notice ?? throw new ArgumentNullException(nameof(notice));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.ownPlayerUid = ownPlayerUid ?? throw new ArgumentNullException(nameof(ownPlayerUid));
    }

    /// <summary>
    /// Live relation for the active target. The map layer reads this and draws a line
    /// only while it is <see cref="DimensionRelation.Same"/>.
    /// </summary>
    public DimensionRelation Current { get; private set; } = DimensionRelation.Unconfirmed;

    public event Action<ReplanRequest>? ReplanRequested;

    public void Start()
    {
        if (started)
        {
            return;
        }

        events.Subscribe(new DimensionArrivalHandlers(Tick, OnDimensionChanged, OnDeath, OnEntitySpawn));
        targets.Changed += OnTargetChanged;
        started = true;
        Tick();
    }

    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        targets.Changed -= OnTargetChanged;
        events.Unsubscribe();
    }

    public void Tick()
    {
        if (!targets.MapAllowed)
        {
            Current = DimensionRelation.Unconfirmed;
            teleports.ResetBaseline();
            return;
        }

        var player = positions.Read();
        var target = targets.Current;
        var place = PlaceFor(target);
        Current = dimensions.Evaluate(PlayerDimension(player), target, TargetDimension(target, place));

        if (player.IsAvailable && teleports.Observe(player))
        {
            Raise(ReplanReason.Teleport);
        }

        if (!arrival.Observe(player, target, place))
        {
            return;
        }

        if (!targets.ClearTarget())
        {
            return;
        }

        Current = DimensionRelation.Unconfirmed;
        log.Log(LogLevel.Notice, ArrivedMessage);
        notice.Arrived();
    }

    private void OnTargetChanged()
    {
        arrival.Reset();
    }

    private void OnDimensionChanged()
    {
        teleports.ResetBaseline();
        Raise(ReplanReason.DimensionChanged);
    }

    private void OnDeath()
    {
        teleports.ResetBaseline();
        Raise(ReplanReason.Death);
    }

    private void OnEntitySpawn(string playerUid)
    {
        var own = ownPlayerUid();
        if (string.IsNullOrEmpty(own) || !string.Equals(own, playerUid, StringComparison.Ordinal))
        {
            return;
        }

        teleports.ResetBaseline();
        Raise(ReplanReason.Spawn);
    }

    private void Raise(ReplanReason reason)
    {
        ReplanRequested?.Invoke(new ReplanRequest(reason, elapsedMilliseconds()));
    }

    private TargetPlace PlaceFor(NavTarget target)
    {
        switch (target.Kind)
        {
            case NavTargetKind.Coordinate:
                return target.Coordinate is AbsoluteCoord coordinate
                    ? TargetPlace.Coordinate(coordinate, target.Dimension)
                    : TargetPlace.Unknown;
            case NavTargetKind.Waypoint:
                return waypoints.TryGet(target.WaypointId, out var position)
                    ? TargetPlace.Waypoint(position.X, position.PositionY, position.Z)
                    : TargetPlace.Unknown;
            default:
                return TargetPlace.Unknown;
        }
    }

    private static int? PlayerDimension(PlayerFix player)
    {
        return player.IsAvailable ? player.Dimension : null;
    }

    private static int? TargetDimension(NavTarget target, TargetPlace place)
    {
        switch (target.Kind)
        {
            case NavTargetKind.Coordinate:
                return target.Dimension;
            case NavTargetKind.Waypoint:
                return place.HasDimension ? place.Dimension : null;
            default:
                return null;
        }
    }
}
