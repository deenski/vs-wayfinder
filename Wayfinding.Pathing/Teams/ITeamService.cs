namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Who may be drawn or targeted. False for anyone who is not a teammate,
/// has opted out, or is unknown. Callers must not show a line, marker,
/// distance, or tether unless this returns true.
/// </summary>
public interface ITeamService
{
    /// <summary>
    /// True only for a remote player who is a verified, sharing teammate.
    /// False for a non-teammate, an opted-out player, the local player, and anyone unknown.
    /// </summary>
    /// <param name="uid">Player uid. Null and empty are hidden.</param>
    bool IsVisibleTeammate(string? uid);

    /// <summary>Uids that <see cref="IsVisibleTeammate"/> currently accepts, in ordinal order.</summary>
    IReadOnlyCollection<string> GetVisibleTeammates();
}
