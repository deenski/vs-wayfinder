using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wayfinding.Infra;

public readonly struct NavReply
{
    private NavReply(bool ok, string message)
    {
        Ok = ok;
        Message = message;
    }

    public bool Ok { get; }

    public string Message { get; }

    public static NavReply Success(string message)
    {
        return new NavReply(true, message);
    }

    public static NavReply Fail(string message)
    {
        return new NavReply(false, message);
    }

    public static NavReply Disabled()
    {
        return Fail(NavTargets.MapDisabledMessage);
    }
}

/// <summary>
/// Navigation target for one world.
/// Main-thread only. <see cref="Current"/> is the field <c>current</c>, and both
/// are read and written only on the game thread. <c>current</c> is not volatile:
/// nothing reads it from another thread, and KAN-19 must keep reading it on the
/// game thread too.
/// TODO(KAN-19): WayfindingMapLayer reads <see cref="Current"/> and subscribes to
/// <see cref="Changed"/> to draw the route. Draw nothing while <see cref="MapAllowed"/>
/// is false. Ctrl+click and hotkeys call <see cref="SetWaypoint"/>, <see cref="SetCoordinate"/>,
/// and <see cref="Off"/>. Call <see cref="NotifyWaypointDeleted"/> when the tracked waypoint is removed.
/// </summary>
public sealed class NavTargets
{
    public const string MapDisabledMessage =
        "Wayfinding navigation is disabled because this server has the map turned off (allowMap=false).";

    public const string WorldNotReadyMessage = "The world isn't ready yet.";

    public const string TeammateSharingUnavailableMessage = "Teammate sharing isn't available yet.";

    public static string NotVisibleTeammateMessage(string name)
    {
        return "\"" + name + "\" isn't a visible teammate.";
    }

    private readonly INavTargetStore store;
    private readonly ITeammateVisibility teammates;
    private NavTargetDocument document = new();
    private string? worldId;
    // Main-thread only. There is no cross-thread reader.
    private NavTarget current = NavTarget.None;
    private bool seenWaypointList;

    public NavTargets(INavTargetStore store, ITeammateVisibility? teammates = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.teammates = teammates ?? NoTeammateVisibility.Instance;
    }

    public NavTarget Current => current;

    public bool MapAllowed { get; private set; } = true;

    public event Action? Changed;

    public void SetMapAllowed(bool allowed)
    {
        if (MapAllowed == allowed)
        {
            return;
        }

        MapAllowed = allowed;
        Changed?.Invoke();
    }

    public void AttachWorld(string worldId)
    {
        if (string.IsNullOrWhiteSpace(worldId))
        {
            throw new ArgumentException("A world id is required.", nameof(worldId));
        }

        var trimmed = worldId.Trim();
        if (string.Equals(this.worldId, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        document = store.Load() ?? new NavTargetDocument();
        this.worldId = trimmed;
        current = document.Read(trimmed);
        seenWaypointList = false;
        Changed?.Invoke();
    }

    public void DetachWorld()
    {
        if (worldId is null && current.Kind == NavTargetKind.None)
        {
            return;
        }

        worldId = null;
        current = NavTarget.None;
        seenWaypointList = false;
        Changed?.Invoke();
    }

    public NavReply SelectWaypoint(string? argument, IReadOnlyList<WaypointRef> waypoints)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        ArgumentNullException.ThrowIfNull(waypoints);
        var selected = WaypointSelector.Select(argument, waypoints);
        if (!selected.Ok)
        {
            return NavReply.Fail(selected.Message);
        }

        return SetWaypoint(selected.Waypoint.Id, selected.Waypoint.Name);
    }

    public NavReply SetWaypoint(Guid id, string? name)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        if (id == Guid.Empty)
        {
            return NavReply.Fail("That waypoint has no id, so it can't be tracked.");
        }

        var title = name ?? "";
        current = NavTarget.Waypoint(id, title);
        Persist();
        Changed?.Invoke();
        return NavReply.Success(title.Length == 0
            ? "Navigating to waypoint " + id.ToString("D") + "."
            : "Navigating to waypoint \"" + title + "\".");
    }

    public NavReply Player(string? argument)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        if (!PlayerNameParser.TryParse(argument, out var name, out var error))
        {
            return NavReply.Fail(error);
        }

        // Check before storing the name. Callers must not resolve an online
        // player by name unless this returns true.
        if (!teammates.IsVisibleTeammate(name))
        {
            var message = teammates is NoTeammateVisibility
                ? TeammateSharingUnavailableMessage
                : NotVisibleTeammateMessage(name);
            return NavReply.Fail(message);
        }

