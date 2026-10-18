using Vintagestory.API.Client;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    /// <summary>
    /// KAN-22 (WF-8) client hook for the teammate tether: entity position, then
    /// vanilla remote-player tracking, then a greyed last-known marker. Subscribe
    /// from <paramref name="api"/> here. Do not register a map layer or a team
    /// service; those registrations belong to other tickets.
    /// </summary>
    private void RegisterTeammateTether(ICoreClientAPI api)
    {
        _ = api;
    }

    /// <summary>
    /// KAN-22 cleanup for listeners started in <see cref="RegisterTeammateTether"/>.
    /// </summary>
    private void DisposeTeammateTether()
    {
    }
}
