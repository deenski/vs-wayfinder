using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class MapDrawTests
{
    static MapViewRect View()
    {
        return new MapViewRect
        {
            BlockX1 = 100,
            BlockZ1 = 200,
            BlockX2 = 300,
            BlockZ2 = 600,
            InnerWidth = 400,
            InnerHeight = 200,
            OriginX = 0,
            OriginY = 0,
        };
    }

    [Fact]
    public void World_to_view_matches_the_map_element_formula()
    {
        MapViewRect view = View();
        Assert.True(MapProjection.TryWorldToView(view, 100, 200, out float x, out float y));
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.True(MapProjection.TryWorldToView(view, 300, 600, out x, out y));
        Assert.Equal(400, x);
        Assert.Equal(200, y);
        Assert.True(MapProjection.TryWorldToView(view, 200, 400, out x, out y));
        Assert.Equal(200, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void View_to_world_round_trips()
    {
        MapViewRect view = View() with { OriginX = 10, OriginY = 20 };
        Assert.True(MapProjection.TryWorldToView(view, 250, 400, out float x, out float y));
        Assert.True(MapProjection.TryViewToWorld(view, x, y, out double worldX, out double worldZ));
        Assert.Equal(250, worldX, 3);
        Assert.Equal(400, worldZ, 3);
    }

    [Fact]
    public void Degenerate_view_is_rejected()
    {
        MapViewRect view = View() with { BlockX2 = 100 };
        Assert.False(MapProjection.TryWorldToView(view, 100, 200, out _, out _));
        Assert.False(MapProjection.TryViewToWorld(view, 10, 10, out _, out _));
    }

    [Fact]
    public void Clamp_leaves_an_interior_point()
    {
        float x = 40;
        float y = 40;
        MapProjection.ClampPreserveAngle(ref x, ref y, 100, 80, 2);
        Assert.Equal(40, x);
        Assert.Equal(40, y);
        Assert.False(MapEdge.TryEdgePoint(40, 40, 100, 80, 2, out _, out _, out _));
    }

    [Fact]
    public void Clamp_keeps_the_direction_from_the_inset_center()
    {
        float x = 180;
        float y = 50;
        const double width = 100;
        const double height = 80;
        const int border = 2;
        double centerX = width / 2 - border;
        double centerY = height / 2 - border;
        double dirX = x - centerX;
        double dirY = y - centerY;

        Assert.True(MapEdge.TryEdgePoint(x, y, width, height, border, out float edgeX, out float edgeY, out float angle));
        double cross = dirX * (edgeY - centerY) - dirY * (edgeX - centerX);
        Assert.Equal(0, cross, 2);
        Assert.True(edgeX > centerX);
        Assert.Equal(Math.Atan2(dirY, dirX), angle, 2);
    }

    [Fact]
    public void Label_sits_inward_from_the_edge()
    {
        MapEdge.LabelAnchor(98, 40, 100, 80, 12, out float x, out float y);
        Assert.True(x < 98);
        Assert.InRange(y, 39, 41);
        float edgeToCenter = Distance(98, 40, 50, 40);
        float labelToCenter = Distance(x, y, 50, 40);
        Assert.True(labelToCenter < edgeToCenter);
    }

    [Fact]
    public void Inside_segment_is_unchanged()
    {
        Assert.True(MapClip.TryClip(10, 10, 40, 20, 0, 0, 100, 80, out float x1, out float y1, out float x2, out float y2));
        Assert.Equal(10, x1, 3);
        Assert.Equal(10, y1, 3);
        Assert.Equal(40, x2, 3);
        Assert.Equal(20, y2, 3);
    }

    [Fact]
    public void Segment_crossing_the_right_edge_is_clipped()
    {
        Assert.True(MapClip.TryClip(90, 10, 130, 10, 0, 0, 100, 80, out float x1, out float y1, out float x2, out float y2));
        Assert.Equal(90, x1, 3);
        Assert.Equal(10, y1, 3);
        Assert.Equal(100, x2, 3);
        Assert.Equal(10, y2, 3);
    }

    [Fact]
    public void Segment_outside_the_rectangle_is_rejected()
    {
        Assert.False(MapClip.TryClip(-20, 10, -5, 10, 0, 0, 100, 80, out _, out _, out _, out _));
    }

    [Fact]
    public void Diagonal_through_a_corner_stays_inside()
    {
        Assert.True(MapClip.TryClip(-10, 50, 50, -10, 0, 0, 100, 100, out float x1, out float y1, out float x2, out float y2));
        Assert.InRange(x1, 0, 100);
        Assert.InRange(y1, 0, 100);
        Assert.InRange(x2, 0, 100);
        Assert.InRange(y2, 0, 100);
        Assert.True(x1 < x2);
    }

    [Theory]
    [InlineData(0, 0, 412, 0, 412)]
    [InlineData(0, 0, 300, 400, 500)]
    [InlineData(10.4, -2, 10.4, 8, 10)]
    public void Displayed_meters_are_within_one_block(double x1, double z1, double x2, double z2, int expected)
    {
        double distance = MapMath.HorizontalDistance(x1, z1, x2, z2);
        int shown = MapMath.RoundedMeters(distance);
        Assert.Equal(expected, shown);
        Assert.True(Math.Abs(shown - distance) <= 1);
    }

    [Fact]
    public void Distance_label_reads_as_meters()
    {
        Assert.Equal("412 m", DistanceText.Format(412));
        Assert.Equal("0 m", DistanceText.Format(0));
    }

    [Fact]
    public void Icon_square_is_strict()
    {
        Assert.True(MapHit.InsideSquare(10, 10, 14, 10, 5));
        Assert.False(MapHit.InsideSquare(10, 10, 15, 10, 5));
    }

    [Fact]
    public void Remote_player_prefers_the_entity_then_the_packet()
    {
        Assert.True(RemotePlayerPosition.TryHorizontal(true, 3, 4, true, false, 9, 9, out double x, out double z));
        Assert.Equal(3, x);
        Assert.Equal(4, z);

        Assert.True(RemotePlayerPosition.TryHorizontal(false, 0, 0, true, false, 8, -2, out x, out z));
        Assert.Equal(8, x);
        Assert.Equal(-2, z);

        Assert.False(RemotePlayerPosition.TryHorizontal(false, 0, 0, true, true, 8, -2, out _, out _));
        Assert.False(RemotePlayerPosition.TryHorizontal(false, 0, 0, false, false, 8, -2, out _, out _));
    }

    [Fact]
    public void Subpixel_dash_returns_zero_and_a_zero_dash_length_stays_solid()
    {
        Span<DashSegment> dashes = stackalloc DashSegment[2];
        Assert.Equal(0, DashLayout.Copy(0, 0, 0.4f, 0, 7, 5, 64, dashes));
        Assert.Equal(-1, DashLayout.Copy(0, 0, 20, 0, 0, 5, 64, dashes));
    }

    static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
