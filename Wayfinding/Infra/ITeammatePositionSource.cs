namespace Wayfinding.Infra;

/// <summary>
/// Live positions for one teammate. The tether asks for the entity first, then
/// vanilla remote tracking. Neither call applies hide rules.
/// </summary>
public interface ITeammatePositionSource
{
    /// <summary>
    /// Position of the player entity when that entity is loaded.
    /// False when the entity is not loaded.
    /// </summary>
    bool TryGetEntityPosition(string? playerNameOrUid, out TeammatePoint position);

    /// <summary>
    /// Vanilla remote-tracking X/Z for this player. False when tracking has no
    /// current packet. On 1.22.7 the client list also contains nearby players
    /// who are not in the group; the tether's visibility check is what drops them.
    /// </summary>
    bool TryGetTrackedPosition(string? playerNameOrUid, out TeammatePoint position);
}
