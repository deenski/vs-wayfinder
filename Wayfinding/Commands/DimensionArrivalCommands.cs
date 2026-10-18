using Vintagestory.API.Common;

namespace Wayfinding;

/// <summary>
/// KAN-23 (WF-9) command placeholder on the single <c>.nav</c> root.
/// </summary>
public static class DimensionArrivalCommands
{
    /// <summary>
    /// Add dimension or arrival subcommands on <paramref name="nav"/> with BeginSubCommand.
    /// This method must not open its own root. <c>.nav off</c> and <c>.nav coord</c>
    /// belong to KAN-19 and KAN-20.
    /// </summary>
    public static void Register(IChatCommand nav, WayfindingModSystem mod)
    {
        _ = nav;
        _ = mod;
    }
}
