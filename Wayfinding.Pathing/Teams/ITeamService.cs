namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Who may be drawn or targeted. False for anyone who is not a teammate,
/// has opted out, or is unknown. Callers must not show a line, marker,
/// distance, or tether unless this returns true.
/// </summary>
public interface ITeamService
{
    bool IsVisibleTeammate(string? uid);

    IReadOnlyCollection<string> GetVisibleTeammates();
}
