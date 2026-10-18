using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Wayfinding.Infra;

namespace Wayfinding;

/// <summary>
/// Reads <c>capi.World.Player.Entity.Pos</c>. Unavailable when the player or the entity is missing.
/// </summary>
public sealed class ClientPlayerPositionSource : IPlayerPositionSource
{
    private readonly ICoreClientAPI api;

    public ClientPlayerPositionSource(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public PlayerFix Read()
    {
        var player = api.World?.Player;
        var entity = player?.Entity;
        var pos = entity?.Pos;
        if (player == null || entity == null || pos == null)
        {
            return PlayerFix.Unavailable;
        }

        return PlayerFix.At(pos.X, pos.Y, pos.Z, pos.Dimension);
    }
}

public sealed class ClientArrivalNotice : IArrivalNotice
{
    private readonly ICoreClientAPI api;

    public ClientArrivalNotice(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public void Arrived()
    {
        api.ShowChatMessage("[wayfinding] " + DimensionArrivalSession.ArrivedMessage);
    }
}

/// <summary>
/// Live waypoint position from <c>WaypointMapLayer.ownWaypoints</c>. Closed when that feature is gated off.
/// </summary>
public sealed class ClientWaypointPositionSource : IWaypointPositionSource
{
    private readonly ICoreClientAPI api;
    private readonly VersionGate gate;

    public ClientWaypointPositionSource(ICoreClientAPI api, VersionGate gate)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public bool TryGet(Guid waypointId, out WaypointPosition position)
    {
        position = default;
        if (waypointId == Guid.Empty
            || !gate.IsEnabled(Feature.MapLayer)
            || !gate.IsEnabled(Feature.Waypoints))
        {
            return false;
        }

        var manager = api.ModLoader.GetModSystem<WorldMapManager>();
        if (manager?.MapLayers == null)
        {
            return false;
        }

        WaypointMapLayer? layer = null;
        for (var i = 0; i < manager.MapLayers.Count; i++)
        {
            if (manager.MapLayers[i] is WaypointMapLayer found)
            {
                layer = found;
                break;
            }
        }

        if (layer?.ownWaypoints == null)
        {
            return false;
        }

        for (var i = 0; i < layer.ownWaypoints.Count; i++)
        {
            var wp = layer.ownWaypoints[i];
            if (wp?.Position == null || string.IsNullOrEmpty(wp.Guid) || !Guid.TryParse(wp.Guid, out var id) || id != waypointId)
            {
                continue;
            }

            position = new WaypointPosition(wp.Position.X, wp.Position.Y, wp.Position.Z);
            return true;
        }

        return false;
    }
}

/// <summary>
/// One 20 ms game-tick listener plus dimension, death, and entity-spawn. Unsubscribe is idempotent.
/// </summary>
public sealed class ClientDimensionArrivalEvents : IDimensionArrivalEvents
{
    public const int TickIntervalMs = 20;

    private readonly ICoreClientAPI api;
    private DimensionArrivalHandlers? handlers;
    private long tickListenerId;
    private bool subscribed;

    public ClientDimensionArrivalEvents(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public void Subscribe(DimensionArrivalHandlers handlers)
    {
        if (subscribed)
        {
            return;
        }

        this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        tickListenerId = api.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
        api.Event.PlayerDimensionChanged += OnDimensionChanged;
        api.Event.PlayerDeath += OnDeath;
        api.Event.PlayerEntitySpawn += OnSpawn;
        subscribed = true;
    }

    public void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        api.Event.UnregisterGameTickListener(tickListenerId);
        api.Event.PlayerDimensionChanged -= OnDimensionChanged;
        api.Event.PlayerDeath -= OnDeath;
        api.Event.PlayerEntitySpawn -= OnSpawn;
        handlers = null;
        subscribed = false;
    }

    private void OnTick(float dt)
    {
        handlers?.OnTick();
    }

    private void OnDimensionChanged(IPlayer player)
    {
        if (!IsOwn(player?.PlayerUID))
        {
            return;
        }

        handlers?.OnDimensionChanged();
    }

    private void OnDeath(IClientPlayer player)
    {
        if (!IsOwn(player?.PlayerUID))
        {
            return;
        }

        handlers?.OnDeath();
    }

    private void OnSpawn(IClientPlayer player)
    {
        if (string.IsNullOrEmpty(player?.PlayerUID))
        {
            return;
        }

        handlers?.OnEntitySpawn(player.PlayerUID);
    }

    private bool IsOwn(string? playerUid)
    {
        var own = api.World?.Player?.PlayerUID;
        return !string.IsNullOrEmpty(own) && string.Equals(own, playerUid, StringComparison.Ordinal);
    }
}
