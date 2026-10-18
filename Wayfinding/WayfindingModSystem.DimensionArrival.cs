using Vintagestory.API.Client;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    /// <summary>
    /// KAN-23 (WF-9) client hook for a different dimension, an unconfirmed
    /// dimension, a teleport jump, and waypoint arrival. Subscribe from
    /// <paramref name="api"/> here. Do not register a map layer.
    /// The dimension source should sit behind an interface so Phase 2b can replace it.
    /// </summary>
    private void RegisterDimensionAndArrival(ICoreClientAPI api)
    {
        _ = api;
    }

    /// <summary>
    /// KAN-23 cleanup for listeners started in <see cref="RegisterDimensionAndArrival"/>.
    /// Idempotent, and safe when that register method never ran. Dispose also runs
    /// on the server instance, which does not call the client hook.
    /// </summary>
    private void DisposeDimensionAndArrival()
    {
    }
}
