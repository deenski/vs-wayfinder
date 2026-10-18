using Vintagestory.API.Client;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    /// <summary>
    /// KAN-23 (WF-9) client hook for a different dimension, an unconfirmed
    /// dimension, a teleport jump, and waypoint arrival. Subscribe from
    /// <paramref name="api"/> here. Do not register a map layer.
    /// </summary>
    private void RegisterDimensionAndArrival(ICoreClientAPI api)
    {
        _ = api;
    }

    /// <summary>
    /// KAN-23 cleanup for listeners started in <see cref="RegisterDimensionAndArrival"/>.
    /// </summary>
    private void DisposeDimensionAndArrival()
    {
    }
}
