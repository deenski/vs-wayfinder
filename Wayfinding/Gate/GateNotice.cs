using Vintagestory.API.Client;
using Wayfinding.Infra;

namespace Wayfinding;

public sealed class GateNotice : IGateNotice
{
    private readonly ICoreClientAPI capi;

    public GateNotice(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void ShowDisabled(Feature feature, string message)
    {
        capi.TriggerIngameError(this, "wayfinding-" + feature, "Wayfinding: " + message);
    }
}
