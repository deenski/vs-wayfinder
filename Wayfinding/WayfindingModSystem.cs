using Vintagestory.API.Common;

namespace Wayfinding;

public class WayfindingModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("loaded on " + api.Side);
    }
}
