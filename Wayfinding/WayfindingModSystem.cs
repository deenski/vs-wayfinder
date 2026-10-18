using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Wayfinding.Pathing.Teams;
using Wayfinding.Teams;

namespace Wayfinding;

public class WayfindingModSystem : ModSystem
{
    private TeamService? _teams;

    public ITeamService? Teams => _teams;

    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("loaded on " + api.Side);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<DebugMapLayer>(DebugMapLayer.LayerCode, 1.1);

        // KAN-17 registered this root with Create, which throws if "nav" already
        // exists. GetOrCreate keeps one root for KAN-17's debug subcommands,
        // KAN-21's share and team, and KAN-18's stats.
        api.ChatCommands.GetOrCreate("nav")
            .BeginSubCommand("debug")
                .WithDescription("Render spike. Subcommands: on, off, throw.")
                .HandleWith(_ => TextCommandResult.Success(LayerStatus(), null))
                .BeginSubCommand("on")
                    .WithDescription("Draw a sample line from the player.")
                    .HandleWith(_ => OnDebug())
                .EndSubCommand()
                .BeginSubCommand("off")
                    .WithDescription("Clear the debug target.")
                    .HandleWith(_ => OffDebug())
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .HandleWith(_ => OffDebug())
                .EndSubCommand()
                .BeginSubCommand("throw")
                    .WithDescription("Throw once inside Render to prove the guard.")
                    .HandleWith(_ => ArmThrow())
                .EndSubCommand()
            .EndSubCommand();

        _teams = new TeamService(api, Mod.Logger);
        _teams.Start();
    }

    public override void Dispose()
    {
        _teams?.Dispose();
        _teams = null;
    }

    static TextCommandResult OnDebug()
    {
        DebugMapLayer? layer = DebugMapLayer.Instance;
        if (layer == null)
        {
            return TextCommandResult.Success("Wayfinding debug layer is not loaded yet.", null);
        }

        layer.SetSampleTarget();
        return TextCommandResult.Success(
            layer.HasTarget ? "Drawing a sample line. " + layer.Status() : "No player position yet.",
            null);
    }

    static TextCommandResult OffDebug()
    {
        DebugMapLayer? layer = DebugMapLayer.Instance;
        layer?.ClearTarget();
        return TextCommandResult.Success("Wayfinding debug target cleared.", null);
    }

    static TextCommandResult ArmThrow()
    {
        DebugMapLayer? layer = DebugMapLayer.Instance;
        if (layer == null)
        {
            return TextCommandResult.Success("Wayfinding debug layer is not loaded yet.", null);
        }

        layer.ArmRenderException();
        return TextCommandResult.Success("Next map render will throw once, then the layer disables itself.", null);
    }

    static string LayerStatus()
    {
        return DebugMapLayer.Instance?.Status() ?? "Wayfinding debug layer is not loaded yet.";
    }
}
