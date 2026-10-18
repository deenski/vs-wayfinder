namespace Wayfinding.Infra;

/// <summary>
/// Whether a player may be targeted. Design §4: no line, marker, distance, or
/// tether for anyone who is not a visible teammate.
/// The argument is the name typed into <c>.nav player</c>, or a uid.
/// TeamService resolves a name with a client-side name-to-uid lookup through
/// AllOnlinePlayers and fails closed: an unknown name is not visible.
/// When no team service is passed, the default denies every name.
/// </summary>
public interface ITeammateVisibility
{
    bool IsVisibleTeammate(string? playerNameOrUid);
}

/// <summary>
/// Denies every player. Used when no team service is passed in.
/// </summary>
public sealed class NoTeammateVisibility : ITeammateVisibility
{
    public static NoTeammateVisibility Instance { get; } = new();

    public bool IsVisibleTeammate(string? playerNameOrUid)
    {
        return false;
    }
}