        current = NavTarget.Player(name);
        Persist();
        Changed?.Invoke();
        return NavReply.Success("Navigating to player \"" + name + "\".");
    }

    public NavReply Coord(string? text, SpawnOrigin spawn, int mapHeight, int? dimension = null)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        if (!NavCoordParser.TryParse(text, spawn, mapHeight, out var parsed, out var error))
        {
            return NavReply.Fail(error);
        }

        return SetCoordinate(parsed.Absolute, spawn, dimension);
    }

    /// <summary>
    /// Sets a coordinate target. <paramref name="dimension"/> is the local player's
    /// dimension at that moment. Null leaves it unconfirmed, which is also how a
    /// coordinate saved before stamping existed is read back.
    /// </summary>
    public NavReply SetCoordinate(AbsoluteCoord coordinate, SpawnOrigin? spawn, int? dimension = null)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        current = NavTarget.At(coordinate, dimension);
        Persist();
        Changed?.Invoke();
        return NavReply.Success(NavigatingCoordinate(coordinate, spawn));
    }

    public NavReply Off()
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        if (current.Kind == NavTargetKind.None)
        {
            return NavReply.Success("No navigation target.");
        }

        Clear();
        return NavReply.Success("Navigation target cleared.");
    }

    public NavReply Status(SpawnOrigin? spawn, IReadOnlyList<WaypointRef>? liveWaypoints)
    {
        if (!MapAllowed)
        {
            return NavReply.Disabled();
        }

        if (!Ready(out var notReady))
        {
            return notReady;
        }

        if (liveWaypoints != null && NoteWaypointList(liveWaypoints))
        {
            return NavReply.Success("The tracked waypoint was deleted, so the navigation target was cleared.");
        }

        return NavReply.Success(Describe(spawn));
    }

    /// <summary>
    /// Model half of design §8.1 AC4. Deleting the tracked waypoint clears the target.
    /// </summary>
    public bool NotifyWaypointDeleted(Guid waypointId)
    {
        if (worldId is null || current.Kind != NavTargetKind.Waypoint || current.WaypointId != waypointId)
        {
            return false;
        }

        Clear();
        return true;
    }

    /// <summary>
    /// Applies a waypoint list the client has actually received.
    /// An empty list before any populated list does not clear a persisted target,
    /// because the client list stays empty until the server sends it.
    /// </summary>
    public bool NoteWaypointList(IReadOnlyList<WaypointRef> live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (worldId is null)
        {
            return false;
        }

        if (live.Count > 0)
        {
            seenWaypointList = true;
        }

        if (current.Kind != NavTargetKind.Waypoint)
        {
            return false;
        }

        for (var i = 0; i < live.Count; i++)
        {
            if (live[i].HasId && live[i].Id == current.WaypointId)
            {
                if (!string.Equals(live[i].Name, current.Name, StringComparison.Ordinal))
                {
                    current = NavTarget.Waypoint(live[i].Id, live[i].Name);
                    Persist();
                    Changed?.Invoke();
                }

                return false;
            }
        }

        if (live.Count == 0 && !seenWaypointList)
        {
            return false;
        }

        Clear();
        return true;
    }

    public string Describe(SpawnOrigin? spawn)
    {
        switch (current.Kind)
        {
            case NavTargetKind.Waypoint:
                return string.IsNullOrEmpty(current.Name)
                    ? "Target: waypoint " + current.WaypointId.ToString("D")
                    : "Target: waypoint \"" + current.Name + "\" " + current.WaypointId.ToString("D");
            case NavTargetKind.Player:
                return "Target: player \"" + current.Name + "\"";
            case NavTargetKind.Coordinate:
                return "Target: coordinate " + FormatCoordinate(current.Coordinate!.Value, spawn);
            default:
                return "No navigation target.";
        }
    }

    private bool Ready(out NavReply reply)
    {
        if (worldId is not null)
        {
            reply = default;
            return true;
        }

        reply = NavReply.Fail(WorldNotReadyMessage);
        return false;
    }

    /// <summary>
    /// Drops the active target and deletes its persisted record, including a waypoint Guid.
    /// Arrival uses this. Returns false when no world is attached or no target is set.
    /// </summary>
    public bool ClearTarget()
    {
        if (worldId is null || current.Kind == NavTargetKind.None)
        {
            return false;
        }

        Clear();
        return true;
    }

    private void Clear()
    {
        current = NavTarget.None;
        Persist();
        Changed?.Invoke();
    }

    private void Persist()
    {
        document ??= new NavTargetDocument();
        document.Write(worldId!, current);
        store.Save(document);
    }

    private static string NavigatingCoordinate(AbsoluteCoord coordinate, SpawnOrigin? spawn)
    {
        return "Navigating to " + FormatCoordinate(coordinate, spawn) + ".";
    }

    private static string FormatCoordinate(AbsoluteCoord coordinate, SpawnOrigin? spawn)
    {
        if (spawn is { } origin
            && TrySubtract(coordinate.X, origin.X, out var relativeX)
            && TrySubtract(coordinate.Z, origin.Z, out var relativeZ))
        {
            var absolute = coordinate.Y is int y
                ? Num(coordinate.X) + ", " + Num(y) + ", " + Num(coordinate.Z)
                : Num(coordinate.X) + ", " + Num(coordinate.Z);
            var display = coordinate.Y is int shownY
                ? Num(relativeX) + ", " + Num(shownY) + ", " + Num(relativeZ)
                : Num(relativeX) + ", " + Num(relativeZ);
            return display + " (absolute " + absolute + ")";
        }

        var body = coordinate.Y is int absY
            ? Num(coordinate.X) + ", " + Num(absY) + ", " + Num(coordinate.Z)
            : Num(coordinate.X) + ", " + Num(coordinate.Z);
        return "absolute " + body;
    }

    private static bool TrySubtract(int absolute, int spawn, out int relative)
    {
        var value = (long)absolute - spawn;
        if (value < int.MinValue || value > int.MaxValue)
        {
            relative = 0;
            return false;
        }

        relative = (int)value;
        return true;
    }

    private static string Num(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
