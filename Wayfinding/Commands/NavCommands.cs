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
            .EndSubCommand()
            .BeginSubCommand("coord")
                .WithDescription("Set a spawn-relative target and draw a straight line. Usage: .nav coord <x> <z>")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(capi.ChatCommands.Parsers.Int("x"), capi.ChatCommands.Parsers.Int("z"))
                .HandleWith(args => SetCoordinate(mod, args))
            .EndSubCommand()
            .BeginSubCommand("off")
                .WithDescription("Clear the wayfinding target.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => ClearTarget(mod))
            .EndSubCommand()
            .BeginSubCommand("debug")
                .WithDescription("Map layer debug. Subcommands: on, clear, throw.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => TextCommandResult.Success(Status(mod)))
                .BeginSubCommand("on")
                    .WithDescription("Draw a sample line 96 blocks east and 48 blocks south of the player.")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(_ => SampleTarget(mod))
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
    }

    static TextCommandResult SetCoordinate(WayfindingModSystem mod, TextCommandCallingArgs args)
    {
        ICoreClientAPI? capi = mod.Client;
        if (capi?.World == null)
        {
            return TextCommandResult.Success("Wayfinding is not on a client world yet.");
        }

        int x = (int)args.Parsers[0].GetValue();
        int z = (int)args.Parsers[1].GetValue();
        var spawn = capi.World.DefaultSpawnPosition.AsBlockPos;
        double worldX = spawn.X + x;
        double worldZ = spawn.Z + z;
        int y = capi.World.BlockAccessor.GetRainMapHeightAt((int)worldX, (int)worldZ);
        mod.Target.Set(worldX, y, worldZ, NavTargetKind.Coordinate, null);
        return TextCommandResult.Success(
            "Target set to spawn "
            + x + ", " + z
            + " (world " + (int)worldX + ", " + y + ", " + (int)worldZ + "). "
            + GuardNote(mod));
    }

    static TextCommandResult SampleTarget(WayfindingModSystem mod)
    {
        var pos = mod.Client?.World.Player?.Entity?.Pos;
        if (pos == null)
        {
            return TextCommandResult.Success("No player position yet.");
        }

        mod.Target.Set(
            pos.X + WayfindingMapLayer.SampleOffsetX,
            pos.Y,
            pos.Z + WayfindingMapLayer.SampleOffsetZ,
            NavTargetKind.Coordinate,
            null);
        return TextCommandResult.Success("Drawing a sample line. " + Status(mod));
    }

    static TextCommandResult ClearTarget(WayfindingModSystem mod)
    {
        mod.Target.Clear();
        return TextCommandResult.Success("Wayfinding target cleared.");
    }

    static TextCommandResult ArmThrow(WayfindingModSystem mod)
    {
        WayfindingMapLayer? layer = mod.MapLayer;
        if (layer == null)
        {
            return TextCommandResult.Success("Wayfinding map layer is not loaded yet.");
        }

        if (!mod.RenderGuard.LayerEnabled)
        {
            return TextCommandResult.Success("The render guard has already disabled the wayfinding layer for this session.");
        }

        layer.ArmRenderException();
        return TextCommandResult.Success("The next map render will throw once, then the render guard disables this layer.");
    }

    static string Status(WayfindingModSystem mod)
    {
        string target = mod.Target.Has
            ? "target " + (int)mod.Target.X + ", " + (int)mod.Target.Y + ", " + (int)mod.Target.Z
            : "no target";
        string layer = mod.MapLayer == null ? "layer not loaded" : "layer loaded";
        return target + "; " + layer + ". " + GuardNote(mod);
    }

    static string GuardNote(WayfindingModSystem mod)
    {
        return mod.RenderGuard.LayerEnabled
            ? "Render guard is on."
            : "Render guard has disabled the layer.";
    }
}
