namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Monotonic clock for share-chat spacing. The game adapter uses tick time.
/// Tests supply their own.
/// </summary>
public interface ITeamClock
{
    /// <summary>Milliseconds since an arbitrary origin. Only differences matter.</summary>
    long Milliseconds { get; }
}
