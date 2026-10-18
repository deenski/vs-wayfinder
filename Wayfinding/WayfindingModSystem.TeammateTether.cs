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
    /// KAN-19 skips player-name targets because they have no position; it should
    /// read this on the client main thread. Draw nothing when Visible is false.
    /// LastKnown is the greyed marker. Stale is the marker after 30 seconds.
    /// </summary>
    public TeammateTetherSample? TeammateSample => teammateTether?.Current;

    /// <summary>
    /// KAN-22 (WF-8) client hook. Idempotent. Subscribes one game-tick listener,
    /// which Vintage Story runs on the main thread. Does not register a map layer
    /// or a team service. Visibility stays <see cref="NoTeammateVisibility"/> and
    /// share stays unknown until KAN-21 is wired, so the sample stays hidden.
    /// </summary>
    private void RegisterTeammateTether(ICoreClientAPI api)
    {
        if (teammateTetherListening)
        {
            return;
        }

        teammateTetherApi = api;
        teammateTether = new TeammateTether(
            new ClientTeammatePositionSource(api, this),
            NoTeammateVisibility.Instance,
            new ClientTeammateTetherInputs(api),
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
        var target = NavTargets?.Current;
        var player = target?.Kind == NavTargetKind.Player ? target.Name : null;
        teammateTether?.Tick(player);
    }
}
