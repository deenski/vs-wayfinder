using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Wayfinding.Infra;
using Wayfinding.Pathing.Teams;

namespace Wayfinding.Teams;

/// <summary>
/// Client adapter. Reads the local player's groups, posts and receives share
/// announcements, and remembers the local share flag per world.
/// </summary>
public sealed class TeamService : ITeamService, IDisposable
{
    public const string ConfigName = "wayfinding-teams.json";

    private readonly ICoreClientAPI _capi;
    private readonly IWayfindingLog _log;
    private readonly GameTickClock _clock = new();
    private readonly TeamModel _model = new();
    private readonly ShareOutbox _outbox;
    private readonly ShareSession _session;

    private ShareStateStore _store = new();
    private long _tickListener = -1;
    private bool _ready;
    private bool _loggedWorldFallback;
    private string _worldId = "";
    private string _membershipKey = "";
    private string _visibleKey = "";

    public TeamService(ICoreClientAPI capi, IWayfindingLog log)
    {
        _capi = capi;
        _log = log;
        _outbox = new ShareOutbox(_clock);
        _session = new ShareSession(_model, _outbox);
    }

    public void Start()
    {
        _capi.Event.ChatMessage += OnChatMessage;
        _capi.Event.LevelFinalize += OnLevelFinalize;
        _capi.Event.LeaveWorld += OnLeaveWorld;
        _tickListener = _capi.Event.RegisterGameTickListener(OnTick, 200);
    }

    public void Dispose()
    {
        _ready = false;
        _outbox.Clear();
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

    public void RegisterSubcommands(IChatCommand nav)
    {
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
        PublishLocal();
        _session.AnnounceToMembership();
        FlushPending();
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
        _outbox.Clear();
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
            ApplyMembership(groups);
            NoteVisibility();
        }

        FlushPending();
    }

    private void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
    {
        // The server replaces client chat data. The payload is read only from
        // the text after the server name prefix.
        _ = data;
        if (!_ready)
        {
            return;
        }

        var kind = chattype switch
        {
            EnumChatType.OwnMessage => ShareChatKind.Own,
            EnumChatType.OthersMessage => ShareChatKind.Other,
            _ => ShareChatKind.Ignored,
        };

        PublishLocal();
        var receipt = _session.Receive(kind, groupId, message, ReadOnlinePlayers());
        if (receipt.Disposition == ShareChatDisposition.Applied)
        {
            NoteVisibility();
        }
        else if (receipt.Disposition == ShareChatDisposition.Unverified)
        {
            NoteUnverified(receipt);
        }

        FlushPending();
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
        ApplyMembership(ReadGroups());
        NoteVisibility();
    }

    private void ApplyMembership(IReadOnlyList<TeamGroup> groups)
    {
        _membershipKey = MembershipKey(groups);
        PublishLocal();
        _session.ReplaceMembership(groups);
        _model.SetRestrictionName(_store.TeamGroup(_worldId));
        FlushPending();
    }

    private void PublishLocal()
    {
        var name = _capi.World?.Player?.PlayerName;
        _session.LocalName = string.IsNullOrWhiteSpace(name) ? "" : name;
        _session.Sharing = _store.IsSharing(_worldId);
    }

    private void FlushPending()
    {
        if (!_outbox.TryTake(out var groupId, out var text))
        {
            return;
        }

        var data = ShareAnnouncement.TryParsePayload(text, out var notice)
            ? ShareAnnouncement.FormatPayload(notice.Sharing, notice.PlayerUid)
            : "";
        try
        {
            _capi.SendChatMessage(text, groupId, data);
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Warning, "Could not announce Wayfinding share state to group " + groupId + ": " + ex.Message);
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

    private List<OnlinePlayer> ReadOnlinePlayers()
    {
        var list = new List<OnlinePlayer>();
        var players = _capi.World?.AllOnlinePlayers;
        if (players is null)
        {
            return list;
        }

        foreach (var player in players)
        {
            // Name and uid are present for players whose entities are not loaded.
            if (player is null || string.IsNullOrEmpty(player.PlayerUID) || string.IsNullOrEmpty(player.PlayerName))
            {
                continue;
            }

            list.Add(new OnlinePlayer(player.PlayerName, player.PlayerUID));
        }

        return list;
    }

    private void NoteUnverified(ShareReceipt receipt)
    {
        var key = receipt.SenderName.Length > 0
            ? "share-name:" + receipt.SenderName
            : receipt.PayloadUid.Length > 0
                ? "share-payload:" + receipt.PayloadUid
                : "share-unresolved";
        _log.LogOnce(key, LogLevel.Debug, "Ignored an unverified share announcement.");
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
            _log.Log(LogLevel.Warning, "SavegameIdentifier was empty. Wayfinding share settings are keyed by world seed.");
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
            _log.Log(LogLevel.Warning, "Could not read Wayfinding team settings: " + ex.Message);
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
            _log.Log(LogLevel.Warning, "Could not save Wayfinding team settings: " + ex.Message);
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
        _log.Log(
            LogLevel.Debug,
            visible.Count == 0
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

    private sealed class GameTickClock : ITeamClock
    {
        public long Milliseconds => Environment.TickCount64;
    }
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
