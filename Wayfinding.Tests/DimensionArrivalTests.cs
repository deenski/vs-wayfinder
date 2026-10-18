using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class DimensionArrivalTests
{
    private static readonly Guid HomeId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly SpawnOrigin Spawn = new(512000, 512000);
    private const int MapHeight = 256;

    [Fact]
    public void Waypoint_dimension_is_position_y_divided_by_the_boundary()
    {
        Assert.Equal(32768, WaypointDimension.Boundary);
        Assert.Equal(0, WaypointDimension.Decode(40));
        Assert.Equal(0, WaypointDimension.Decode(WaypointDimension.Boundary - 0.1));
        Assert.Equal(1, WaypointDimension.Decode(WaypointDimension.Boundary));
        Assert.Equal(2, WaypointDimension.Decode(WaypointDimension.InternalY(80, 2)));
    }

    [Fact]
    public void Coordinate_dimension_is_stamped_and_round_trips()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        var reply = nav.Coord("10 -4", Spawn, MapHeight, 2);

        Assert.True(reply.Ok);
        Assert.Equal(2, nav.Current.Dimension);
        Assert.Null(nav.Current.Coordinate!.Value.Y);
        Assert.True(store.Document.Worlds[0].HasDimension);
        Assert.Equal(2, store.Document.Worlds[0].Dimension);

        var restored = Open(store);
        Assert.Equal(2, restored.Current.Dimension);
        Assert.Equal(NavTargetKind.Coordinate, restored.Current.Kind);
    }

    [Fact]
    public void Stamped_dimension_zero_is_not_a_missing_dimension()
    {
        var store = new MemoryNavTargetStore();
        var nav = Open(store);
        nav.SetCoordinate(new AbsoluteCoord(1, 2, 3), null, 0);

        var restored = Open(store);
        Assert.Equal(0, restored.Current.Dimension);
        Assert.True(store.Document.Worlds[0].HasDimension);
        Assert.Equal(DimensionRelation.Same, new OwnTargetDimensionDetector().Evaluate(0, restored.Current, restored.Current.Dimension));
    }

    [Fact]
    public void Coordinate_saved_without_a_dimension_stays_unconfirmed()
    {
        var store = new MemoryNavTargetStore();
        store.Document.Worlds.Add(new NavTargetRecord
        {
            WorldId = "save-a",
            Kind = "coordinate",
            X = 10,
            Z = 20,
            HasY = true,
            Y = 70,
        });
        store.Save(store.Document);

        var nav = Open(store);
        Assert.Equal(NavTargetKind.Coordinate, nav.Current.Kind);
        Assert.Null(nav.Current.Dimension);
        Assert.False(store.Document.Worlds[0].HasDimension);
        Assert.Equal(
            DimensionRelation.Unconfirmed,
            new OwnTargetDimensionDetector().Evaluate(0, nav.Current, null));
    }

    [Fact]
    public void Dimension_relation_is_same_different_or_unconfirmed()
    {
        var detector = new OwnTargetDimensionDetector();
        var same = NavTarget.At(new AbsoluteCoord(1, 2, 3), 2);
        var waypoint = NavTarget.Waypoint(HomeId, "Home");

        Assert.Equal(DimensionRelation.Same, detector.Evaluate(2, same, same.Dimension));
        Assert.Equal(DimensionRelation.Different, detector.Evaluate(0, same, same.Dimension));
        Assert.Equal(DimensionRelation.Unconfirmed, detector.Evaluate(null, same, 2));
        Assert.Equal(DimensionRelation.Same, detector.Evaluate(2, waypoint, 2));
        Assert.Equal(DimensionRelation.Different, detector.Evaluate(0, waypoint, 2));
        Assert.Equal(DimensionRelation.Unconfirmed, detector.Evaluate(0, waypoint, null));
        Assert.Equal(DimensionRelation.Unconfirmed, detector.Evaluate(0, NavTarget.None, 0));
    }

    [Fact]
    public void Player_targets_stay_unconfirmed_until_a_teammate_position_source_exists()
    {
        var nav = Open(teammates: new NamedTeammates("Maili"));
        var reply = nav.Player("Maili");

        Assert.True(reply.Ok);
        Assert.Equal(NavTargetKind.Player, nav.Current.Kind);
        Assert.Equal(DimensionRelation.Unconfirmed, new OwnTargetDimensionDetector().Evaluate(0, nav.Current, 0));
        Assert.Equal(DimensionRelation.Unconfirmed, DimensionRelationFor(nav, PlayerFix.At(0, 0, 0, 0)));
    }

    [Fact]
    public void First_sample_is_never_a_jump()
    {
        var detector = new TeleportDetector();

        Assert.False(detector.Observe(PlayerFix.At(10000, 80, 0, 0)));
    }

    [Fact]
    public void Horizontal_move_of_exactly_64_blocks_is_not_a_jump()
    {
        var detector = new TeleportDetector();
        Assert.False(detector.Observe(PlayerFix.At(0, 0, 0, 0)));

        Assert.False(detector.Observe(PlayerFix.At(TeleportLimits.Blocks, 0, 0, 0)));
    }

    [Fact]
    public void Horizontal_move_of_65_blocks_is_a_jump()
    {
        var detector = new TeleportDetector();
        Assert.False(detector.Observe(PlayerFix.At(0, 0, 0, 0)));

        Assert.True(detector.Observe(PlayerFix.At(65, 0, 0, 0)));
    }

    [Fact]
    public void Vertical_only_move_is_not_a_jump()
    {
        var detector = new TeleportDetector();
        Assert.False(detector.Observe(PlayerFix.At(0, 0, 0, 0)));

        Assert.False(detector.Observe(PlayerFix.At(0, 400, 0, 0)));
    }

    [Fact]
    public void Exactly_4_horizontal_and_3_vertical_arrives()
    {
        var detector = new ArrivalDetector();
        var target = CoordTarget(0, 10, 0, 0);
        var place = Place(target);

        Assert.False(detector.Observe(PlayerFix.At(100, 10, 0, 0), target, place));
        Assert.True(detector.Observe(PlayerFix.At(4, 13, 0, 0), target, place));
    }

    [Fact]
    public void Five_horizontal_blocks_does_not_arrive()
    {
        var detector = new ArrivalDetector();
        var target = CoordTarget(0, 10, 0, 0);
        var place = Place(target);

        Assert.False(detector.Observe(PlayerFix.At(100, 10, 0, 0), target, place));
        Assert.False(detector.Observe(PlayerFix.At(5, 10, 0, 0), target, place));
    }

    [Fact]
    public void Four_vertical_blocks_does_not_arrive()
    {
        var detector = new ArrivalDetector();
        var target = CoordTarget(0, 10, 0, 0);
        var place = Place(target);

        Assert.False(detector.Observe(PlayerFix.At(100, 10, 0, 0), target, place));
        Assert.False(detector.Observe(PlayerFix.At(0, 14, 0, 0), target, place));
    }

    [Fact]
    public void Arrival_is_not_armed_until_one_out_of_range_sample()
    {
        var detector = new ArrivalDetector();
        var target = CoordTarget(0, 10, 0, 0);
        var place = Place(target);

        Assert.False(detector.Observe(PlayerFix.At(0, 10, 0, 0), target, place));
        Assert.False(detector.Observe(PlayerFix.At(1, 10, 0, 0), target, place));
        Assert.False(detector.Observe(PlayerFix.At(100, 10, 0, 0), target, place));
        Assert.True(detector.Observe(PlayerFix.At(0, 10, 0, 0), target, place));
    }

    [Fact]
    public void Player_targets_never_arrive()
    {
        var detector = new ArrivalDetector();
        var target = NavTarget.Player("Maili");

        Assert.False(detector.Observe(PlayerFix.At(100, 0, 0, 0), target, TargetPlace.Unknown));
        Assert.False(detector.Observe(PlayerFix.At(0, 0, 0, 0), target, TargetPlace.Unknown));
        Assert.False(detector.Observe(PlayerFix.At(0, 0, 0, 0), target, TargetPlace.Waypoint(0, 0, 0)));
    }

    [Fact]
    public void Different_dimension_never_arrives()
    {
        var detector = new ArrivalDetector();
        var target = CoordTarget(0, 10, 0, 0);
        var place = Place(target);

        Assert.False(detector.Observe(PlayerFix.At(100, 10, 0, 2), target, place));
        Assert.False(detector.Observe(PlayerFix.At(0, 10, 0, 2), target, place));
    }

    [Fact]
    public void Coordinate_without_y_checks_horizontal_distance_only()
    {
        var detector = new ArrivalDetector();
        var target = NavTarget.At(new AbsoluteCoord(0, null, 0), 0);
        var place = TargetPlace.Coordinate(target.Coordinate!.Value, 0);

        Assert.False(place.HasVertical);
        Assert.False(detector.Observe(PlayerFix.At(100, 0, 0, 0), target, place));
        Assert.True(detector.Observe(PlayerFix.At(0, 500, 0, 0), target, place));
    }

    [Fact]
    public void Arrival_clears_the_target_and_the_persisted_guid_and_says_arrived()
    {
        var harness = new Harness();
        harness.Targets.SetWaypoint(HomeId, "Home");
        harness.Waypoints.Points[HomeId] = new WaypointPosition(0, 10, 0);
        harness.Positions.Current = PlayerFix.At(100, 10, 0, 0);
        harness.Session.Tick();

        Assert.Equal(HomeId, harness.Targets.Current.WaypointId);

        harness.Positions.Current = PlayerFix.At(0, 10, 0, 0);
        harness.Session.Tick();

        Assert.Equal(NavTargetKind.None, harness.Targets.Current.Kind);
        Assert.DoesNotContain(harness.Store.Document.Worlds, record => record.WaypointId == HomeId.ToString("D"));
        Assert.Empty(harness.Store.Document.Worlds);
        Assert.Equal(1, harness.Notice.Count);
        Assert.Contains(harness.Sink.Lines, line => line.Line.Contains("Arrived", StringComparison.Ordinal));
        Assert.Equal(DimensionRelation.Unconfirmed, harness.Session.Current);
    }

    [Fact]
    public void Session_does_not_arrive_a_player_target()
    {
        var harness = new Harness(new NamedTeammates("Maili"));
        harness.Targets.Player("Maili");
        harness.Positions.Current = PlayerFix.At(0, 0, 0, 0);

        harness.Session.Tick();
        harness.Session.Tick();

        Assert.Equal(NavTargetKind.Player, harness.Targets.Current.Kind);
        Assert.Equal(0, harness.Notice.Count);
        Assert.Equal(DimensionRelation.Unconfirmed, harness.Session.Current);
    }

    [Fact]
    public void Dimension_change_death_and_own_spawn_reset_the_baseline_and_request_a_replan()
    {
        AssertReset(static harness => harness.Events.DimensionChanged(), ReplanReason.DimensionChanged);
        AssertReset(static harness => harness.Events.Died(), ReplanReason.Death);
        AssertReset(static harness => harness.Events.Spawned("local-player"), ReplanReason.Spawn);
    }

    [Fact]
    public void Another_players_spawn_does_not_reset_the_baseline()
    {
        var harness = new Harness();
        harness.Positions.Current = PlayerFix.At(0, 0, 0, 0);
        harness.Session.Start();
        harness.Events.Spawned("someone-else");
        harness.Positions.Current = PlayerFix.At(65, 0, 0, 0);
        harness.Session.Tick();

        Assert.Contains(harness.Replans, request => request.Reason == ReplanReason.Teleport);
        Assert.DoesNotContain(harness.Replans, request => request.Reason == ReplanReason.Spawn);
    }

    [Fact]
    public void Teleport_replan_uses_the_injected_clock()
    {
        var harness = new Harness();
        harness.Clock.ElapsedMilliseconds = 50;
        harness.Positions.Current = PlayerFix.At(0, 0, 0, 0);
        harness.Session.Tick();
        harness.Clock.ElapsedMilliseconds = 80;
        harness.Positions.Current = PlayerFix.At(65, 0, 0, 0);
        harness.Session.Tick();

        var replan = Assert.Single(harness.Replans);
        Assert.Equal(ReplanReason.Teleport, replan.Reason);
        Assert.Equal(80, replan.ElapsedMilliseconds);
    }

    [Fact]
    public void Closed_navigation_does_not_arrive_and_reports_unconfirmed()
    {
        var harness = new Harness();
        harness.Targets.SetWaypoint(HomeId, "Home");
        harness.Waypoints.Points[HomeId] = new WaypointPosition(0, 10, 0);
        harness.Positions.Current = PlayerFix.At(100, 10, 0, 0);
        harness.Session.Tick();
        harness.Targets.SetMapAllowed(false);
        harness.Positions.Current = PlayerFix.At(0, 10, 0, 0);
        harness.Session.Tick();

        Assert.Equal(HomeId, harness.Targets.Current.WaypointId);
        Assert.Equal(0, harness.Notice.Count);
        Assert.Equal(DimensionRelation.Unconfirmed, harness.Session.Current);
    }

    [Fact]
    public void Dispose_is_idempotent_and_unsubscribes_once()
    {
        var harness = new Harness();
        harness.Session.Start();
        Assert.Equal(1, harness.Events.SubscribeCount);
        Assert.True(harness.Events.Subscribed);

        harness.Session.Dispose();
        harness.Session.Dispose();

        Assert.False(harness.Events.Subscribed);
        Assert.Equal(1, harness.Events.UnsubscribeCount);
    }

    [Fact]
    public void Dispose_before_start_does_not_unsubscribe()
    {
        var harness = new Harness();

        harness.Session.Dispose();
        harness.Session.Dispose();

        Assert.Equal(0, harness.Events.UnsubscribeCount);
    }

    [Fact]
    public void Unconfirmed_draws_nothing_different_draws_the_badge_and_same_draws_the_line()
    {
        MapDraw hidden = DimensionPresentation.For(DimensionRelation.Unconfirmed);
        Assert.False(hidden.Line);
        Assert.Null(hidden.Badge);

        MapDraw other = DimensionPresentation.For(DimensionRelation.Different);
        Assert.False(other.Line);
        Assert.Equal("different dimension", other.Badge);

        MapDraw same = DimensionPresentation.For(DimensionRelation.Same);
        Assert.True(same.Line);
        Assert.Null(same.Badge);
    }

    [Fact]
    public void Debug_sample_stamps_the_player_dimension()
    {
        var commands = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "NavCommands.cs"));
        Assert.Contains("SetCoordinate(coordinate, Spawn(mod), pos.Dimension)", commands, StringComparison.Ordinal);

        var bare = NavTarget.At(new AbsoluteCoord(1, 2, 3), null);
        var stamped = NavTarget.At(new AbsoluteCoord(1, 2, 3), 0);
        var detector = new OwnTargetDimensionDetector();
        Assert.Equal(DimensionRelation.Unconfirmed, detector.Evaluate(0, bare, bare.Dimension));
        Assert.False(DimensionPresentation.For(DimensionRelation.Unconfirmed).Line);
        Assert.Equal(DimensionRelation.Same, detector.Evaluate(0, stamped, stamped.Dimension));
        Assert.True(DimensionPresentation.For(DimensionRelation.Same).Line);
    }

    [Fact]
    public void Dispose_clears_the_client_api_after_the_arrival_hook()
    {
        var mod = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "WayfindingModSystem.cs"));
        var arrival = mod.IndexOf("DisposeDimensionAndArrival()", StringComparison.Ordinal);
        var tether = mod.IndexOf("DisposeTeammateTether()", StringComparison.Ordinal);
        var clear = mod.LastIndexOf("capi = null", StringComparison.Ordinal);

        Assert.True(arrival >= 0);
        Assert.True(tether > arrival);
        Assert.True(clear > tether);
    }

    [Fact]
    public void Map_layer_reads_the_relation_and_subscribes_to_the_replan()
    {
        var layer = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Map", "WayfindingMapLayer.cs"));

        Assert.Contains("DimensionPresentation.For(mod.RouteDimension)", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("labelText = badge", layer, StringComparison.Ordinal);
        Assert.Contains("mod.ReplanRequested += OnForcedReplan", layer, StringComparison.Ordinal);
        Assert.Contains("mod.ReplanRequested -= OnForcedReplan", layer, StringComparison.Ordinal);
        Assert.Contains("straight = null", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("mod.Target", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(", File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "DimensionArrivalCommands.cs")), StringComparison.Ordinal);
    }

    private static void AssertReset(Action<Harness> raise, ReplanReason reason)
    {
        var harness = new Harness();
        harness.Positions.Current = PlayerFix.At(0, 0, 0, 0);
        harness.Session.Start();
        harness.Positions.Current = PlayerFix.At(1000, 0, 0, 0);
        raise(harness);
        harness.Session.Tick();

        Assert.Contains(harness.Replans, request => request.Reason == reason);
        Assert.DoesNotContain(harness.Replans, request => request.Reason == ReplanReason.Teleport);
    }

    private static DimensionRelation DimensionRelationFor(NavTargets nav, PlayerFix player)
    {
        return new OwnTargetDimensionDetector().Evaluate(
            player.IsAvailable ? player.Dimension : null,
            nav.Current,
            null);
    }

    private static NavTarget CoordTarget(int x, int? y, int z, int dimension)
    {
        return NavTarget.At(new AbsoluteCoord(x, y, z), dimension);
    }

    private static TargetPlace Place(NavTarget target)
    {
        return TargetPlace.Coordinate(target.Coordinate!.Value, target.Dimension);
    }

    private static NavTargets Open(MemoryNavTargetStore? store = null, ITeammateVisibility? teammates = null)
    {
        var nav = new NavTargets(store ?? new MemoryNavTargetStore(), teammates);
        nav.AttachWorld("save-a");
        nav.SetMapAllowed(true);
        return nav;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wayfinding.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find Wayfinding.sln above the test output.");
    }

    private sealed class Harness
    {
        public Harness(ITeammateVisibility? teammates = null)
        {
            Store = new MemoryNavTargetStore();
            Targets = new NavTargets(Store, teammates);
            Targets.AttachWorld("save-a");
            Targets.SetMapAllowed(true);
            Log = new WayfindingLog(Sink);
            Session = new DimensionArrivalSession(
                Targets,
                Positions,
                () => Clock.ElapsedMilliseconds,
                Waypoints,
                Events,
                Notice,
                Log,
                () => "local-player");
            Session.ReplanRequested += request => Replans.Add(request);
        }

        public MemoryNavTargetStore Store { get; }

        public NavTargets Targets { get; }

        public ScriptedPositions Positions { get; } = new();

        public ManualClock Clock { get; } = new();

        public ScriptedWaypoints Waypoints { get; } = new();

        public FakeDimensionEvents Events { get; } = new();

        public ArrivalCounter Notice { get; } = new();

        public ListSink Sink { get; } = new();

        public WayfindingLog Log { get; }

        public DimensionArrivalSession Session { get; }

        public List<ReplanRequest> Replans { get; } = new();
    }

    private sealed class ScriptedPositions : IPlayerPositionSource
    {
        public PlayerFix Current { get; set; } = PlayerFix.Unavailable;

        public PlayerFix Read()
        {
            return Current;
        }
    }

    private sealed class ManualClock
    {
        public long ElapsedMilliseconds { get; set; }
    }

    private sealed class ScriptedWaypoints : IWaypointPositionSource
    {
        public Dictionary<Guid, WaypointPosition> Points { get; } = new();

        public bool TryGet(Guid waypointId, out WaypointPosition position)
        {
            return Points.TryGetValue(waypointId, out position);
        }
    }

    private sealed class ArrivalCounter : IArrivalNotice
    {
        public int Count { get; private set; }

        public void Arrived()
        {
            Count++;
        }
    }

    private sealed class FakeDimensionEvents : IDimensionArrivalEvents
    {
        private DimensionArrivalHandlers? handlers;

        public int SubscribeCount { get; private set; }

        public int UnsubscribeCount { get; private set; }

        public bool Subscribed { get; private set; }

        public void Subscribe(DimensionArrivalHandlers handlers)
        {
            if (Subscribed)
            {
                throw new InvalidOperationException("Already subscribed.");
            }

            this.handlers = handlers;
            Subscribed = true;
            SubscribeCount++;
        }

        public void Unsubscribe()
        {
            if (!Subscribed)
            {
                return;
            }

            handlers = null;
            Subscribed = false;
            UnsubscribeCount++;
        }

        public void DimensionChanged()
        {
            handlers?.OnDimensionChanged();
        }

        public void Died()
        {
            handlers?.OnDeath();
        }

        public void Spawned(string uid)
        {
            handlers?.OnEntitySpawn(uid);
        }
    }
}
