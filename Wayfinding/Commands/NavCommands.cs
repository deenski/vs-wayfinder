using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Wayfinding.Infra;

namespace Wayfinding;

public static class NavCommands
{
    public static void Register(ICoreClientAPI capi, WayfindingModSystem mod)
    {
        // One .nav root. GetOrCreate so a later registrar can add subcommands
        // without Create throwing when this root already exists. KAN-21 adds
        // share and team on its own call. KAN-22 and KAN-23 extend this same
        // instance through the stubs below.
        IChatCommand nav = capi.ChatCommands
            .GetOrCreate("nav")
            .WithDescription("Wayfinding")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("stats")
                .WithDescription("Show the version gate, render guard, verbosity, and planner stats.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => TextCommandResult.Success(StatsReport.Format(
                    mod.Gate,
                    mod.RenderGuard,
                    mod.Log.Verbosity,
                    mod.Metrics)))
            .EndSubCommand()
            .BeginSubCommand("debug")
                .WithDescription("Render spike. Subcommands: on, off, throw.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => TextCommandResult.Success(LayerStatus(), null))
                .BeginSubCommand("on")
                    .WithDescription("Draw a sample line from the player.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => OnDebug())
                .EndSubCommand()
                .BeginSubCommand("off")
                    .WithDescription("Clear the debug target.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => OffDebug())
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => OffDebug())
                .EndSubCommand()
                .BeginSubCommand("throw")
                    .WithDescription("Throw once inside Render to prove the guard.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => ArmThrow())
                .EndSubCommand()
            .EndSubCommand();

        TeammateTetherCommands.Register(nav, mod);
        DimensionArrivalCommands.Register(nav, mod);
    }

    private static TextCommandResult OnDebug()
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

    private static TextCommandResult OffDebug()
    {
        DebugMapLayer? layer = DebugMapLayer.Instance;
        layer?.ClearTarget();
        return TextCommandResult.Success("Wayfinding debug target cleared.", null);
    }

    private static TextCommandResult ArmThrow()
    {
        DebugMapLayer? layer = DebugMapLayer.Instance;
        if (layer == null)
        {
            return TextCommandResult.Success("Wayfinding debug layer is not loaded yet.", null);
        }

        layer.ArmRenderException();
        return TextCommandResult.Success("Next map render will throw once, then the layer disables itself.", null);
    }

    private static string LayerStatus()
    {
        return DebugMapLayer.Instance?.Status() ?? "Wayfinding debug layer is not loaded yet.";
    }
}
