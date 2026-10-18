using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Wayfinding.Pathing.Teams;

namespace Wayfinding.Teams;

/// <summary>
/// Client adapter. Reads the local player's groups, posts and receives share
/// announcements, and remembers the local share flag per world.
/// </summary>
public sealed class TeamService : ITeamService, IDisposable
{
    public const string ConfigName = "wayfinding-teams.json";

    // One group chat per interval. Dedicated servers rate-limit chat, and an
    // opt-out has to reach online teammates within 5 seconds. The first
    // channel goes out immediately; further channels wait this long.
    private const int SendSpacingMs = 1100;

    private readonly ICoreClientAPI _capi;
    private readonly ILogger _logger;
    private readonly TeamModel _model = new();
    private readonly Dictionary<int, PendingAnnouncement> _pending = new();

    private ShareStateStore _store = new();
    private long _tickListener = -1;
    private bool _ready;
    private bool _loggedWorldFallback;
    private string _worldId = "";
    private string _membershipKey = "";
    private string _visibleKey = "";
    private long _lastSendAt = long.MinValue;

    public TeamService(ICoreClientAPI capi, ILogger logger)
    {
        _capi = capi;
        _logger = logger;
    }

    public void Start()
    {
        RegisterCommands();
        _capi.Event.ChatMessage += OnChatMessage;
        _capi.Event.LevelFinalize += OnLevelFinalize;
        _capi.Event.LeaveWorld += OnLeaveWorld;
        _tickListener = _capi.Event.RegisterGameTickListener(OnTick, 200);
    }

    public void Dispose()
    {
        _capi.Event.ChatMessage -= OnChatMessage;
        _capi.Event.LevelFinalize -= OnLevelFinalize;
        _capi.Event.LeaveWorld -= OnLeaveWorld;
        if (_tickListener >= 0)
        {
            _capi.Event.UnregisterGameTickListener(_tickListener);
            _tickListener = -1;
        }
    }

    public bool IsVisibleTeammate(string? uid)
    {
        return _model.IsVisibleTeammate(uid);
    }

    public IReadOnlyCollection<string> GetVisibleTeammates()
    {
        return _model.GetVisibleTeammates();
    }

    private void RegisterCommands()
    {
        var nav = _capi.ChatCommands.GetOrCreate("nav");
        var parsers = _capi.ChatCommands.Parsers;

        nav.BeginSubCommand("share")
            .WithDescription("Tell teammate groups whether you are sharing your location.")
            .WithExamples(".nav share on", ".nav share off")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.Word("state"))
            .HandleWith(OnShare)
            .EndSubCommand();

