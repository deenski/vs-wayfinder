using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Wayfinding;

public class WayfindingModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("loaded on " + api.Side);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<DebugMapLayer>(DebugMapLayer.LayerCode, 1.1);

        api.ChatCommands.Create("nav")
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
