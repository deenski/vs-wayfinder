namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Membership, announcement evidence, and the visibility filter.
/// A remote player is visible only when the local player is in a team group,
/// an announcement from that player was seen in that group's channel, and
/// the latest announcement has sharing on. Unknown players are hidden.
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

    public bool TryApplyChat(int groupId, string? message, string? data, string? senderUid)
    {
        if (!ShareAnnouncement.TryParseIncoming(message, data, out var parsed))
        {
            return false;
        }

        return ApplyNotice(groupId, parsed.Notice, senderUid);
    }

    public bool ApplyNotice(int groupId, ShareNotice notice, string? senderUid)
    {
        if (string.IsNullOrEmpty(notice.PlayerUid))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(senderUid)
            && !string.Equals(senderUid, notice.PlayerUid, StringComparison.Ordinal))
        {
            return false;
        }

        // Every group the local player belongs to, not only the .nav team
        // filter. An opt-out is posted to each of those channels and must
        // clear visibility even while the filter is narrower.
        if (!_groups.ContainsKey(groupId))
        {
            return false;
        }

        _evidence.Add((groupId, notice.PlayerUid));
        _sharing[notice.PlayerUid] = notice.Sharing;
        return true;
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
