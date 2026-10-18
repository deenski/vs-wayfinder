using Vintagestory.API.Common;

namespace Wayfinding;

/// <summary>
/// KAN-23 (WF-9) command placeholder on the single <c>.nav</c> root.
/// </summary>
public static class DimensionArrivalCommands
{
    /// <summary>
    /// No subcommands are planned for dimension or arrival handling. <paramref name="nav"/>
    /// is the existing root; this method must not open another one. <c>.nav off</c> and
    /// <c>.nav coord</c> belong to KAN-19 and KAN-20.
    /// </summary>
    public static void Register(IChatCommand nav, WayfindingModSystem mod)
    {
        _ = nav;
        _ = mod;
    }
}
