namespace Wayfinding.Pathing.Teams;

/// <summary>
/// An online player the client can match to a chat <c>from:</c> entity id.
/// <see cref="EntityLoaded"/> is false when the id is known but the entity
/// is not in the world. That line cannot be verified.
/// </summary>
public readonly record struct OnlinePlayer(string Uid, long EntityId, bool EntityLoaded);

/// <summary>What <see cref="TeamModel.TryApplyChat"/> did with one chat line.</summary>
public enum ShareChatDisposition
{
    /// <summary>Not a share announcement, or the message and data disagree.</summary>
    Ignored,

    /// <summary>
    /// A share payload was dropped because the sender was not verified.
    /// No evidence or share flag was changed.
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
public readonly record struct ShareRecord(int GroupId, string PlayerUid, bool Sharing);
