using Vintagestory.API.Common;

namespace Wayfinding;

/// <summary>
/// KAN-22 (WF-8) command placeholder on the single <c>.nav</c> root.
/// </summary>
public static class TeammateTetherCommands
{
    /// <summary>
    /// No subcommands are planned for the teammate tether. <paramref name="nav"/> is
    /// the existing root; this method must not open another one. <c>.nav share</c>
    /// and <c>.nav team</c> belong to KAN-21. <c>.nav player</c> belongs to KAN-20.
    /// </summary>
    public static void Register(IChatCommand nav, WayfindingModSystem mod)
    {
        _ = nav;
        _ = mod;
    }
}
