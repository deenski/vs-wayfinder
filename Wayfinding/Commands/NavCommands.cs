using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Wayfinding.Infra;

namespace Wayfinding;

public static class NavCommands
{
    public static void Register(ICoreClientAPI capi, WayfindingModSystem mod)
    {
        // GetOrCreate so another mod system can add subcommands (KAN-21 share/team)
        // without Create throwing when this root already exists.
        capi.ChatCommands
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
            .EndSubCommand();
    }
}
