namespace Wayfinding.Pathing.Teams;

/// <summary>
/// One pending share announcement per group. A send stays queued until this
/// client's own echo of that text arrives. Any echo of this client's chat,
/// including ordinary typed chat, restarts the spacing timer.
/// </summary>
public sealed class ShareOutbox
{
    /// <summary>Vintage Story's default <c>ChatRateLimitMs</c>. A gap of this many milliseconds is allowed.</summary>
    public const int ChatRateLimitMs = 1000;

    /// <summary>Minimum spacing between sends. This is above <see cref="ChatRateLimitMs"/>.</summary>
    public const int SpacingMs = 1500;

    private readonly ITeamClock _clock;
    private readonly int _spacingMs;
    private readonly Dictionary<int, string> _pending = new();
    private long _blockedUntil = long.MinValue;

    /// <summary>
    /// Creates an outbox. Spacing below <see cref="SpacingMs"/> or below
    /// <paramref name="rateLimitMs"/> is raised, so a requested gap of 0 or 900
    /// still waits <see cref="SpacingMs"/>.
    /// </summary>
    /// <param name="clock">Clock that timestamps sends and echoes.</param>
    /// <param name="spacingMs">Requested gap between sends.</param>
    /// <param name="rateLimitMs">Server chat rate limit. The gap is at least this large.</param>
    public ShareOutbox(ITeamClock clock, int spacingMs = SpacingMs, int rateLimitMs = ChatRateLimitMs)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _spacingMs = Math.Max(spacingMs, Math.Max(SpacingMs, rateLimitMs));
    }

    /// <summary>Groups that still need an acknowledgement.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>True when this group is still waiting to be acknowledged.</summary>
    public bool IsPending(int groupId)
    {
        return _pending.ContainsKey(groupId);
    }

    /// <summary>The pending text for a group, or null when that group is not queued.</summary>
    public string? PendingText(int groupId)
    {
        return _pending.TryGetValue(groupId, out var text) ? text : null;
    }

    /// <summary>Replaces the pending announcement for one group. One slot per group.</summary>
    public void Enqueue(int groupId, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("An announcement is required.", nameof(text));
        }

        _pending[groupId] = text;
    }

    /// <summary>Drops queued groups that are not in <paramref name="groupIds"/>.</summary>
    public void DropExcept(IReadOnlyCollection<int> groupIds)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        foreach (var groupId in _pending.Keys.ToArray())
        {
            if (!groupIds.Contains(groupId))
            {
                _pending.Remove(groupId);
            }
        }
    }

    /// <summary>Drops every queued announcement.</summary>
    public void Clear()
    {
        _pending.Clear();
    }

    /// <summary>
    /// Records an echo of this client's own chat. The spacing window restarts.
    /// When the text after the server prefix equals the pending text for
    /// <paramref name="groupId"/>, that group is acknowledged and not resent.
    /// </summary>
    public void NoteOwnEcho(int groupId, string? message)
    {
        _blockedUntil = _clock.Milliseconds + _spacingMs;
        if (!ServerShareLine.TrySplit(message, out _, out var body))
        {
            return;
        }

        if (_pending.TryGetValue(groupId, out var pending)
            && string.Equals(body, pending, StringComparison.Ordinal))
        {
            _pending.Remove(groupId);
        }
    }

    /// <summary>
    /// Takes the lowest queued group id when the spacing window is open.
    /// The item stays queued until <see cref="NoteOwnEcho"/> acknowledges it,
    /// so a dropped send is returned again after the gap. The first take is immediate.
    /// </summary>
    public bool TryTake(out int groupId, out string text)
    {
        groupId = 0;
        text = "";
        if (_pending.Count == 0)
        {
            return false;
        }

        var now = _clock.Milliseconds;
        if (_blockedUntil != long.MinValue && now < _blockedUntil)
        {
            return false;
        }

        groupId = _pending.Keys.Min();
        text = _pending[groupId];
        _blockedUntil = now + _spacingMs;
        return true;
    }
}
