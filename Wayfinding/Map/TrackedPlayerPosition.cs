using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Wayfinding.Pathing.Map;

namespace Wayfinding;

/// <summary>
/// Position of a tracked player for a later tether. The entity is used when it
/// is loaded. Otherwise the tracking packet's X/Z is used. <c>PlayerByUid</c>
/// is not called: a null <see cref="PacketPlayerPosition.AssociatedPlayer"/>
/// means that lookup already failed when the packet was stored.
/// </summary>
public static class TrackedPlayerPosition
{
    public static bool TryHorizontal(PacketPlayerPosition packet, out double x, out double z)
    {
        var entity = packet.AssociatedPlayer?.Entity;
        return RemotePlayerPosition.TryHorizontal(
            entity != null,
            entity?.Pos.X ?? 0,
            entity?.Pos.Z ?? 0,
            packetPresent: true,
            packet.Despawn,
            packet.PosX,
            packet.PosZ,
            out x,
            out z);
    }
}
