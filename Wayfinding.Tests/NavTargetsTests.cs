using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class NavTargetsTests
{
    private static readonly Guid HomeId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly Guid CampId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    private static readonly SpawnOrigin Spawn = new(512000, 512000);
    private const int MapHeight = 256;

    [Fact]
    public void Set_clear_and_status_for_each_kind()
    {
        var nav = Open(teammates: new NamedTeammates("Maili"));

        var waypoint = nav.SetWaypoint(HomeId, "Home");
        Assert.True(waypoint.Ok);
        Assert.Equal("Navigating to waypoint \"Home\".", waypoint.Message);
        Assert.Equal("Target: waypoint \"Home\" " + HomeId.ToString("D"), nav.Status(Spawn, null).Message);

        var player = nav.Player("Maili");
        Assert.True(player.Ok);
        Assert.Equal(NavTargetKind.Player, nav.Current.Kind);
        Assert.Equal("Target: player \"Maili\"", nav.Status(null, null).Message);

        var coord = nav.Coord("10 70 -4", Spawn, MapHeight);
        Assert.True(coord.Ok);
        Assert.Equal("Navigating to 10, 70, -4 (absolute 512010, 70, 511996).", coord.Message);
        Assert.Equal(
            "Target: coordinate 10, 70, -4 (absolute 512010, 70, 511996)",
            nav.Status(Spawn, null).Message);

        var cleared = nav.Off();
        Assert.True(cleared.Ok);
        Assert.Equal("Navigation target cleared.", cleared.Message);
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
        Assert.Equal("No navigation target.", nav.Status(Spawn, null).Message);
        Assert.Equal("No navigation target.", nav.Off().Message);
    }

    [Fact]
    public void X_z_target_status_omits_y()
    {
        var nav = Open();
        nav.Coord("10 -4", Spawn, MapHeight);

        Assert.Null(nav.Current.Coordinate!.Value.Y);
        Assert.Equal(
            "Target: coordinate 10, -4 (absolute 512010, 511996)",
            nav.Status(Spawn, null).Message);
    }

    [Fact]
    public void Waypoint_target_round_trips_by_guid()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");

        var restored = Open(store);
        Assert.Equal(NavTargetKind.Waypoint, restored.Current.Kind);
        Assert.Equal(HomeId, restored.Current.WaypointId);
        Assert.Equal("Home", restored.Current.Name);
        Assert.Equal(HomeId.ToString("D"), store.Document.Worlds[0].WaypointId);
    }

    [Fact]
    public void Coordinate_round_trip_keeps_a_missing_y_distinct_from_y_zero()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store, "heights");
        nav.SetCoordinate(new AbsoluteCoord(5, null, 6), null);

        var restored = Open(store, "heights");
        Assert.Equal(NavTargetKind.Coordinate, restored.Current.Kind);
        Assert.False(restored.Current.Coordinate!.Value.Y.HasValue);
        Assert.Equal("Target: coordinate absolute 5, 6", restored.Status(null, null).Message);

        restored.SetCoordinate(new AbsoluteCoord(5, 0, 6), null);
        var withZero = Open(store, "heights");
        Assert.Equal(0, withZero.Current.Coordinate!.Value.Y);
    }

    [Fact]
    public void Worlds_do_not_share_a_target()
    {
        var store = new MemoryNavTargetStore();
        var first = Open(store, "save-a");
        first.SetWaypoint(HomeId, "Home");

        var second = Open(store, "save-b");
        Assert.Equal(NavTargetKind.None, second.Current.Kind);

        var again = Open(store, "save-a");
        Assert.Equal(HomeId, again.Current.WaypointId);
    }

    [Fact]
    public void Clear_persists()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");
        nav.Off();

        var restored = Open(store);
        Assert.Equal(NavTargetKind.None, restored.Current.Kind);
        Assert.Empty(store.Document.Worlds);
    }

    [Fact]
    public void Deleting_the_tracked_waypoint_clears_it()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");

        Assert.False(nav.NotifyWaypointDeleted(CampId));
        Assert.Equal(HomeId, nav.Current.WaypointId);

        Assert.True(nav.NotifyWaypointDeleted(HomeId));
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
        Assert.False(nav.NotifyWaypointDeleted(HomeId));

        var restored = Open(store);
        Assert.Equal(NavTargetKind.None, restored.Current.Kind);
    }

    [Fact]
    public void Deleting_a_waypoint_does_not_clear_a_player_or_coordinate()
    {
        var nav = Open(teammates: new NamedTeammates("Maili"));
        nav.Player("Maili");
        Assert.False(nav.NotifyWaypointDeleted(HomeId));
        Assert.Equal(NavTargetKind.Player, nav.Current.Kind);

        nav.SetCoordinate(new AbsoluteCoord(1, 2, 3), null);
        Assert.False(nav.NotifyWaypointDeleted(HomeId));
        Assert.Equal(NavTargetKind.Coordinate, nav.Current.Kind);
    }

    [Fact]
    public void Empty_list_before_any_waypoints_arrive_does_not_clear()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");

        Assert.False(nav.NoteWaypointList(new List<WaypointRef>()));
        Assert.Equal(HomeId, nav.Current.WaypointId);
    }

    [Fact]
    public void A_populated_list_without_the_guid_clears_the_target()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");

        var cleared = nav.NoteWaypointList(new List<WaypointRef> { new(CampId, "Camp") });

        Assert.True(cleared);
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
    }

    [Fact]
    public void A_later_empty_list_clears_after_the_waypoint_was_seen()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");
        Assert.False(nav.NoteWaypointList(new List<WaypointRef> { new(HomeId, "Home") }));

        Assert.True(nav.NoteWaypointList(new List<WaypointRef>()));
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
    }

    [Fact]
    public void Renaming_the_waypoint_keeps_the_guid()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");

        Assert.False(nav.NoteWaypointList(new List<WaypointRef> { new(HomeId, "House") }));

        Assert.Equal(HomeId, nav.Current.WaypointId);
        Assert.Equal("House", nav.Current.Name);
        var restored = Open(store);
        Assert.Equal(HomeId, restored.Current.WaypointId);
        Assert.Equal("House", restored.Current.Name);
    }

    [Fact]
    public void Status_reports_a_deleted_waypoint()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");

        var status = nav.Status(Spawn, new List<WaypointRef> { new(CampId, "Camp") });

        Assert.True(status.Ok);
        Assert.Equal("The tracked waypoint was deleted, so the navigation target was cleared.", status.Message);
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
    }

    [Fact]
    public void Commands_report_disabled_when_the_map_is_off_and_do_not_change_the_target()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");
        var saves = store.Saves;
        nav.SetMapAllowed(false);

        Assert.Equal(NavTargets.MapDisabledMessage, nav.SelectWaypoint("0", new List<WaypointRef> { new(CampId, "Camp") }).Message);
        Assert.Equal(NavTargets.MapDisabledMessage, nav.Player("Maili").Message);
        Assert.Equal(NavTargets.MapDisabledMessage, nav.Coord("1 2", Spawn, MapHeight).Message);
        Assert.Equal(NavTargets.MapDisabledMessage, nav.SetCoordinate(new AbsoluteCoord(1, 2, 3), Spawn).Message);
        Assert.Equal(NavTargets.MapDisabledMessage, nav.Off().Message);
        Assert.Equal(NavTargets.MapDisabledMessage, nav.Status(Spawn, null).Message);
        Assert.DoesNotContain("Home", nav.Status(Spawn, null).Message, StringComparison.Ordinal);

        Assert.Equal(HomeId, nav.Current.WaypointId);
        Assert.Equal(saves, store.Saves);

        nav.SetMapAllowed(true);
        Assert.Equal(HomeId, nav.Current.WaypointId);
        Assert.Contains("Home", nav.Status(Spawn, null).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_input_does_not_replace_the_current_target()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");

        var coord = nav.Coord("nope", Spawn, MapHeight);
        var player = nav.Player("  ");
        var waypoint = nav.SelectWaypoint("missing", new List<WaypointRef> { new(CampId, "Camp") });

        Assert.False(coord.Ok);
        Assert.False(player.Ok);
        Assert.False(waypoint.Ok);
        Assert.Equal(HomeId, nav.Current.WaypointId);
    }

    [Fact]
    public void Commands_before_a_world_is_attached_fail()
    {
        var nav = new NavTargets(new MemoryNavTargetStore());

        Assert.Equal(NavTargets.WorldNotReadyMessage, nav.Off().Message);
        Assert.Equal(NavTargets.WorldNotReadyMessage, nav.Player("Maili").Message);
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
    }

    [Fact]
    public void Corrupt_waypoint_id_loads_as_no_target()
    {
        var store = new MemoryNavTargetStore();
        store.Document.Worlds.Add(new NavTargetRecord
        {
            WorldId = "save-a",
            Kind = "waypoint",
            WaypointId = "not-a-guid",
        });
        store.Save(store.Document);

        var nav = Open(store);
        Assert.Equal(NavTargetKind.None, nav.Current.Kind);
    }

    [Fact]
    public void Same_world_attach_does_not_drop_an_unsaved_in_memory_view()
    {
        var nav = Open();
        nav.SetWaypoint(HomeId, "Home");
        nav.AttachWorld("save-a");

        Assert.Equal(HomeId, nav.Current.WaypointId);
    }

    [Fact]
    public void Default_visibility_rejects_every_player_and_leaves_the_target()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetWaypoint(HomeId, "Home");
        var saves = store.Saves;

        var reply = nav.Player("Maili");

        Assert.False(reply.Ok);
        Assert.Equal(NavTargets.TeammateSharingUnavailableMessage, reply.Message);
        Assert.Equal(HomeId, nav.Current.WaypointId);
        Assert.Equal(saves, store.Saves);
    }

    [Fact]
    public void A_non_teammate_is_rejected_and_the_target_stays()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store, teammates: new NamedTeammates("Oren"));
        nav.SetWaypoint(HomeId, "Home");
        var saves = store.Saves;

        var reply = nav.Player("Maili");

        Assert.False(reply.Ok);
        Assert.Equal(NavTargets.NotVisibleTeammateMessage("Maili"), reply.Message);
        Assert.Equal(HomeId, nav.Current.WaypointId);
        Assert.Equal(saves, store.Saves);
    }

    [Fact]
    public void A_visible_teammate_is_accepted()
    {
        var nav = Open(teammates: new NamedTeammates("Maili"));

        var reply = nav.Player("Maili");

        Assert.True(reply.Ok);
        Assert.Equal("Navigating to player \"Maili\".", reply.Message);
        Assert.Equal(NavTargetKind.Player, nav.Current.Kind);
        Assert.Equal("Target: player \"Maili\"", nav.Status(null, null).Message);
    }

    private static NavTargets Open(
        MemoryNavTargetStore? store = null,
        string worldId = "save-a",
        ITeammateVisibility? teammates = null)
    {
        var nav = new NavTargets(store ?? new MemoryNavTargetStore(), teammates);
        nav.AttachWorld(worldId);
        nav.SetMapAllowed(true);
        return nav;
    }
}

internal sealed class NamedTeammates : ITeammateVisibility
{
    private readonly HashSet<string> names;

    public NamedTeammates(params string[] names)
    {
        this.names = new HashSet<string>(names, StringComparer.Ordinal);
    }

    public bool IsVisibleTeammate(string? playerNameOrUid)
    {
        return playerNameOrUid != null && names.Contains(playerNameOrUid);
    }
}

internal sealed class MemoryNavTargetStore : INavTargetStore
{
    public NavTargetDocument Document { get; private set; } = new();

    public int Saves { get; private set; }

    public NavTargetDocument Load()
    {
        return Document.Clone();
    }

    public void Save(NavTargetDocument document)
    {
        Document = (document ?? new NavTargetDocument()).Clone();
        Saves++;
    }
}
