namespace Wayfinding.Pathing.Teams;

/// <summary>One vanilla player group the local player belongs to.</summary>
/// <param name="GroupId">Chat channel id of the group.</param>
/// <param name="Name">Group name shown in chat.</param>
public readonly record struct TeamGroup(int GroupId, string Name);

/// <summary>Result of limiting teammates to one named group.</summary>
public enum TeamRestrictResult
{
    /// <summary>The named group is now the only group whose teammates are visible.</summary>
    Restricted,

    /// <summary>The local player is not in a group with that name.</summary>
    NotAMember,

    /// <summary>More than one membership group matches that name.</summary>
    Ambiguous,
}
