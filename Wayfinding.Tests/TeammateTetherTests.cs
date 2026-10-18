using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class TeammateTetherTests
{
    [Fact]
    public void Entity_position_wins_over_tracking()
    {
        var harness = new TetherHarness();
        harness.Positions.Entity = new TeammatePoint(40, 5, 70);
        harness.Positions.Tracked = new TeammatePoint(8, 9);

        var sample = harness.Tick();

        Assert.True(sample.Visible);
        Assert.Equal(TeammateFixKind.Live, sample.Kind);
        Assert.Equal(40, sample.Position.X);
        Assert.Equal(5, sample.Position.Z);
        Assert.Equal(70, sample.Position.Y);
        Assert.Equal(TimeSpan.Zero, sample.Age);
    }

    [Fact]
    public void Tracked_position_is_used_when_the_entity_is_not_loaded()
    {
        var harness = new TetherHarness();
        harness.Positions.Entity = null;
        harness.Positions.Tracked = new TeammatePoint(30, -4);

        var sample = harness.Tick();

        Assert.True(sample.Visible);
        Assert.Equal(TeammateFixKind.Live, sample.Kind);
        Assert.Equal(30, sample.Position.X);
        Assert.Equal(-4, sample.Position.Z);
        Assert.Null(sample.Position.Y);
    }

    [Fact]
    public void Missing_live_data_keeps_a_greyed_last_known_marker_with_its_age()
    {
        var harness = new TetherHarness();
        harness.Positions.Entity = new TeammatePoint(25, 2, 11);
        harness.Tick();

        harness.Positions.Entity = null;
        harness.Positions.Tracked = null;
        harness.Clock.Milliseconds = 10_000;
        var sample = harness.Tick();

        Assert.True(sample.Visible);
        Assert.Equal(TeammateFixKind.LastKnown, sample.Kind);
        Assert.Equal(25, sample.Position.X);
        Assert.Equal(2, sample.Position.Z);
        Assert.Equal(11, sample.Position.Y);
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), sample.Age);
    }

    [Fact]
    public void No_position_yet_shows_nothing()
    {
        var harness = new TetherHarness();
        harness.Positions.Entity = null;
        harness.Positions.Tracked = null;

        var sample = harness.Tick();

        Assert.False(sample.Visible);
        Assert.Equal(TeammateFixKind.None, sample.Kind);
        Assert.False(sample.HasPosition);
    }

    [Theory]
    [InlineData(12.1, true)]
    [InlineData(12, true)]
    [InlineData(11.9, true)]
    [InlineData(6.1, true)]
    [InlineData(6, false)]
    [InlineData(5.9, false)]
    public void Shown_tether_hides_at_six_blocks(double distance, bool visible)
    {
        var sample = AtDistance(distance, startApart: true);

        Assert.Equal(visible, sample.Visible);
        Assert.Equal(TeammateFixKind.Live, sample.Kind);
        Assert.True(sample.HasPosition);
    }

    [Theory]
    [InlineData(5.9, false)]
    [InlineData(6, false)]
    [InlineData(6.1, false)]
    [InlineData(11.9, false)]
    [InlineData(12, false)]
    [InlineData(12.1, true)]
    public void Hidden_tether_shows_only_beyond_twelve_blocks(double distance, bool visible)
    {
        var sample = AtDistance(distance, startApart: false);

        Assert.Equal(visible, sample.Visible);
        Assert.Equal(TeammateFixKind.Live, sample.Kind);
        Assert.True(sample.HasPosition);
    }

    [Fact]
    public void Hysteresis_holds_across_the_band_in_both_directions()
    {
        var harness = new TetherHarness();
        harness.Place(20);
        Assert.True(harness.Tick().Visible);

        harness.Place(6.1);
        Assert.True(harness.Tick().Visible);
        harness.Place(6);
        Assert.False(harness.Tick().Visible);
        harness.Place(11.9);
        Assert.False(harness.Tick().Visible);
        harness.Place(12);
        Assert.False(harness.Tick().Visible);
        harness.Place(12.1);
        Assert.True(harness.Tick().Visible);
        harness.Place(5.9);
        Assert.False(harness.Tick().Visible);
    }

    [Fact]
    public void Stale_marker_starts_at_thirty_seconds_and_a_new_fix_clears_it()
    {
        Assert.Equal(30_000, TeammateTether.StaleAfterMilliseconds);
        var harness = new TetherHarness();
        harness.Place(20);
        harness.Tick();

        harness.Positions.Entity = null;
        harness.Clock.Milliseconds = 29_999;
        var fresh = harness.Tick();
        Assert.True(fresh.Visible);
        Assert.Equal(TeammateFixKind.LastKnown, fresh.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(29_999), fresh.Age);

        harness.Clock.Milliseconds = 30_000;
        var stale = harness.Tick();
        Assert.True(stale.Visible);
        Assert.Equal(TeammateFixKind.Stale, stale.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(30_000), stale.Age);
        Assert.Equal(20, stale.Position.X);

        harness.Clock.Milliseconds = 30_001;
        Assert.Equal(TeammateFixKind.Stale, harness.Tick().Kind);

        harness.Place(20);
        harness.Clock.Milliseconds = 45_000;
        var again = harness.Tick();
        Assert.Equal(TeammateFixKind.Live, again.Kind);
        Assert.Equal(TimeSpan.Zero, again.Age);
        Assert.True(again.Visible);
    }

    [Fact]
    public void Stale_marker_inside_six_blocks_is_not_shown()
    {
        var harness = new TetherHarness();
        harness.Place(20);
        harness.Tick();
        harness.Positions.Entity = null;
        harness.Clock.Milliseconds = 30_000;
        Assert.Equal(TeammateFixKind.Stale, harness.Tick().Kind);

        harness.Inputs.Local = new TeammatePoint(20, 0);
        var hidden = harness.Tick();
        Assert.False(hidden.Visible);
        Assert.Equal(TeammateFixKind.Stale, hidden.Kind);
    }

    [Fact]
    public void Thirty_seconds_without_ever_receiving_a_fix_shows_nothing()
    {
        var harness = new TetherHarness();
        harness.Positions.Entity = null;
        harness.Clock.Milliseconds = 30_000;

        var sample = harness.Tick();

        Assert.False(sample.Visible);
        Assert.Equal(TeammateFixKind.None, sample.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Map_hide_other_players_shows_nothing(bool? hide)
    {
        var harness = new TetherHarness();
        harness.Inputs.MapHideOtherPlayers = hide;

        AssertHidden(harness.Tick());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Spectator_shows_nothing(bool? spectator)
    {
        var harness = new TetherHarness();
        harness.Inputs.Spectator = spectator;

        AssertHidden(harness.Tick());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Unconfirmed_dimension_shows_nothing(bool? confirmed)
    {
        var harness = new TetherHarness();
        harness.Inputs.DimensionConfirmed = confirmed;

        AssertHidden(harness.Tick());
    }

    [Fact]
    public void Entity_leaving_sim_range_keeps_the_marker_and_then_goes_stale()
    {
        var adapter = new ClientAdapter();
        Assert.Equal(true, adapter.Inputs.IsDimensionConfirmed("Maili"));
        var live = adapter.Tick();
        Assert.True(live.Visible);
        Assert.Equal(TeammateFixKind.Live, live.Kind);
        Assert.Equal(20, live.Position.X);

        adapter.TargetDimension = null;
        adapter.Positions.Entity = null;
        adapter.Positions.Tracked = new TeammatePoint(80, 0);
        adapter.Clock.Milliseconds = 1_000;
        Assert.Null(adapter.Inputs.IsDimensionConfirmed("Maili"));
        var lastKnown = adapter.Tick();
        Assert.True(lastKnown.Visible);
        Assert.Equal(TeammateFixKind.LastKnown, lastKnown.Kind);
        Assert.Equal(20, lastKnown.Position.X);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), lastKnown.Age);

        adapter.Clock.Milliseconds = TeammateTether.StaleAfterMilliseconds;
        var stale = adapter.Tick();
        Assert.True(stale.Visible);
        Assert.Equal(TeammateFixKind.Stale, stale.Kind);
        Assert.Equal(20, stale.Position.X);
        Assert.Equal(TimeSpan.FromMilliseconds(TeammateTether.StaleAfterMilliseconds), stale.Age);
    }

    [Fact]
    public void A_positive_dimension_mismatch_forgets_the_fix()
    {
        var adapter = new ClientAdapter();
        Assert.Equal(TeammateFixKind.Live, adapter.Tick().Kind);

        adapter.TargetDimension = adapter.LocalDimension + 1;
        Assert.Equal(false, adapter.Inputs.IsDimensionConfirmed("Maili"));
        AssertHidden(adapter.Tick());

        adapter.TargetDimension = adapter.LocalDimension;
        adapter.Positions.Entity = null;
        var forgotten = adapter.Tick();
        Assert.Equal(TeammateFixKind.None, forgotten.Kind);
        Assert.False(forgotten.HasPosition);
    }

    [Fact]
    public void A_player_who_is_not_a_visible_teammate_shows_nothing()
    {
        var harness = new TetherHarness();
        harness.Teammates.Only("SomeoneElse");

        AssertHidden(harness.Tick("Maili"));
    }

    [Fact]
    public void Missing_visibility_service_denies_everyone()
    {
        var harness = new TetherHarness(passVisibility: false);

        AssertHidden(harness.Tick());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Share_off_or_unknown_shows_nothing(bool? sharing)
    {
        var harness = new TetherHarness();
        harness.Inputs.Sharing = sharing;

        AssertHidden(harness.Tick());
    }

    [Fact]
    public void Share_off_hides_a_live_teammate_within_five_seconds()
    {
        Assert.Equal(5_000, TeammateTether.ShareOffWithinMilliseconds);
        Assert.InRange(
            TeammateTether.ClientTickIntervalMilliseconds,
            1,
            (int)TeammateTether.ShareOffWithinMilliseconds);

        var immediate = new TetherHarness();
        Assert.True(immediate.Tick().Visible);
        immediate.Inputs.Sharing = false;
        immediate.Clock.Milliseconds += 1;
        var atOneMillisecond = immediate.Tick();
        AssertHidden(atOneMillisecond);

        var deadline = new TetherHarness();
        Assert.True(deadline.Tick().Visible);
        deadline.Inputs.Sharing = false;
        deadline.Positions.Entity = new TeammatePoint(20, 0, 64);
        deadline.Clock.Milliseconds += TeammateTether.ShareOffWithinMilliseconds;
        AssertHidden(deadline.Tick());

        var stillSharing = new TetherHarness();
        stillSharing.Tick();
        stillSharing.Positions.Entity = null;
        stillSharing.Clock.Milliseconds = TeammateTether.ShareOffWithinMilliseconds;
        var lastKnown = stillSharing.Tick();
        Assert.True(lastKnown.Visible);
        Assert.Equal(TeammateFixKind.LastKnown, lastKnown.Kind);
    }

    [Fact]
    public void Leaving_the_visible_set_hides_within_five_seconds()
    {
        // TeamService reports both "not a teammate" and share-off as not visible.
        // The tick that observes it hides. Five seconds is the deadline.
        var immediate = new TetherHarness();
        Assert.True(immediate.Tick().Visible);
        immediate.Teammates.Only("SomeoneElse");
        immediate.Clock.Milliseconds += 1;
        AssertHidden(immediate.Tick());

        var deadline = new TetherHarness();
        Assert.True(deadline.Tick().Visible);
        deadline.Teammates.Only("SomeoneElse");
        deadline.Positions.Entity = new TeammatePoint(20, 0, 64);
        deadline.Clock.Milliseconds += TeammateTether.ShareOffWithinMilliseconds;
        AssertHidden(deadline.Tick());
    }

    [Fact]
    public void A_different_player_does_not_keep_the_previous_fix()
    {
        var harness = new TetherHarness();
        harness.Teammates.Allow("Maili");
        harness.Teammates.Allow("Sable");
        harness.Place(20);
        Assert.Equal(20, harness.Tick("Maili").Position.X);

        harness.Place(40);
        var sample = harness.Tick("Sable");

        Assert.True(sample.Visible);
        Assert.Equal(40, sample.Position.X);
        Assert.Equal(TeammateFixKind.Live, sample.Kind);
    }

    [Fact]
    public void Clearing_the_target_shows_nothing()
    {
        var harness = new TetherHarness();
        Assert.True(harness.Tick("Maili").Visible);

        AssertHidden(harness.Tick(null));
        AssertHidden(harness.Tick("   "));
    }

    [Fact]
    public void Last_known_and_stale_markers_show_an_age_and_a_live_fix_does_not()
    {
        Assert.Null(TeammateAgeText.For(TeammateFixKind.None, TimeSpan.FromSeconds(12)));
        Assert.Null(TeammateAgeText.For(TeammateFixKind.Live, TimeSpan.FromSeconds(12)));
        Assert.Equal("12s", TeammateAgeText.For(TeammateFixKind.LastKnown, TimeSpan.FromSeconds(12)));
        Assert.Equal("0s", TeammateAgeText.For(TeammateFixKind.LastKnown, TimeSpan.Zero));
        Assert.Equal("30s", TeammateAgeText.For(TeammateFixKind.Stale, TimeSpan.FromMilliseconds(30_000)));
        Assert.Equal("59s", TeammateAgeText.For(TeammateFixKind.Stale, TimeSpan.FromSeconds(59)));
        Assert.Equal("1m", TeammateAgeText.For(TeammateFixKind.Stale, TimeSpan.FromSeconds(60)));
        Assert.Equal("3m", TeammateAgeText.For(TeammateFixKind.LastKnown, TimeSpan.FromMinutes(3)));

        var layer = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Map", "WayfindingMapLayer.cs"));
        Assert.Contains("TeammateAgeText.For", layer, StringComparison.Ordinal);
        Assert.Contains("DistanceText.Format", layer, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_hook_ticks_from_the_game_listener_and_adds_no_nav_command()
    {
        var hook = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "WayfindingModSystem.TeammateTether.cs"));
        var commands = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "TeammateTetherCommands.cs"));
        var adapter = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Teammate", "ClientTeammateTether.cs"));

        Assert.Contains("RegisterGameTickListener", hook, StringComparison.Ordinal);
        Assert.Contains("UnregisterGameTickListener", hook, StringComparison.Ordinal);
        Assert.Contains("ClientTickIntervalMilliseconds", hook, StringComparison.Ordinal);
        Assert.Contains("if (teammateTetherListening)", hook, StringComparison.Ordinal);
        Assert.Contains("TeammateSample", hook, StringComparison.Ordinal);
        Assert.Contains("NoTeammateVisibility.Instance", hook, StringComparison.Ordinal);
        Assert.Contains("teammates.IsVisibleTeammate", adapter, StringComparison.Ordinal);
        Assert.Contains("GetPlayerPositionInformation", adapter, StringComparison.Ordinal);
        var layer = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Map", "WayfindingMapLayer.cs"));
        Assert.Contains("NavTargetKind.Player", layer, StringComparison.Ordinal);
        Assert.Contains("mod.TeammateSample", layer, StringComparison.Ordinal);
        Assert.Contains("mapHideOtherPlayers", adapter, StringComparison.Ordinal);
        Assert.Contains("EnumGameMode.Spectator", adapter, StringComparison.Ordinal);
        Assert.Contains("return null;", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate(\"nav\")", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Create(\"nav\")", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Create(\"nav\")", commands, StringComparison.Ordinal);
    }

    private static TeammateTetherSample AtDistance(double distance, bool startApart)
    {
        var harness = new TetherHarness();
        harness.Place(startApart ? 100 : 0);
        harness.Tick();
        harness.Place(distance);
        return harness.Tick();
    }

    private static void AssertHidden(TeammateTetherSample sample)
    {
        Assert.False(sample.Visible);
        Assert.Equal(TeammateFixKind.None, sample.Kind);
        Assert.False(sample.HasPosition);
        Assert.Equal(TimeSpan.Zero, sample.Age);
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

    private sealed class ClientAdapter
    {
        public ClientAdapter()
        {
            var teammates = new NamedTeammates();
            teammates.Allow("Maili");
            Inputs = new ClientTeammateTetherInputs(
                () => false,
                _ => false,
                () => LocalDimension,
                _ => TargetDimension,
                _ => true,
                () => new TeammatePoint(0, 0));
            Positions.Entity = new TeammatePoint(20, 0, 64);
            Tether = new TeammateTether(Positions, teammates, Inputs, Clock);
        }

        public int? LocalDimension { get; set; } = 0;

        public int? TargetDimension { get; set; } = 0;

        public ClientTeammateTetherInputs Inputs { get; }

        public FakePositions Positions { get; } = new();

        public ManualClock Clock { get; } = new();

        public TeammateTether Tether { get; }

        public TeammateTetherSample Tick()
        {
            Tether.Tick("Maili");
            return Tether.Current;
        }
    }

    private sealed class TetherHarness
    {
        public TetherHarness(ITeammateVisibility? visibility = null, bool passVisibility = true)
        {
            Teammates = new NamedTeammates();
            Teammates.Allow("Maili");
            Inputs.Sharing = true;
            Inputs.MapHideOtherPlayers = false;
            Inputs.Spectator = false;
            Inputs.DimensionConfirmed = true;
            Inputs.Local = new TeammatePoint(0, 0);
            Place(20);
            Tether = new TeammateTether(Positions, passVisibility ? visibility ?? Teammates : null, Inputs, Clock);
        }

        public ManualClock Clock { get; } = new();

        public FakePositions Positions { get; } = new();

        public FakeInputs Inputs { get; } = new();

        public NamedTeammates Teammates { get; }

        public TeammateTether Tether { get; }

        public void Place(double distance)
        {
            Positions.Entity = new TeammatePoint(distance, 0, 64);
            Positions.Tracked = null;
        }

        public TeammateTetherSample Tick(string? player = "Maili")
        {
            Tether.Tick(player);
            return Tether.Current;
        }
    }

    private sealed class ManualClock : ITeammateTetherClock
    {
        public long Milliseconds { get; set; }
    }

    private sealed class FakePositions : ITeammatePositionSource
    {
        public TeammatePoint? Entity { get; set; }

        public TeammatePoint? Tracked { get; set; }

        public bool TryGetEntityPosition(string? playerNameOrUid, out TeammatePoint position)
        {
            return Take(Entity, out position);
        }

        public bool TryGetTrackedPosition(string? playerNameOrUid, out TeammatePoint position)
        {
            return Take(Tracked, out position);
        }

        private static bool Take(TeammatePoint? point, out TeammatePoint position)
        {
            if (point is TeammatePoint value)
            {
                position = value;
                return true;
            }

            position = default;
            return false;
        }
    }

    private sealed class FakeInputs : ITeammateTetherInputs
    {
        public bool? MapHideOtherPlayers { get; set; }

        public bool? Spectator { get; set; }

        public bool? DimensionConfirmed { get; set; }

        public bool? Sharing { get; set; }

        public TeammatePoint? Local { get; set; }

        public bool? IsSpectator(string? playerNameOrUid)
        {
            return Spectator;
        }

        public bool? IsDimensionConfirmed(string? playerNameOrUid)
        {
            return DimensionConfirmed;
        }

        public bool? IsSharing(string? playerNameOrUid)
        {
            return Sharing;
        }

        public bool TryGetLocalPosition(out TeammatePoint position)
        {
            if (Local is TeammatePoint point)
            {
                position = point;
                return true;
            }

            position = default;
            return false;
        }
    }

    private sealed class NamedTeammates : ITeammateVisibility
    {
        private readonly HashSet<string> names = new(StringComparer.Ordinal);

        public void Allow(string name)
        {
            names.Add(name);
        }

        public void Only(string name)
        {
            names.Clear();
            names.Add(name);
        }

        public bool IsVisibleTeammate(string? playerNameOrUid)
        {
            return playerNameOrUid != null && names.Contains(playerNameOrUid);
        }
    }
}
