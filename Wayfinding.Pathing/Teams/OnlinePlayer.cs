namespace Wayfinding.Pathing.Teams;

/// <summary>
/// An online player the client can match to a server chat prefix.
/// The entity does not have to be loaded. Far-away players are included.
/// </summary>
/// <param name="Name">Player name, matched exactly to the server prefix.</param>
/// <param name="Uid">Player uid.</param>
public readonly record struct OnlinePlayer(string Name, string Uid);

/// <summary>Name lookups against the online player list. No entity is required.</summary>
public static class OnlinePlayers
{
    /// <summary>
    /// Maps an exact player name to one online uid. An unknown name, an empty
    /// name, and two players who share the name but not the uid return null.
    /// </summary>
    /// <param name="name">Player name, matched with ordinal equality.</param>
    /// <param name="players">Online players. Null is treated as nobody online.</param>
    /// <returns>The matching uid, or null when the name does not resolve to one player.</returns>
    public static string? UidForName(string? name, IReadOnlyList<OnlinePlayer>? players)
    {
        if (string.IsNullOrEmpty(name) || players is null)
        {
            return null;
        }

        string? found = null;
        foreach (var player in players)
        {
            if (string.IsNullOrEmpty(player.Uid)
                || !string.Equals(player.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null && !string.Equals(found, player.Uid, StringComparison.Ordinal))
            {
                return null;
            }

            found = player.Uid;
        }

        return found;
    }
}

/// <summary>What <see cref="TeamModel.TryApplyChat"/> did with one chat line.</summary>
public enum ShareChatDisposition
{
    /// <summary>Not a share announcement.</summary>
    Ignored,

    /// <summary>
    /// The sender was not verified. A share-on changed nothing. A share-off
    /// hid that uid and did not add teammate evidence.
    /// </summary>
    Unverified,

    /// <summary>
    /// The sender was verified, but this client is not in that group channel.
    /// No evidence or share flag was changed.
    /// </summary>
    NotInGroup,

    /// <summary>The sender was verified and the share flag and evidence were updated.</summary>
    Applied,
}

/// <summary>
/// One recorded announcement: evidence in a group, plus the latest share flag
/// for that player. GroupId -1 is a share flag with no remaining evidence.
/// </summary>
/// <param name="GroupId">Group channel, or -1 when no evidence remains.</param>
/// <param name="PlayerUid">Player the announcement named.</param>
/// <param name="Sharing">Latest verified share flag for that player.</param>
public readonly record struct ShareRecord(int GroupId, string PlayerUid, bool Sharing);
