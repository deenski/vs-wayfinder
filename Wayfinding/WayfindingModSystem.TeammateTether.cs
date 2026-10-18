using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;
using Wayfinding.Teammate;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    private TeammateTether? teammateTether;
    private ICoreClientAPI? teammateTetherApi;
    private long teammateTetherListenerId;
    private bool teammateTetherListening;

    /// <summary>
    /// Latest tether sample for the map layer. Null on the server, and null
    /// before <see cref="RegisterTeammateTether"/> or after it is disposed.
    /// <see cref="Map.WayfindingMapLayer"/> reads this on the client main thread
    /// while resolving a player target. Draw nothing when Visible is false.
    /// LastKnown is the greyed marker. Stale is the marker after 30 seconds.
    /// </summary>
    public TeammateTetherSample? TeammateSample => teammateTether?.Current;

    /// <summary>
    /// KAN-22 (WF-8) client hook. Idempotent. Subscribes one game-tick listener,
    /// which Vintage Story runs on the main thread. Does not register a map layer
    /// or a second team service. Teammate and share-off both come from
    /// <see cref="ITeammateVisibility"/> (TeamService). A missing service denies everyone.
    /// </summary>
    private void RegisterTeammateTether(ICoreClientAPI api)
    {
        if (teammateTetherListening)
        {
            return;
        }

        teammateTetherApi = api;
        ITeammateVisibility teammates = (ITeammateVisibility?)_teams ?? NoTeammateVisibility.Instance;
        teammateTether = new TeammateTether(
            new ClientTeammatePositionSource(api, this),
            teammates,
            ClientTeammateReads.Create(api, teammates),
            new ClientElapsedClock(api));
        teammateTetherListenerId = api.Event.RegisterGameTickListener(
            OnTeammateTetherTick,
            TeammateTether.ClientTickIntervalMilliseconds);
        teammateTetherListening = true;
        OnTeammateTetherTick(0f);
    }

    /// <summary>
    /// KAN-22 cleanup. Idempotent, and safe when <see cref="RegisterTeammateTether"/>
    /// never ran, including on the server instance.
    /// </summary>
    private void DisposeTeammateTether()
    {
        if (teammateTetherListening && teammateTetherApi != null)
        {
            teammateTetherApi.Event.UnregisterGameTickListener(teammateTetherListenerId);
        }

        teammateTetherListening = false;
        teammateTetherListenerId = 0;
        teammateTetherApi = null;
        teammateTether = null;
    }

    private void OnTeammateTetherTick(float dt)
    {
        _ = dt;
        try
        {
            // NavTargets is main-thread only. This listener is that thread.
            var target = NavTargets?.Current;
            var player = target?.Kind == NavTargetKind.Player ? target.Name : null;
            teammateTether?.Tick(player);
        }
        catch (Exception ex)
        {
            Log.LogOnce(
                "teammate-tick",
                LogLevel.Warning,
                "teammate tether tick failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            teammateTether?.Tick(null);
        }
    }
}
