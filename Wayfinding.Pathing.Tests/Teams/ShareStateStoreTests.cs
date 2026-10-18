using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class ShareStateStoreTests
{
    [Fact]
    public void Missing_world_defaults_to_sharing_on()
    {
        var store = new ShareStateStore();

        Assert.True(store.IsSharing("world-a"));
        Assert.Null(store.TeamGroup("world-a"));
    }

    [Fact]
    public void Share_flag_and_team_group_round_trip_per_world()
    {
        var store = new ShareStateStore();
        store.SetSharing("world-a", false);
        store.SetTeamGroup("world-a", "scouts");
        store.SetSharing("world-b", true);

        var restored = ShareStateStore.Import(store.Export());

        Assert.False(restored.IsSharing("world-a"));
        Assert.Equal("scouts", restored.TeamGroup("world-a"));
        Assert.True(restored.IsSharing("world-b"));
        Assert.Null(restored.TeamGroup("world-b"));
        Assert.True(restored.IsSharing("world-c"));
    }

    [Fact]
    public void Clearing_the_team_group_keeps_the_share_flag()
    {
        var store = new ShareStateStore();
        store.SetSharing("world-a", false);
        store.SetTeamGroup("world-a", "scouts");

        store.SetTeamGroup("world-a", "  ");

        Assert.False(store.IsSharing("world-a"));
        Assert.Null(store.TeamGroup("world-a"));
    }

    [Fact]
    public void Import_skips_records_without_a_world_id()
    {
        var store = ShareStateStore.Import(
        [
            new WorldTeamPreference("", false, "scouts"),
            new WorldTeamPreference("world-a", false, null),
        ]);

        Assert.False(store.IsSharing("world-a"));
        Assert.True(store.IsSharing(""));
    }
}
