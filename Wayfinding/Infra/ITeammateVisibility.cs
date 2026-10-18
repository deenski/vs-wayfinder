namespace Wayfinding.Infra;

/// <summary>
/// Whether a player may be targeted. Design §4: no line, marker, distance, or
/// tether for anyone who is not a visible teammate. The argument is a player
/// name or a uid, matching <c>ITeamService.IsVisibleTeammate</c> so KAN-21 can
/// implement this interface with its existing method.
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
