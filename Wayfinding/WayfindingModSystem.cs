using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Wayfinding.Infra;

namespace Wayfinding;

public class WayfindingModSystem : ModSystem
{
    private ICoreClientAPI? capi;

    public VersionGate Gate { get; } = new();

    public PlanMetrics Metrics { get; } = new();

    public IWayfindingLog Log { get; private set; } = null!;

    public RenderGuard RenderGuard { get; private set; } = null!;

    public NavTarget Target { get; } = new();

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
        if (IsEnabled(Feature.MapLayer))
        {
            RegisterWayfindingLayer(api);
        }

        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += Target.Clear;
        NavCommands.Register(api, this);
    }

    private static void RegisterWayfindingLayer(ICoreClientAPI api)
    {
        api.ModLoader.GetModSystem<WorldMapManager>()
            .RegisterMapLayer<WayfindingMapLayer>(WayfindingMapLayer.LayerCode, 1.1);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        RunGate();
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
    }
}
