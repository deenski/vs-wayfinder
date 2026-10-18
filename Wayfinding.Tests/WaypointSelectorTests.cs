using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class WaypointSelectorTests
{
    private static readonly Guid HomeId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly Guid CampId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    private static readonly Guid MineId = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    private static List<WaypointRef> Sample()
    {
        return new List<WaypointRef>
        {
            new(HomeId, "Home"),
            new(CampId, "Camp"),
            new(MineId, "Mine"),
        };
    }

    [Fact]
    public void Index_zero_is_the_first_waypoint()
    {
        var selected = WaypointSelector.Select("0", Sample());

        Assert.True(selected.Ok);
        Assert.Equal(HomeId, selected.Waypoint.Id);
        Assert.Equal("Home", selected.Waypoint.Name);
    }

    [Fact]
    public void Hash_index_matches_the_list_index()
    {
        var selected = WaypointSelector.Select("#2", Sample());

        Assert.True(selected.Ok);
        Assert.Equal(MineId, selected.Waypoint.Id);
    }

    [Fact]
    public void Hash_index_wins_over_a_title_with_the_same_text()
    {
        var waypoints = new List<WaypointRef>
        {
            new(HomeId, "#0"),
            new(CampId, "Camp"),
        };

        var selected = WaypointSelector.Select("#0", waypoints);

        Assert.True(selected.Ok);
        Assert.Equal(HomeId, selected.Waypoint.Id);
    }

    [Fact]
    public void Name_match_is_case_insensitive()
    {
        var selected = WaypointSelector.Select("  home  ", Sample());

        Assert.True(selected.Ok);
        Assert.Equal(HomeId, selected.Waypoint.Id);
        Assert.Equal("Home", selected.Waypoint.Name);
    }

    [Fact]
    public void Quoted_number_is_a_name()
    {
        var waypoints = new List<WaypointRef>
        {
            new(HomeId, "0"),
            new(CampId, "Camp"),
        };

        var byName = WaypointSelector.Select("\"0\"", waypoints);
        Assert.True(byName.Ok);
        Assert.Equal(HomeId, byName.Waypoint.Id);
    }

    [Fact]
    public void Bare_number_that_is_both_an_index_and_a_name_is_rejected()
    {
        var waypoints = new List<WaypointRef>
        {
            new(HomeId, "0"),
            new(CampId, "Camp"),
        };

        var selected = WaypointSelector.Select("0", waypoints);

        Assert.False(selected.Ok);
        Assert.Contains("both waypoint index 0", selected.Message, StringComparison.Ordinal);
        Assert.Contains("#0", selected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Number_outside_the_list_can_still_be_a_name()
    {
        var waypoints = new List<WaypointRef>
        {
            new(HomeId, "9"),
            new(CampId, "Camp"),
        };

        var selected = WaypointSelector.Select("9", waypoints);

        Assert.True(selected.Ok);
        Assert.Equal(HomeId, selected.Waypoint.Id);
    }

    [Fact]
    public void Duplicate_names_ask_for_an_index()
    {
        var waypoints = new List<WaypointRef>
        {
            new(HomeId, "Home"),
            new(CampId, "Camp"),
            new(MineId, "home"),
        };

        var selected = WaypointSelector.Select("HOME", waypoints);

        Assert.False(selected.Ok);
        Assert.Equal("More than one waypoint is named \"HOME\". Use an index (#0 or #2).", selected.Message);
    }

    [Fact]
    public void Unknown_name_is_an_error()
    {
        var selected = WaypointSelector.Select("forge", Sample());

        Assert.False(selected.Ok);
        Assert.Equal("No waypoint named \"forge\".", selected.Message);
    }

    [Fact]
    public void Empty_list_is_an_error()
    {
        var selected = WaypointSelector.Select("#0", new List<WaypointRef>());

        Assert.False(selected.Ok);
        Assert.Equal("You have no waypoints.", selected.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_argument_explains_the_usage(string argument)
    {
        var selected = WaypointSelector.Select(argument, Sample());

        Assert.False(selected.Ok);
        Assert.Equal(WaypointSelector.Usage, selected.Message);
    }

    [Fact]
    public void Negative_index_is_rejected()
    {
        var selected = WaypointSelector.Select("#-1", Sample());

        Assert.False(selected.Ok);
        Assert.Equal("Waypoint indexes start at 0.", selected.Message);
    }

    [Fact]
    public void Hash_without_a_number_is_rejected()
    {
        var selected = WaypointSelector.Select("#home", Sample());

        Assert.False(selected.Ok);
        Assert.Equal("\"#home\" is not a waypoint index.", selected.Message);
    }

    [Fact]
    public void Index_past_the_end_names_the_valid_range()
    {
        var selected = WaypointSelector.Select("3", Sample());

        Assert.False(selected.Ok);
        Assert.Equal("There is no waypoint 3. Valid indexes are 0..2.", selected.Message);
    }

    [Fact]
    public void Single_waypoint_range_is_zero_through_zero()
    {
        var selected = WaypointSelector.Select("#1", new List<WaypointRef> { new(HomeId, "Home") });

        Assert.False(selected.Ok);
        Assert.Equal("There is no waypoint 1. Valid indexes are 0..0.", selected.Message);
    }

    [Fact]
    public void Waypoint_without_an_id_cannot_be_tracked()
    {
        var selected = WaypointSelector.Select("0", new List<WaypointRef> { WaypointRef.Missing("Home") });

        Assert.False(selected.Ok);
        Assert.Equal("Waypoint \"Home\" has no id, so it can't be tracked.", selected.Message);
    }

    [Fact]
    public void Leading_zero_is_index_zero()
    {
        var selected = WaypointSelector.Select("#00", Sample());

        Assert.True(selected.Ok);
        Assert.Equal(HomeId, selected.Waypoint.Id);
    }
}
