using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Wayfinding.Infra;

namespace Wayfinding;

public static class NavTargetCommands
{
    public static ClientNavSession Register(ICoreClientAPI capi, WayfindingModSystem mod)
    {
        var session = new ClientNavSession(capi, mod);
        session.Start();

        // Same root as stats and debug. GetOrCreate does not replace that root.
        var parsers = capi.ChatCommands.Parsers;
        capi.ChatCommands
            .GetOrCreate("nav")
            .BeginSubCommand("waypoint")
                .WithDescription("Navigate to a waypoint by name or by its /waypoint list index.")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(parsers.OptionalAll("name"))
                .HandleWith(args => session.Waypoint(ArgText(args)))
            .EndSubCommand()
            .BeginSubCommand("player")
                .WithDescription("Navigate to a visible teammate by name.")
                .WithAdditionalInformation("The name is accepted only when teammate visibility allows it. Until team sharing is connected, this reports that teammate sharing isn't available yet.")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(parsers.OptionalAll("name"))
                .HandleWith(args => session.Player(ArgText(args)))
            .EndSubCommand()
            .BeginSubCommand("coord")
                .WithDescription("Navigate to coordinates. X and Z are spawn-relative, the way the map shows them. Y is absolute height.")
                .WithAdditionalInformation("X and Z are spawn-relative: absolute equals the displayed number plus the spawn block, matching the map and /waypoint list. Y, when given, is absolute height and is not offset by spawn.")
                .WithExamples(".nav coord 10 -4", ".nav coord 10 70 -4")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(parsers.OptionalAll("coordinates"))
                .HandleWith(args => session.Coord(ArgText(args)))
            .EndSubCommand()
            .BeginSubCommand("off")
                .WithDescription("Clear the navigation target.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => session.Off())
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("Show the current navigation target.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(_ => session.Status())
            .EndSubCommand();

        return session;
    }

    private static string ArgText(TextCommandCallingArgs args)
    {
        if (args.ArgCount <= 0)
        {
            return "";
        }

        return args[0] as string ?? "";
    }
}

/// <summary>
/// Client adapter for <see cref="NavTargets"/>. The target model denies every
/// player until a teammate-visibility implementation is passed in. After KAN-21
/// merges, that is one constructor argument: the team service, which already
/// has <c>IsVisibleTeammate</c>.
/// </summary>
public sealed class ClientNavSession
{
    private readonly ICoreClientAPI capi;
    private readonly WayfindingModSystem mod;
    private long tickListener = -1;

    public ClientNavSession(ICoreClientAPI capi, WayfindingModSystem mod)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.mod = mod ?? throw new ArgumentNullException(nameof(mod));
        // NoTeammateVisibility until KAN-21. Then: new NavTargets(store, teamService).
        Targets = new NavTargets(new ClientNavTargetStore(capi, mod.Log));
    }

    public NavTargets Targets { get; }

    public void Start()
    {
        if (tickListener >= 0)
        {
            return;
        }

        tickListener = capi.Event.RegisterGameTickListener(OnTick, 1000);
    }

    public void OnLevelFinalize()
    {
        TryAttach();
        RefreshMapAllowed();
    }

    public void OnLeaveWorld()
    {
        Targets.DetachWorld();
    }

    public void Dispose()
    {
        if (tickListener < 0)
        {
            return;
        }

        capi.Event.UnregisterGameTickListener(tickListener);
        tickListener = -1;
    }

    public TextCommandResult Waypoint(string argument)
    {
        if (!Prepare(out var blocked))
        {
            return blocked;
        }

        if (!TryReadWaypoints(out var waypoints, out var error))
        {
            return TextCommandResult.Error(error);
        }

        Targets.NoteWaypointList(waypoints);
        return Reply(Targets.SelectWaypoint(argument, waypoints));
    }

    public TextCommandResult Player(string argument)
    {
        if (!Prepare(out var blocked))
        {
            return blocked;
        }

        // Do not look up an online player by name here. NavTargets.Player
        // checks ITeammateVisibility first and only then stores the name.
        return Reply(Targets.Player(argument));
    }

    public TextCommandResult Coord(string argument)
    {
        if (!Prepare(out var blocked))
        {
            return blocked;
        }

        if (!TrySpawn(out var spawn, out var error))
        {
            return TextCommandResult.Error(error);
        }

        return Reply(Targets.Coord(argument, spawn));
    }

    public TextCommandResult Off()
    {
        if (!Prepare(out var blocked))
        {
            return blocked;
        }

        return Reply(Targets.Off());
    }

    public TextCommandResult Status()
    {
        if (!Prepare(out var blocked))
        {
            return blocked;
        }

        IReadOnlyList<WaypointRef>? live = null;
        if (TryReadWaypoints(out var waypoints, out _))
        {
            live = waypoints;
        }

        SpawnOrigin? spawn = null;
        if (TrySpawn(out var origin, out _))
        {
            spawn = origin;
        }

        return Reply(Targets.Status(spawn, live));
    }

    private void OnTick(float dt)
    {
        if (!TryAttach())
        {
            return;
        }

        RefreshMapAllowed();
        if (!Targets.MapAllowed || Targets.Current.Kind != NavTargetKind.Waypoint)
        {
            return;
        }

        if (TryReadWaypoints(out var waypoints, out _))
        {
            Targets.NoteWaypointList(waypoints);
        }
    }

    private bool Prepare(out TextCommandResult blocked)
    {
        if (!TryAttach())
        {
            blocked = TextCommandResult.Error(NavTargets.WorldNotReadyMessage);
            return false;
        }

        RefreshMapAllowed();
        if (!Targets.MapAllowed)
        {
            blocked = TextCommandResult.Error(NavTargets.MapDisabledMessage);
            return false;
        }

        blocked = default!;
        return true;
    }

    private bool TryAttach()
    {
        var id = capi.World?.SavegameIdentifier;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        Targets.AttachWorld(id);
        return true;
    }

    private void RefreshMapAllowed()
    {
        var world = capi.World;
        if (world?.Config == null)
        {
            return;
        }

        // Same rule as WorldMapManager.mapAllowedClient: the world config, or the allowMap privilege.
        var allowed = world.Config.GetBool("allowMap", true);
        if (!allowed)
        {
            var privileges = world.Player?.Privileges;
            allowed = privileges != null && Array.IndexOf(privileges, "allowMap") >= 0;
        }

        Targets.SetMapAllowed(allowed);
    }

    private bool TrySpawn(out SpawnOrigin spawn, out string error)
    {
        var pos = capi.World?.DefaultSpawnPosition;
        if (pos == null)
        {
            spawn = default;
            error = "The spawn position isn't known yet.";
            return false;
        }

        var block = pos.AsBlockPos;
        spawn = new SpawnOrigin(block.X, block.Z);
        error = "";
        return true;
    }

    private bool TryReadWaypoints(out IReadOnlyList<WaypointRef> waypoints, out string error)
    {
        if (!mod.IsEnabled(Feature.MapLayer) || !mod.IsEnabled(Feature.Waypoints))
        {
            waypoints = Array.Empty<WaypointRef>();
            error = "Waypoint targets are disabled on this game version.";
            return false;
        }

        return ReadWaypointList(out waypoints, out error);
    }

    private bool ReadWaypointList(out IReadOnlyList<WaypointRef> waypoints, out string error)
    {
        var manager = capi.ModLoader.GetModSystem<WorldMapManager>();
        if (manager?.MapLayers == null)
        {
            waypoints = Array.Empty<WaypointRef>();
            error = "Waypoints are not loaded yet.";
            return false;
        }

        WaypointMapLayer? layer = null;
        for (var i = 0; i < manager.MapLayers.Count; i++)
        {
            if (manager.MapLayers[i] is WaypointMapLayer found)
            {
                layer = found;
                break;
            }
        }

        if (layer?.ownWaypoints == null)
        {
            waypoints = Array.Empty<WaypointRef>();
            error = "Waypoints are not loaded yet.";
            return false;
        }

        var list = new List<WaypointRef>(layer.ownWaypoints.Count);
        for (var i = 0; i < layer.ownWaypoints.Count; i++)
        {
            var wp = layer.ownWaypoints[i];
            if (wp == null || string.IsNullOrEmpty(wp.Guid) || !Guid.TryParse(wp.Guid, out var id) || id == Guid.Empty)
            {
                list.Add(WaypointRef.Missing(wp?.Title));
                continue;
            }

            list.Add(new WaypointRef(id, wp.Title));
        }

        waypoints = list;
        error = "";
        return true;
    }

    private static TextCommandResult Reply(NavReply reply)
    {
        return reply.Ok
            ? TextCommandResult.Success(reply.Message)
            : TextCommandResult.Error(reply.Message);
    }
}
