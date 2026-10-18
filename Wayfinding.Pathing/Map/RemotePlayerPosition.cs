namespace Wayfinding.Pathing.Map;

/// <summary>
/// Horizontal position of a tracked remote player.
/// The entity wins when it is loaded. Otherwise the tracking packet's X/Z is the position.
/// A uid lookup is not a source: the tracking system already tried that when it filled
/// the associated player, and a miss stays a miss.
/// </summary>
public static class RemotePlayerPosition
{
    /// <summary>
    /// Resolves a horizontal position. The entity wins. Otherwise a live packet is used.
    /// Returns false when there is no entity and the packet is missing or despawned.
    /// </summary>
    public static bool TryHorizontal(
        bool entityPresent,
        double entityX,
        double entityZ,
        bool packetPresent,
        bool despawned,
        double packetX,
        double packetZ,
        out double x,
        out double z)
    {
        if (entityPresent)
        {
            x = entityX;
            z = entityZ;
            return true;
        }

        if (packetPresent && !despawned)
        {
            x = packetX;
            z = packetZ;
            return true;
        }

        x = 0;
        z = 0;
        return false;
    }
}
