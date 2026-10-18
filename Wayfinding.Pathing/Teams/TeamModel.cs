namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Membership, announcement evidence, and the visibility filter.
/// A remote player is visible only when the local player is in a team group,
/// a verified announcement from that player was seen in that group's channel,
/// and the latest verified announcement has sharing on. Unknown players are
/// hidden. A payload with no verified sender is ignored and does not change
/// evidence or share state.
/// </summary>
public sealed class TeamModel : ITeamService
{
    private readonly Dictionary<int, TeamGroup> _groups = new();
    private readonly HashSet<(int GroupId, string Uid)> _evidence = new();
    private readonly Dictionary<string, bool> _sharing = new(StringComparer.Ordinal);

    private string? _localUid;
    private string? _restriction;

    public string? LocalPlayerUid => _localUid;

    public string? TeamRestriction => _restriction;

    public IReadOnlyCollection<TeamGroup> Membership => _groups.Values.ToArray();

    public void SetLocalPlayer(string? uid)
    {
        _localUid = string.IsNullOrEmpty(uid) ? null : uid;
    }

    public void SetMembership(IReadOnlyList<TeamGroup> groups)
    {
        var next = new Dictionary<int, TeamGroup>();
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                continue;
            }

            next[group.GroupId] = new TeamGroup(group.GroupId, group.Name.Trim());
        }

        var sameIds = next.Count == _groups.Count && next.Keys.All(_groups.ContainsKey);
        _groups.Clear();
        foreach (var pair in next)
        {
            _groups[pair.Key] = pair.Value;
        }

        if (sameIds)
        {
            return;
        }

        _evidence.RemoveWhere(entry => !_groups.ContainsKey(entry.GroupId));
        var stillEvidenced = new HashSet<string>(_evidence.Select(entry => entry.Uid), StringComparer.Ordinal);
        var forgotten = _sharing.Keys.Where(uid => !stillEvidenced.Contains(uid)).ToArray();
        foreach (var uid in forgotten)
        {
            _sharing.Remove(uid);
        }
    }

    public void ClearRestriction()
    {
        _restriction = null;
    }

    public void SetRestrictionName(string? groupName)
    {
        _restriction = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
    }

    public TeamRestrictResult TryRestrictTo(string groupName, out string canonicalName)
    {
        canonicalName = "";
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return TeamRestrictResult.NotAMember;
        }

        TeamGroup? match = null;
        foreach (var group in _groups.Values)
        {
            if (!group.Name.Equals(groupName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null && !match.Value.Name.Equals(group.Name, StringComparison.Ordinal))
            {
                return TeamRestrictResult.Ambiguous;
            }

            match = group;
        }

        if (match is null)
        {
            return TeamRestrictResult.NotAMember;
        }

        _restriction = match.Value.Name;
        canonicalName = match.Value.Name;
        return TeamRestrictResult.Restricted;
    }

    /// <summary>
    /// Apply one chat line. Fail closed: the <c>from:</c> entity id must match
    /// an online player whose entity is loaded, and that player's uid must
    /// match the payload. Share-on and share-off use the same rule. Anything
    /// else leaves evidence and share flags untouched.
    /// </summary>
    public ShareChatDisposition TryApplyChat(
        int groupId,
        string? message,
        string? data,
        IReadOnlyList<OnlinePlayer>? onlinePlayers)
    {
        if (!ShareAnnouncement.TryParseIncoming(message, data, out var parsed))
        {
            return ShareChatDisposition.Ignored;
        }

        if (parsed.SenderEntityId is not long entityId
            || !TryResolveLoadedSender(onlinePlayers, entityId, out var sender)
            || string.IsNullOrEmpty(sender.Uid)
            || !string.Equals(sender.Uid, parsed.Notice.PlayerUid, StringComparison.Ordinal))
        {
            return ShareChatDisposition.Unverified;
        }

        // Every group the local player belongs to, not only the .nav team
        // filter. An opt-out is posted to each of those channels and must
        // clear visibility even while the filter is narrower. A line on a
        // channel this client is not in does not count.
        if (!_groups.ContainsKey(groupId))
        {
            return ShareChatDisposition.NotInGroup;
        }

        _evidence.Add((groupId, parsed.Notice.PlayerUid));
        _sharing[parsed.Notice.PlayerUid] = parsed.Notice.Sharing;
        return ShareChatDisposition.Applied;
    }

    public IReadOnlyList<ShareRecord> RecordedShares()
    {
        var rows = new List<ShareRecord>(_evidence.Count + _sharing.Count);
        var evidenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _evidence)
        {
            evidenced.Add(entry.Uid);
            _sharing.TryGetValue(entry.Uid, out var sharing);
            rows.Add(new ShareRecord(entry.GroupId, entry.Uid, sharing));
        }

        foreach (var pair in _sharing)
        {
            if (!evidenced.Contains(pair.Key))
            {
                rows.Add(new ShareRecord(-1, pair.Key, pair.Value));
            }
        }

        rows.Sort(static (left, right) =>
        {
            var byGroup = left.GroupId.CompareTo(right.GroupId);
            if (byGroup != 0)
            {
                return byGroup;
            }

            var byUid = string.CompareOrdinal(left.PlayerUid, right.PlayerUid);
            if (byUid != 0)
            {
                return byUid;
            }

            return left.Sharing.CompareTo(right.Sharing);
        });
        return rows;
    }

    private static bool TryResolveLoadedSender(
        IReadOnlyList<OnlinePlayer>? onlinePlayers,
        long entityId,
        out OnlinePlayer sender)
    {
        sender = default;
        if (onlinePlayers is null)
        {
            return false;
        }

        foreach (var player in onlinePlayers)
        {
            if (player.EntityId != entityId || !player.EntityLoaded)
            {
                continue;
            }

            sender = player;
            return true;
        }

        return false;
    }

    public bool IsVisibleTeammate(string? uid)
    {
        if (string.IsNullOrEmpty(uid))
        {
            return false;
        }

        if (string.Equals(uid, _localUid, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_sharing.TryGetValue(uid, out var sharing) || !sharing)
        {
            return false;
        }

        foreach (var group in ActiveGroups())
        {
            if (_evidence.Contains((group.GroupId, uid)))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyCollection<string> GetVisibleTeammates()
    {
        var visible = new List<string>();
        foreach (var uid in _sharing.Keys)
        {
            if (IsVisibleTeammate(uid))
            {
                visible.Add(uid);
            }
        }

        visible.Sort(StringComparer.Ordinal);
        return visible;
    }

    public void Reset()
    {
        _groups.Clear();
        _evidence.Clear();
        _sharing.Clear();
        _localUid = null;
        _restriction = null;
    }

    private IEnumerable<TeamGroup> ActiveGroups()
    {
        if (_restriction is null)
        {
            return _groups.Values;
        }

        return _groups.Values.Where(group =>
            group.Name.Equals(_restriction, StringComparison.OrdinalIgnoreCase));
    }
}
