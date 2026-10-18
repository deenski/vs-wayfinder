using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Wayfinding.Infra;
using Wayfinding.Teams;

namespace Wayfinding;

public static class NavCommands
{
    public static void Register(ICoreClientAPI capi, WayfindingModSystem mod, TeamService teams)
    {
        // One .nav root. GetOrCreate so NavTargetCommands and a later registrar can add
        // subcommands without Create throwing when this root already exists. share and
        // team are registered on this instance. KAN-22 and KAN-23 extend it through the stubs below.
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
                .WithDescription("Map layer debug. Subcommands: on, off, clear, throw.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => TextCommandResult.Success(Status(mod), null))
                .BeginSubCommand("on")
                    .WithDescription("Draw a sample line 96 blocks east and 48 blocks south of the player.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => SampleTarget(mod))
                .EndSubCommand()
                .BeginSubCommand("off")
                    .WithDescription("Clear the wayfinding target.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => ClearTarget(mod))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .WithDescription("Clear the wayfinding target.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => ClearTarget(mod))
                .EndSubCommand()
                .BeginSubCommand("throw")
                    .WithDescription("Throw once inside map Render so the render guard disables this layer.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => ArmThrow(mod))
                .EndSubCommand()
            .EndSubCommand();

        teams.RegisterSubcommands(nav);
        TeammateTetherCommands.Register(nav, mod);
        DimensionArrivalCommands.Register(nav, mod);
    }

    static TextCommandResult SampleTarget(WayfindingModSystem mod)
    {
        NavTargets? targets = mod.NavTargets;
        var pos = mod.Client?.World.Player?.Entity?.Pos;
        if (targets == null || pos == null)
        {
            return TextCommandResult.Success("No player position yet.", null);
        }

        var coordinate = new AbsoluteCoord(
            (int)Math.Floor(pos.X + WayfindingMapLayer.SampleOffsetX),
            (int)Math.Floor(pos.Y),
            (int)Math.Floor(pos.Z + WayfindingMapLayer.SampleOffsetZ));
        NavReply reply = targets.SetCoordinate(coordinate, Spawn(mod), pos.Dimension);
        if (!reply.Ok)
        {
            return TextCommandResult.Error(reply.Message);
        }

        return TextCommandResult.Success(reply.Message + " " + Status(mod), null);
    }

    static TextCommandResult ClearTarget(WayfindingModSystem mod)
    {
        NavTargets? targets = mod.NavTargets;
        if (targets == null)
        {
            return TextCommandResult.Success("Wayfinding is not on a client world yet.", null);
        }

        NavReply reply = targets.Off();
        return reply.Ok
            ? TextCommandResult.Success(reply.Message, null)
            : TextCommandResult.Error(reply.Message);
    }

    static TextCommandResult ArmThrow(WayfindingModSystem mod)
    {
        WayfindingMapLayer? layer = mod.MapLayer;
        if (layer == null)
        {
            return TextCommandResult.Success("Wayfinding map layer is not loaded yet.", null);
        }

        if (!mod.RenderGuard.LayerEnabled)
        {
            return TextCommandResult.Success("The render guard has already disabled the wayfinding layer for this session.", null);
        }

        layer.ArmRenderException();
        return TextCommandResult.Success("The next map render will throw once, then the render guard disables this layer.", null);
    }

    static string Status(WayfindingModSystem mod)
    {
        string target = mod.NavTargets == null
            ? "no target"
            : mod.NavTargets.Describe(Spawn(mod));
        string layer = mod.MapLayer == null ? "layer not loaded" : "layer loaded";
        return target + "; " + layer + ". " + GuardNote(mod);
    }

    static string GuardNote(WayfindingModSystem mod)
    {
        return mod.RenderGuard.LayerEnabled
            ? "Render guard is on."
            : "Render guard has disabled the layer.";
    }

    static SpawnOrigin? Spawn(WayfindingModSystem mod)
    {
        var pos = mod.Client?.World?.DefaultSpawnPosition;
        if (pos == null)
        {
            return null;
        }

        var block = pos.AsBlockPos;
        return new SpawnOrigin(block.X, block.Z);
    }
}
