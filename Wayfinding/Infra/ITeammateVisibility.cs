namespace Wayfinding.Infra;

/// <summary>
/// Whether a player may be targeted. Design §4: no line, marker, distance, or
/// tether for anyone who is not a visible teammate.
/// Today the argument is the name typed into <c>.nav player</c>. KAN-21's
/// <c>ITeamService.IsVisibleTeammate</c> takes a uid, so wiring it needs a
/// server-side name-to-uid lookup. Until that lookup exists, the default
/// implementation fails closed and no name is accepted.
/// </summary>
public interface ITeammateVisibility
{
    bool IsVisibleTeammate(string? playerNameOrUid);
}

/// <summary>
/// Denies every player. This is the stand-in until KAN-21's team service is wired.
/// </summary>
public sealed class NoTeammateVisibility : ITeammateVisibility
{
    public static NoTeammateVisibility Instance { get; } = new();

    public bool IsVisibleTeammate(string? playerNameOrUid)
    {
        return false;
    }
}
