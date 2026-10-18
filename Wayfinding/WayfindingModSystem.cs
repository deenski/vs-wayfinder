using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Wayfinding.Infra;
using Wayfinding.Pathing.Teams;
using Wayfinding.Teams;

namespace Wayfinding;

public partial class WayfindingModSystem : ModSystem
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
    /// Active navigation target. Null on the server. The map layer reads this on
    /// the client main thread and draws nothing while <see cref="NavTargets.MapAllowed"/>
    /// is false.
    /// </summary>
    public NavTargets? NavTargets => navigation?.Targets;

    public WayfindingMapLayer? MapLayer { get; private set; }

    public ICoreClientAPI? Client => capi;

    public bool IsEnabled(Feature feature)
    {
        return Gate.IsEnabled(feature);
    }

    public void AttachMapLayer(WayfindingMapLayer layer)
    {
        MapLayer = layer;
    }

    public void DetachMapLayer(WayfindingMapLayer layer)
    {
        if (ReferenceEquals(MapLayer, layer))
        {
            MapLayer = null;
        }
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
            RegisterWayfindingLayer(api);
        }

        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;
        // NavCommands owns the one .nav root. share and team are subcommands of it.
        _teams = new TeamService(api, Log);
        NavCommands.Register(api, this, _teams);
        _teams.Start();
        navigation = NavTargetCommands.Register(api, this, _teams);
        RegisterTeammateTether(api);
        RegisterDimensionAndArrival(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        RunGate();
    }

    private static void RegisterWayfindingLayer(ICoreClientAPI api)
    {
        api.ModLoader.GetModSystem<WorldMapManager>()
            .RegisterMapLayer<WayfindingMapLayer>(WayfindingMapLayer.LayerCode, 1.1);
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

    public override void Dispose()
    {
        if (capi is not null)
        {
            capi.Event.LevelFinalize -= OnLevelFinalize;
            capi.Event.LeaveWorld -= OnLeaveWorld;
            capi = null;
        }

        // Arrival (KAN-23) before the tether (KAN-22). The tether reads TeamService
        // on its tick, so that listener is removed before teams are disposed.
        // Both partials are safe when their Register methods never ran.
        DisposeDimensionAndArrival();
        DisposeTeammateTether();
        _teams?.Dispose();
        _teams = null;
        navigation?.OnLeaveWorld();
        navigation?.Dispose();
        navigation = null;
        base.Dispose();
    }
}
