using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class MapSelectionTests
{
    [Fact]
    public void Selection_requires_the_binding_the_same_button_and_a_short_press_on_the_world_map()
    {
        Assert.True(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, true, true, true, 0, 0, 5, 0));
        Assert.True(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, true, true, true, 0, 0, 3, 4));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, true, true, true, 0, 0, 4, 4));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, false, true, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Middle, true, true, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Right, true, true, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.Middle, MapMouseButton.Right, false, true, true, 0, 0, 0, 0));
        Assert.True(Select(ClickBinding.Middle, MapMouseButton.Middle, false, true, true, 10, 10, 10, 10));
        Assert.False(Select(ClickBinding.Middle, MapMouseButton.Left, true, true, true, 0, 0, 0, 0));
        Assert.True(Select(ClickBinding.Both, MapMouseButton.Left, true, true, true, 0, 0, 0, 0));
        Assert.True(Select(ClickBinding.Both, MapMouseButton.Middle, false, true, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.Both, MapMouseButton.Left, true, false, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.Both, MapMouseButton.Middle, false, false, true, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, true, true, false, 0, 0, 0, 0));
        Assert.False(Select(ClickBinding.CtrlLeft, MapMouseButton.Left, true, true, true, 0, 0, 0, 0, false));
    }

    [Fact]
    public void Click_binding_text_accepts_the_three_names()
    {
        Assert.Equal(ClickBinding.CtrlLeft, ClickBindings.Parse(null, out bool blank));
        Assert.True(blank);
        Assert.Equal(ClickBinding.CtrlLeft, ClickBindings.Parse("ctrlleft", out bool lower));
        Assert.True(lower);
        Assert.Equal(ClickBinding.Middle, ClickBindings.Parse("Middle", out bool middle));
        Assert.True(middle);
        Assert.Equal(ClickBinding.Both, ClickBindings.Parse("both", out bool both));
        Assert.True(both);
        Assert.Equal(ClickBinding.CtrlLeft, ClickBindings.Parse("Alt", out bool unknown));
        Assert.False(unknown);
    }

    [Fact]
    public void Hidden_player_is_not_a_hit_and_falls_through()
    {
        Assert.False(PlayerPrivacy.MayHit("hidden-uid", isVisibleTeammate: false));
        Assert.False(PlayerPrivacy.MayHit(null, isVisibleTeammate: true));
        Assert.False(PlayerPrivacy.MayHit("", isVisibleTeammate: true));
        Assert.True(PlayerPrivacy.MayHit("maili", isVisibleTeammate: true));
    }

    static bool Select(
        ClickBinding binding,
        MapMouseButton button,
        bool ctrl,
        bool worldMap,
        bool hasPress,
        int pressX,
        int pressY,
        int releaseX,
        int releaseY,
        bool inside = true)
    {
        return MapSelection.IsSelection(
            binding, button, ctrl, inside, worldMap, hasPress, pressX, pressY, releaseX, releaseY);
    }
}
