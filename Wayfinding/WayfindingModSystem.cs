using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Wayfinding.Infra;
using Wayfinding.Pathing.Teams;
using Wayfinding.Teams;

namespace Wayfinding;

public class WayfindingModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private TeamService? _teams;
    private ClientNavSession? navigation;

    public ITeamService? Teams => _teams;

    public VersionGate Gate { get; } = new();

    public PlanMetrics Metrics { get; } = new();

    public IWayfindingLog Log { get; private set; } = null!;

    public RenderGuard RenderGuard { get; private set; } = null!;

    /// <summary>
    /// Active navigation target. Null on the server.
    /// TODO(KAN-19): WayfindingMapLayer reads <see cref="NavTargets"/> to draw the
    /// route, and draws nothing while MapAllowed is false. Ctrl+click and hotkeys
    /// call the same set and clear methods. Call NotifyWaypointDeleted when the
    /// tracked waypoint is removed.
    /// </summary>
    public NavTargets? NavTargets => navigation?.Targets;

    public bool IsEnabled(Feature feature)
    {
        return Gate.IsEnabled(feature);
    }

    public override void Start(ICoreAPI api)
    {
        Log = new WayfindingLog(new ModLoggerAdapter(Mod.Logger));
        RenderGuard = new RenderGuard(Log);
        Log.Log(LogLevel.Notice, "loaded on " + api.Side);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Log.Verbosity = ClientConfigStore.Load(api, Log).debugVerbosity;
        RunGate();
        if (Gate.IsEnabled(Feature.MapLayer))
        {
            api.ModLoader.GetModSystem<WorldMapManager>()
                .RegisterMapLayer<DebugMapLayer>(DebugMapLayer.LayerCode, 1.1);
        }

        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;
        // NavCommands owns the one .nav root. share and team are subcommands of it.
        // NavTargetCommands adds waypoint, player, coord, off, and status on that same root.
        _teams = new TeamService(api, Log);
        NavCommands.Register(api, this, _teams);
        _teams.Start();
        navigation = NavTargetCommands.Register(api, this, _teams);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        RunGate();
    }

    public override void Dispose()
    {
        _teams?.Dispose();
        _teams = null;
        navigation?.Dispose();
        navigation = null;
        base.Dispose();
    }

    private void RunGate()
    {
        try
        {
            Gate.Evaluate(new ReflectionMemberProbe(), Log);
        }
        catch (Exception ex)
        {
            Log.Log(LogLevel.Error, "version gate failed: " + ex.Message);
        }
    }

    private void OnLevelFinalize()
    {
        if (capi is null)
        {
            return;
        }

        Gate.Notify(new GateNotice(capi));
        navigation?.OnLevelFinalize();
    }

    private void OnLeaveWorld()
    {
        navigation?.OnLeaveWorld();
    }
}
