using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class NavCoordParserTests
{
    private static readonly SpawnOrigin Spawn = new(512000, 512000);
    private const int MapHeight = 256;

    [Fact]
    public void X_z_is_spawn_relative_and_y_is_left_unset()
    {
        var ok = NavCoordParser.TryParse("10 -4", Spawn, MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal("", error);
        Assert.Equal(10, coord.DisplayX);
        Assert.Null(coord.DisplayY);
        Assert.Equal(-4, coord.DisplayZ);
        Assert.Equal(512010, coord.Absolute.X);
        Assert.Null(coord.Absolute.Y);
        Assert.Equal(511996, coord.Absolute.Z);
    }

    [Fact]
    public void X_y_z_keeps_y_as_absolute_height()
    {
        var ok = NavCoordParser.TryParse("10 70 -4", Spawn, MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(70, coord.DisplayY);
        Assert.Equal(512010, coord.Absolute.X);
        Assert.Equal(70, coord.Absolute.Y);
        Assert.Equal(511996, coord.Absolute.Z);
    }

    [Fact]
    public void Y_zero_is_a_real_height()
    {
        var ok = NavCoordParser.TryParse("1 0 2", new SpawnOrigin(0, 0), MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(0, coord.Absolute.Y);
    }

    [Fact]
    public void Spawn_at_zero_leaves_x_and_z_unchanged()
    {
        var ok = NavCoordParser.TryParse("-20 5", new SpawnOrigin(0, 0), MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(-20, coord.Absolute.X);
        Assert.Null(coord.Absolute.Y);
        Assert.Equal(5, coord.Absolute.Z);
    }

    [Theory]
    [InlineData("10, 70, -4")]
    [InlineData("10,70,-4")]
    [InlineData("10, 70, -4,")]
    [InlineData("  10   70   -4  ")]
    [InlineData("+10 +70 -4")]
    public void Accepts_commas_signs_and_extra_space(string text)
    {
        var ok = NavCoordParser.TryParse(text, Spawn, MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(512010, coord.Absolute.X);
        Assert.Equal(70, coord.Absolute.Y);
        Assert.Equal(511996, coord.Absolute.Z);
    }

    [Fact]
    public void Comma_form_of_x_z()
    {
        var ok = NavCoordParser.TryParse("10,-4", Spawn, MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Null(coord.Absolute.Y);
        Assert.Equal(512010, coord.Absolute.X);
        Assert.Equal(511996, coord.Absolute.Z);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10")]
    public void Missing_coordinates_explain_the_usage(string text)
    {
        var ok = NavCoordParser.TryParse(text, Spawn, MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.Equal(NavCoordParser.Usage, error);
    }

    [Fact]
    public void Too_many_coordinates_are_rejected()
    {
        var ok = NavCoordParser.TryParse("1 2 3 4", Spawn, MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.StartsWith("Too many coordinates.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.5 -4", "10.5")]
    [InlineData("10 -4.0", "-4.0")]
    [InlineData("0x10 4", "0x10")]
    [InlineData("1e2 4", "1e2")]
    [InlineData("ten -4", "ten")]
    public void Non_integers_are_rejected(string text, string token)
    {
        var ok = NavCoordParser.TryParse(text, Spawn, MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.Equal("\"" + token + "\" is not a whole number.", error);
    }

    [Theory]
    [InlineData("10,,-4")]
    [InlineData(",10 -4")]
    public void Empty_comma_parts_are_rejected(string text)
    {
        var ok = NavCoordParser.TryParse(text, Spawn, MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.Contains("is not a coordinate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Addition_that_overflows_int_is_rejected()
    {
        var ok = NavCoordParser.TryParse("10 0", new SpawnOrigin(int.MaxValue - 5, 0), MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.Equal(NavCoordParser.CoordinateOverflowMessage, error);
    }

    [Fact]
    public void Negative_relative_coordinates_move_west_and_north_of_spawn()
    {
        var ok = NavCoordParser.TryParse("-20 5", new SpawnOrigin(1000, 1000), MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(980, coord.Absolute.X);
        Assert.Equal(1005, coord.Absolute.Z);
    }

    [Theory]
    [InlineData(-9999)]
    [InlineData(-1)]
    [InlineData(256)]
    public void Y_outside_the_world_height_is_rejected(int y)
    {
        var ok = NavCoordParser.TryParse("0 " + y + " 0", Spawn, MapHeight, out _, out var error);

        Assert.False(ok);
        Assert.Equal(NavCoordParser.HeightOutsideWorldMessage(y, MapHeight), error);
    }

    [Fact]
    public void Y_at_the_top_block_is_inside_the_world()
    {
        var ok = NavCoordParser.TryParse("0 255 0", Spawn, MapHeight, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Equal(255, coord.Absolute.Y);
    }

    [Fact]
    public void Explicit_y_is_rejected_when_the_world_height_is_unknown()
    {
        var ok = NavCoordParser.TryParse("0 1 0", Spawn, 0, out _, out var error);

        Assert.False(ok);
        Assert.Equal(NavCoordParser.WorldHeightUnknownMessage, error);
    }

    [Fact]
    public void X_z_does_not_need_a_world_height()
    {
        var ok = NavCoordParser.TryParse("10 -4", Spawn, 0, out var coord, out var error);

        Assert.True(ok, error);
        Assert.Null(coord.Absolute.Y);
        Assert.Equal(512010, coord.Absolute.X);
    }
}