        nav.BeginSubCommand("team")
            .WithDescription("Use every group you are in, or limit teammates to one group.")
            .WithExamples(".nav team", ".nav team scouts")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.OptionalAll("group"))
            .HandleWith(OnTeam)
            .EndSubCommand();
    }

    private TextCommandResult OnShare(TextCommandCallingArgs args)
    {
        if (!EnsureReady(out var notReady))
        {
            return notReady;
        }

        var raw = args.ArgCount > 0 ? args[0] as string : null;
        var word = (raw ?? "").Trim();
        bool sharing;
        if (word.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            sharing = true;
        }
        else if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            sharing = false;
        }
        else
        {
            return TextCommandResult.Error("Use .nav share on or .nav share off.");
        }

        _store.SetSharing(_worldId, sharing);
        SaveStore();
        EnqueueAnnouncements();
        NoteVisibility();

        var message = sharing
            ? "Sharing location with your groups."
            : "Location sharing is off.";
        if (_model.Membership.Count == 0)
        {
            message += " You are not in a group yet, so there is no group chat to tell.";
        }

        return TextCommandResult.Success(message);
    }

    private TextCommandResult OnTeam(TextCommandCallingArgs args)
    {
        if (!EnsureReady(out var notReady))
        {
            return notReady;
        }

        var requested = args.ArgCount > 0 ? args[0] as string : null;
        if (string.IsNullOrWhiteSpace(requested))
        {
            _store.SetTeamGroup(_worldId, null);
            _model.ClearRestriction();
            SaveStore();
            NoteVisibility();
            return TextCommandResult.Success("Teammates are everyone in your groups.");
        }

        var result = _model.TryRestrictTo(requested, out var canonical);
        if (result == TeamRestrictResult.NotAMember)
        {
            return TextCommandResult.Error("You are not in a group named \"" + requested.Trim() + "\".");
        }

        if (result == TeamRestrictResult.Ambiguous)
        {
            return TextCommandResult.Error("More than one group matches \"" + requested.Trim() + "\".");
        }

        _store.SetTeamGroup(_worldId, canonical);
        SaveStore();
        NoteVisibility();
        return TextCommandResult.Success("Teammates are limited to " + canonical + ".");
    }

    private void OnLevelFinalize()
    {
        TryEnterWorld();
    }

    private void OnLeaveWorld()
    {
        _ready = false;
        _pending.Clear();
        _membershipKey = "";
        _visibleKey = "";
        _worldId = "";
        _model.Reset();
    }

    private void OnTick(float dt)
    {
        if (!_ready)
        {
            TryEnterWorld();
        }

        if (!_ready)
        {
            return;
        }

        var groups = ReadGroups();
        var key = MembershipKey(groups);
        if (!string.Equals(key, _membershipKey, StringComparison.Ordinal))
        {
            ApplyMembership(groups, announce: true);
            NoteVisibility();
        }

        FlushPending();
    }

    private void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
    {
        if (!_ready)
        {
            return;
        }

        if (chattype is not (EnumChatType.OthersMessage or EnumChatType.OwnMessage))
        {
            return;
        }

        if (!ShareAnnouncement.TryParseIncoming(message, data, out var parsed))
        {
            return;
        }

        string? senderUid = null;
        if (parsed.SenderEntityId is long entityId)
        {
            senderUid = FindUidByEntityId(entityId);
        }

        if (_model.ApplyNotice(groupId, parsed.Notice, senderUid))
        {
            NoteVisibility();
        }
    }

    private void TryEnterWorld()
    {
        if (_ready)
        {
            return;
        }

        var player = _capi.World?.Player;
        if (player is null || string.IsNullOrEmpty(player.PlayerUID))
        {
            return;
        }

        _worldId = CurrentWorldId();
        _store = LoadStore();
        _model.SetLocalPlayer(player.PlayerUID);
        _ready = true;
        ApplyMembership(ReadGroups(), announce: true);
        NoteVisibility();
    }

    private void ApplyMembership(IReadOnlyList<TeamGroup> groups, bool announce)
    {
        _membershipKey = MembershipKey(groups);
        _model.SetMembership(groups);
        _model.SetRestrictionName(_store.TeamGroup(_worldId));
        if (announce)
        {
            EnqueueAnnouncements();
        }
    }

    private void EnqueueAnnouncements()
    {
        var player = _capi.World?.Player;
        if (player is null || string.IsNullOrEmpty(player.PlayerUID))
        {
            return;
        }

        var uid = player.PlayerUID;
        var name = string.IsNullOrWhiteSpace(player.PlayerName) ? "A player" : player.PlayerName;
        var sharing = _store.IsSharing(_worldId);
        var text = ShareAnnouncement.FormatChatMessage(name, sharing, uid);
        var data = ShareAnnouncement.FormatPayload(sharing, uid);
        var memberIds = new HashSet<int>();
        foreach (var group in _model.Membership)
        {
            memberIds.Add(group.GroupId);
            _pending[group.GroupId] = new PendingAnnouncement(text, data);
        }

        foreach (var groupId in _pending.Keys.ToArray())
        {
            if (!memberIds.Contains(groupId))
            {
                _pending.Remove(groupId);
            }
        }

        FlushPending();
    }

    private void FlushPending()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (_lastSendAt != long.MinValue && now - _lastSendAt < SendSpacingMs)
        {
            return;
        }

        var groupId = _pending.Keys.First();
        var pending = _pending[groupId];
        _pending.Remove(groupId);
        _lastSendAt = now;
        try
        {
            _capi.SendChatMessage(pending.Text, groupId, pending.Data);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not announce Wayfinding share state to group " + groupId + ": " + ex.Message);
        }
    }

    private List<TeamGroup> ReadGroups()
    {
        var groups = _capi.World?.Player?.Groups;
        var list = new List<TeamGroup>();
        if (groups is null)
        {
            return list;
        }

        foreach (var group in groups)
        {
            if (group is null || string.IsNullOrWhiteSpace(group.GroupName))
            {
                continue;
            }

            list.Add(new TeamGroup(group.GroupUid, group.GroupName));
        }

        return list;
    }

    private string? FindUidByEntityId(long entityId)
    {
        var players = _capi.World?.AllOnlinePlayers;
        if (players is null)
        {
            return null;
        }

        foreach (var player in players)
        {
            if (player?.Entity is null || player.Entity.EntityId != entityId)
            {
                continue;
            }

            return string.IsNullOrEmpty(player.PlayerUID) ? null : player.PlayerUID;
        }

        return null;
    }

    private string CurrentWorldId()
    {
        var identifier = _capi.World?.SavegameIdentifier;
        if (!string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        if (!_loggedWorldFallback)
        {
            _loggedWorldFallback = true;
            _logger.Warning("SavegameIdentifier was empty. Wayfinding share settings are keyed by world seed.");
        }

        var seed = _capi.World?.Seed ?? 0;
        return "seed:" + seed.ToString(CultureInfo.InvariantCulture);
    }

    private ShareStateStore LoadStore()
    {
        try
        {
            var config = _capi.LoadModConfig<TeamClientConfig>(ConfigName);
            if (config?.Worlds is null)
            {
                return new ShareStateStore();
            }

            var records = new List<WorldTeamPreference>();
            foreach (var world in config.Worlds)
            {
                if (world is null || string.IsNullOrEmpty(world.WorldId))
                {
                    continue;
                }

                records.Add(new WorldTeamPreference(world.WorldId, world.Sharing, world.TeamGroup));
            }

            return ShareStateStore.Import(records);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not read Wayfinding team settings: " + ex.Message);
            return new ShareStateStore();
        }
    }

    private void SaveStore()
    {
        try
        {
            var worlds = new List<TeamWorldRecord>();
            foreach (var preference in _store.Export())
            {
                worlds.Add(new TeamWorldRecord
                {
                    WorldId = preference.WorldId,
                    Sharing = preference.Sharing,
                    TeamGroup = preference.TeamGroup,
                });
            }

            _capi.StoreModConfig(new TeamClientConfig { Worlds = worlds.ToArray() }, ConfigName);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not save Wayfinding team settings: " + ex.Message);
        }
    }

    private bool EnsureReady(out TextCommandResult result)
    {
        if (_ready)
        {
            result = TextCommandResult.Success();
            return true;
        }

        result = TextCommandResult.Error("Join a world before using Wayfinding team commands.");
        return false;
    }

    private void NoteVisibility()
    {
        var visible = _model.GetVisibleTeammates();
        var key = string.Join(",", visible);
        if (string.Equals(key, _visibleKey, StringComparison.Ordinal))
        {
            return;
        }

        _visibleKey = key;
        _logger.Notification(visible.Count == 0
            ? "Wayfinding teammates visible: none"
            : "Wayfinding teammates visible: " + key);
    }

    private static string MembershipKey(IReadOnlyList<TeamGroup> groups)
    {
        return string.Join("|", groups
            .OrderBy(group => group.GroupId)
            .ThenBy(group => group.Name, StringComparer.Ordinal)
            .Select(group => group.GroupId.ToString(CultureInfo.InvariantCulture) + "=" + group.Name));
    }

    private readonly record struct PendingAnnouncement(string Text, string Data);
}

public sealed class TeamClientConfig
{
    public TeamWorldRecord[] Worlds = [];
}

public sealed class TeamWorldRecord
{
    public string WorldId = "";
    public bool Sharing = true;
    public string? TeamGroup;
}
