using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    private DimensionArrivalSession? dimensionArrival;

    /// <summary>
    /// Live dimension relation for <see cref="NavTargets"/>. The map layer reads this.
    /// Unconfirmed, including every player target until KAN-22, draws nothing.
    /// Different shows the "different dimension" label and no line.
    /// </summary>
    public DimensionRelation RouteDimension => dimensionArrival?.Current ?? DimensionRelation.Unconfirmed;

    /// <summary>
    /// Forced re-plan. Phase 1 has no planner entry point; <see cref="WayfindingMapLayer"/>
    /// subscribes and drops its cached straight route so the next frame rebuilds it.
    /// Raised for a horizontal jump of more than <see cref="TeleportLimits.Blocks"/> blocks,
    /// and for dimension change, death, and our own entity spawn.
    /// </summary>
    public event Action<ReplanRequest>? ReplanRequested;

    /// <summary>
    /// KAN-23 (WF-9) client hook for a different dimension, an unconfirmed
    /// dimension, a teleport jump, and waypoint arrival. Subscribe from
    /// <paramref name="api"/> here. Do not register a map layer.
    /// </summary>
    private void RegisterDimensionAndArrival(ICoreClientAPI api)
    {
        if (navigation is null || dimensionArrival is not null)
        {
            return;
        }

        var session = new DimensionArrivalSession(
            navigation.Targets,
            new ClientPlayerPositionSource(api),
            () => api.ElapsedMilliseconds,
            new ClientWaypointPositionSource(api, Gate),
            new ClientDimensionArrivalEvents(api),
            new ClientArrivalNotice(api),
            Log,
            () => api.World?.Player?.PlayerUID);
        session.ReplanRequested += OnSessionReplan;
        session.Start();
        dimensionArrival = session;
    }

    /// <summary>
    /// KAN-23 cleanup for listeners started in <see cref="RegisterDimensionAndArrival"/>.
    /// Idempotent, and safe when that register method never ran. Dispose also runs
    /// on the server instance, which does not call the client hook.
    /// </summary>
    private void DisposeDimensionAndArrival()
    {
        if (dimensionArrival is null)
        {
            return;
        }

        dimensionArrival.ReplanRequested -= OnSessionReplan;
        dimensionArrival.Dispose();
        dimensionArrival = null;
    }

    private void OnSessionReplan(ReplanRequest request)
    {
        ReplanRequested?.Invoke(request);
    }
}
