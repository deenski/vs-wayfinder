using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class MapClickTests
{
    [Theory]
    [InlineData(MapMouseButton.Left, true, false, true, true)]
    [InlineData(MapMouseButton.Left, false, false, true, false)]
    [InlineData(MapMouseButton.Right, true, false, true, false)]
    [InlineData(MapMouseButton.Middle, true, false, true, false)]
    [InlineData(MapMouseButton.Left, true, true, true, false)]
    [InlineData(MapMouseButton.Left, true, false, false, false)]
    public void Navigation_click_is_ctrl_left_inside_without_a_drag(
        MapMouseButton button,
        bool ctrl,
        bool dragged,
        bool inside,
        bool expected)
    {
        Assert.Equal(expected, MapClick.IsNavigationClick(button, ctrl, dragged, inside));
    }

    [Fact]
    public void Drag_starts_past_five_pixels()
    {
        Assert.False(MapClick.IsDrag(0));
        Assert.False(MapClick.IsDrag(MapClick.DragThresholdPixels));
        Assert.True(MapClick.IsDrag(MapClick.DragThresholdPixels + 0.1f));
    }

    [Fact]
    public void Square_hit_matches_vanilla_half_extents()
    {
        Assert.True(MapHitTest.InsideSquare(10, 10, 14, 12, 8));
        Assert.False(MapHitTest.InsideSquare(10, 10, 18, 10, 8));
        Assert.False(MapHitTest.InsideSquare(10, 10, 10, 10, 0));
    }

    [Fact]
    public void Pick_closest_icon_and_ignore_misses()
    {
        IconCandidate[] icons =
        [
            new() { X = 0, Y = 0, HalfExtent = 5, Id = 1 },
            new() { X = 10, Y = 0, HalfExtent = 5, Id = 2 },
            new() { X = 4, Y = 0, HalfExtent = 3, Id = 3 },
        ];

        Assert.Equal(2, MapHitTest.PickClosest(icons, 4, 0));
        Assert.Equal(1, MapHitTest.PickClosest(icons, 10, 1));
        Assert.Equal(-1, MapHitTest.PickClosest(icons, 40, 40));
    }

    [Fact]
    public void Equal_distance_keeps_the_earlier_icon()
    {
        IconCandidate[] icons =
        [
            new() { X = 0, Y = 0, HalfExtent = 4, Id = 7 },
            new() { X = 0, Y = 0, HalfExtent = 4, Id = 8 },
        ];

        Assert.Equal(0, MapHitTest.PickClosest(icons, 1, 0));
    }
}
