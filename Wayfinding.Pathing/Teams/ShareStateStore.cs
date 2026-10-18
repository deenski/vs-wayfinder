namespace Wayfinding.Pathing.Teams;

public sealed record WorldTeamPreference(string WorldId, bool Sharing, string? TeamGroup);

/// <summary>
/// Local share flag and optional team-group name, keyed by world id.
/// A missing world defaults to sharing on. Remote teammates are not stored
/// here; their evidence lives only for the session.
/// </summary>
public sealed class ShareStateStore
{
    private readonly Dictionary<string, WorldTeamPreference> _worlds = new(StringComparer.Ordinal);

    public bool IsSharing(string worldId)
    {
        if (string.IsNullOrEmpty(worldId) || !_worlds.TryGetValue(worldId, out var preference))
        {
            return true;
        }

        return preference.Sharing;
    }

    public void SetSharing(string worldId, bool sharing)
    {
        RequireWorldId(worldId);
        var team = TeamGroup(worldId);
        _worlds[worldId] = new WorldTeamPreference(worldId, sharing, team);
    }

    public string? TeamGroup(string worldId)
    {
        if (string.IsNullOrEmpty(worldId) || !_worlds.TryGetValue(worldId, out var preference))
        {
            return null;
        }

        return preference.TeamGroup;
    }

    public void SetTeamGroup(string worldId, string? teamGroup)
    {
        RequireWorldId(worldId);
        var sharing = IsSharing(worldId);
        var name = string.IsNullOrWhiteSpace(teamGroup) ? null : teamGroup.Trim();
        _worlds[worldId] = new WorldTeamPreference(worldId, sharing, name);
    }

    public IReadOnlyList<WorldTeamPreference> Export()
    {
        return _worlds.Values
            .OrderBy(preference => preference.WorldId, StringComparer.Ordinal)
            .ToArray();
    }

    public static ShareStateStore Import(IEnumerable<WorldTeamPreference>? records)
    {
        var store = new ShareStateStore();
        if (records is null)
        {
            return store;
        }

        foreach (var record in records)
        {
            if (record is null || string.IsNullOrEmpty(record.WorldId))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(record.TeamGroup) ? null : record.TeamGroup.Trim();
            store._worlds[record.WorldId] = new WorldTeamPreference(record.WorldId, record.Sharing, name);
        }

        return store;
    }

    private static void RequireWorldId(string worldId)
    {
        if (string.IsNullOrEmpty(worldId))
        {
            throw new ArgumentException("A world id is required.", nameof(worldId));
        }
    }
}
