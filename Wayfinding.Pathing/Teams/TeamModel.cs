namespace Wayfinding.Pathing.Teams;

/// <summary>
/// Membership, announcement evidence, and the visibility filter.
/// A remote player is visible only when the local player is in a team group,
/// a verified announcement from that player was seen in that group's channel,
/// and the latest verified announcement has sharing on. Unknown players are
/// hidden. The sender is the name in the server chat prefix, mapped through
/// the online player list. An unverified share-on is dropped. An unverified
/// share-off still hides that uid, and does not add teammate evidence.
/// </summary>
public sealed class TeamModel : ITeamService
{
    private readonly Dictionary<int, TeamGroup> _groups = new();
    private readonly HashSet<(int GroupId, string Uid)> _evidence = new();
    private readonly Dictionary<string, bool> _sharing = new(StringComparer.Ordinal);

    private string? _localUid;
    private string? _restriction;

    /// <summary>Uid of the local player, or null before one is known.</summary>
    internal string? LocalPlayerUid => _localUid;

    /// <summary>Group name selected with <c>.nav team</c>, or null for every membership group.</summary>
    internal string? TeamRestriction => _restriction;

    /// <summary>Groups the local player is in.</summary>
    internal IReadOnlyCollection<TeamGroup> Membership => _groups.Values.ToArray();

    /// <summary>Sets the local player. An empty uid clears it. The local player is never visible.</summary>
    internal void SetLocalPlayer(string? uid)
    {
        _localUid = string.IsNullOrEmpty(uid) ? null : uid;
    }

    /// <summary>
    /// Replaces membership. When the set of group ids changes, evidence for groups
    /// that were left is dropped, and share flags with no remaining evidence are dropped.
    /// </summary>
    internal void SetMembership(IReadOnlyList<TeamGroup> groups)
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

    /// <summary>Shows teammates from every membership group again.</summary>
    internal void ClearRestriction()
    {
        _restriction = null;
    }

    /// <summary>Sets the team limit by name without checking membership. Whitespace clears it.</summary>
    internal void SetRestrictionName(string? groupName)
    {
        _restriction = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
    }

    /// <summary>Limits visibility to one membership group. The stored name is the group's own spelling.</summary>
    internal TeamRestrictResult TryRestrictTo(string groupName, out string canonicalName)
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
    /// Apply one chat line. The server name prefix must map to one online
    /// player, and that player's uid must match the payload. The payload is
    /// read only from the text after the prefix. An unverified share-on is
    /// dropped. An unverified share-off in a group this client belongs to hides
    /// that uid without adding evidence. General chat and other groups do not.
    /// </summary>
    internal ShareChatDisposition TryApplyChat(
        int groupId,
        string? message,
        IReadOnlyList<OnlinePlayer>? onlinePlayers)
    {
        var resolution = ServerShareLine.Resolve(message, onlinePlayers);
        if (resolution.Status == ShareLineStatus.None)
        {
            return ShareChatDisposition.Ignored;
        }

        if (resolution.Status != ShareLineStatus.Verified)
        {
            if (!resolution.Notice.Sharing
                && !string.IsNullOrEmpty(resolution.Notice.PlayerUid)
                && _groups.ContainsKey(groupId))
            {
                _sharing[resolution.Notice.PlayerUid] = false;
            }

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

        _evidence.Add((groupId, resolution.Notice.PlayerUid));
        _sharing[resolution.Notice.PlayerUid] = resolution.Notice.Sharing;
        return ShareChatDisposition.Applied;
    }

    /// <summary>True when this group has already seen an announcement from that uid.</summary>
    internal bool HasEvidence(int groupId, string uid)
    {
        return !string.IsNullOrEmpty(uid) && _evidence.Contains((groupId, uid));
    }

    /// <summary>Recorded evidence and share flags, in a stable order. Group id -1 has no evidence left.</summary>
    internal IReadOnlyList<ShareRecord> RecordedShares()
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

    /// <inheritdoc />
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

    /// <summary>
    /// True when <paramref name="playerNameOrUid"/> is a visible teammate uid,
    /// or an online name that maps to exactly one such uid. An unknown name is hidden.
    /// </summary>
    /// <param name="playerNameOrUid">Player name or uid, as <c>.nav player</c> receives it.</param>
    /// <param name="onlinePlayers">Online players used for the name lookup. Null hides every name.</param>
    internal bool IsVisibleNameOrUid(string? playerNameOrUid, IReadOnlyList<OnlinePlayer>? onlinePlayers)
    {
        if (IsVisibleTeammate(playerNameOrUid))
        {
            return true;
        }

        var uid = OnlinePlayers.UidForName(playerNameOrUid, onlinePlayers);
        return uid is not null && IsVisibleTeammate(uid);
    }

    /// <inheritdoc />
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

    /// <summary>Clears membership, evidence, share flags, the local uid, and the team limit.</summary>
    internal void Reset()
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
