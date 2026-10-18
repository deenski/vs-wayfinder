using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        _teams = new TeamService(api, Mod.Logger);
        _teams.Start();
    }

    public override void Dispose()
    {
        _teams?.Dispose();
        _teams = null;
    }
}
