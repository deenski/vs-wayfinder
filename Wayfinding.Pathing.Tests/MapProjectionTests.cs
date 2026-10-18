using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class MapProjectionTests
{
    static MapViewRect View(double originX = 0, double originY = 0)
    {
        return new MapViewRect
        {
            BlockX1 = 100,
            BlockZ1 = 200,
            BlockX2 = 300,
            BlockZ2 = 600,
            InnerWidth = 400,
            InnerHeight = 200,
            OriginX = originX,
            OriginY = originY,
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
        MapViewRect view = View(10, 20);
        Assert.True(MapProjection.TryWorldToView(view, 250, 400, out float x, out float y));
        Assert.True(MapProjection.TryViewToWorld(view, x, y, out double worldX, out double worldZ));
        Assert.Equal(250, worldX, 3);
        Assert.Equal(400, worldZ, 3);
    }

    [Fact]
    public void Affine_transform_matches_point_projection()
    {
        MapViewRect view = View(15, 25);
        Assert.True(MapProjection.TryViewTransform(view, out MapProjection.ViewTransform transform));

        double[] xs = [100, 150, 300, 50];
        double[] zs = [200, 500, 600, 100];
        for (int i = 0; i < xs.Length; i++)
        {
            Assert.True(MapProjection.TryWorldToView(view, xs[i], zs[i], out float expectedX, out float expectedY));
            transform.Apply(xs[i], zs[i], out float actualX, out float actualY);
            Assert.Equal(expectedX, actualX, 3);
            Assert.Equal(expectedY, actualY, 3);
        }
    }

    [Fact]
    public void Degenerate_view_is_rejected()
    {
        MapViewRect view = View() with { BlockX2 = 100 };
        Assert.False(MapProjection.TryWorldToView(view, 100, 200, out _, out _));
        Assert.False(MapProjection.TryViewTransform(view, out _));
    }

    [Fact]
    public void Clamp_leaves_an_interior_point()
    {
        float x = 40;
        float y = 40;
        MapProjection.ClampPreserveAngle(ref x, ref y, 100, 80, 2);
        Assert.Equal(40, x);
        Assert.Equal(40, y);
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

        MapProjection.ClampPreserveAngle(ref x, ref y, width, height, border);

        Assert.True(MapProjection.IsInsideClampRegion(x, y, width, height, border) || Math.Abs(x - (width - 2)) < 1.5);
        double cross = dirX * (y - centerY) - dirY * (x - centerX);
        Assert.Equal(0, cross, 2);
        Assert.True(x > centerX);
    }

    [Theory]
    [InlineData(0, 0, 3, 4, 5)]
    [InlineData(10, -5, 10, -5, 0)]
    [InlineData(-2, 0, 0, 0, 2)]
    public void Horizontal_distance(double x1, double z1, double x2, double z2, double expected)
    {
        Assert.Equal(expected, MapMath.HorizontalDistance(x1, z1, x2, z2), 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10.4, 10)]
    [InlineData(10.5, 11)]
    [InlineData(-4, 0)]
    public void Rounded_meters_uses_away_from_zero(double distance, int expected)
    {
        Assert.Equal(expected, MapMath.RoundedMeters(distance));
    }
}
