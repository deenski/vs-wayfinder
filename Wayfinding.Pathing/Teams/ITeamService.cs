namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Who may be drawn or targeted. KAN-20 and KAN-22 must call this from the
/// game main thread only. Teams are not behind the version gate. False for
/// anyone who is not a teammate, has opted out, or is unknown. Do not show a
/// line, marker, distance, or tether unless <see cref="IsVisibleTeammate"/> is true.
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
