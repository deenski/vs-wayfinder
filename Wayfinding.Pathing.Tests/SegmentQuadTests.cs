using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class SegmentQuadTests
{
    [Fact]
    public void Horizontal_segment_is_centered_and_unrotated()
    {
        Assert.True(SegmentQuad.TryFromEndpoints(0, 10, 10, 10, 2, out SegmentQuad quad));
        Assert.Equal(5, quad.CenterX);
        Assert.Equal(10, quad.CenterY);
        Assert.Equal(5, quad.HalfLength);
        Assert.Equal(1, quad.HalfThickness);
        Assert.Equal(0, quad.AngleRadians, 5);
    }

    [Fact]
    public void Diagonal_segment_uses_atan2()
    {
        Assert.True(SegmentQuad.TryFromEndpoints(0, 0, 3, 4, 4, out SegmentQuad quad));
        Assert.Equal(1.5f, quad.CenterX, 5);
        Assert.Equal(2f, quad.CenterY, 5);
        Assert.Equal(2.5f, quad.HalfLength, 5);
        Assert.Equal(2f, quad.HalfThickness, 5);
        Assert.Equal(MathF.Atan2(4, 3), quad.AngleRadians, 5);
    }

    [Fact]
    public void Tiny_segment_is_skipped()
    {
        Assert.False(SegmentQuad.TryFromEndpoints(1, 1, 1.2f, 1, 2, out _));
    }

    [Fact]
    public void Dashes_split_a_known_length()
    {
        Span<DashSegment> dashes = stackalloc DashSegment[8];
        int count = DashLayout.Copy(0, 0, 30, 0, dashLength: 7, gapLength: 5, maxDashes: 300, dashes);
        Assert.Equal(3, count);
        Assert.Equal(0, dashes[0].X1);
        Assert.Equal(7, dashes[0].X2);
        Assert.Equal(12, dashes[1].X1);
        Assert.Equal(19, dashes[1].X2);
        Assert.Equal(24, dashes[2].X1);
        Assert.Equal(30, dashes[2].X2);
    }

    [Fact]
    public void Short_or_huge_segments_stay_solid()
    {
        Span<DashSegment> dashes = stackalloc DashSegment[4];
        Assert.Equal(-1, DashLayout.Copy(0, 0, 5, 0, 7, 5, 300, dashes));
        Assert.Equal(-1, DashLayout.Copy(0, 0, 10_000, 0, 7, 5, 10, dashes));
        Assert.Equal(-1, DashLayout.Copy(0, 0, 20, 0, 0, 5, 300, dashes));
    }

    [Fact]
    public void Subpixel_segment_returns_zero()
    {
        Span<DashSegment> dashes = stackalloc DashSegment[2];
        Assert.Equal(0, DashLayout.Copy(0, 0, 0.4f, 0, 7, 5, 300, dashes));
    }
}
