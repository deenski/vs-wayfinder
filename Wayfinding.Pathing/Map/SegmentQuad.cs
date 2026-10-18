namespace Wayfinding.Pathing.Map;

/// <summary>
/// One view-space quad for a line segment. The unit quad is -1..1, so the draw
/// scales by the half extents, rotates by <see cref="AngleRadians"/>, then translates to the center.
/// Thickness is in the same pixel space as the endpoints (already GUI-scaled).
/// </summary>
public readonly struct SegmentQuad
{
    public float CenterX { get; init; }
    public float CenterY { get; init; }
    public float HalfLength { get; init; }
    public float HalfThickness { get; init; }
    public float AngleRadians { get; init; }

    public static bool TryFromEndpoints(float x1, float y1, float x2, float y2, float thickness, out SegmentQuad quad)
    {
        quad = default;
        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f || thickness <= 0)
        {
            return false;
        }

        quad = new SegmentQuad
        {
            CenterX = x1 + dx * 0.5f,
            CenterY = y1 + dy * 0.5f,
            HalfLength = length * 0.5f,
            HalfThickness = thickness * 0.5f,
            AngleRadians = MathF.Atan2(dy, dx),
        };
        return true;
    }
}

public readonly struct DashSegment
{
    public float X1 { get; init; }
    public float Y1 { get; init; }
    public float X2 { get; init; }
    public float Y2 { get; init; }
}

/// <summary>
/// Screen-space dashes. A return of -1 means "draw one solid quad" (too short, or so long
/// that dashing would exceed <paramref name="maxDashes"/>).
/// </summary>
public static class DashLayout
{
    public static int Copy(
        float x1,
        float y1,
        float x2,
        float y2,
        float dashLength,
        float gapLength,
        int maxDashes,
        Span<DashSegment> destination)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f || dashLength <= 0 || maxDashes < 1)
        {
            return length < 1f ? 0 : -1;
        }

        float step = dashLength + Math.Max(0, gapLength);
        if (step <= 0 || length <= dashLength || length / step > maxDashes)
        {
            return -1;
        }

        float ux = dx / length;
        float uy = dy / length;
        int written = 0;
        for (float distance = 0; distance < length; distance += step)
        {
            float end = MathF.Min(distance + dashLength, length);
            if (written < destination.Length)
            {
                destination[written] = new DashSegment
                {
                    X1 = x1 + ux * distance,
                    Y1 = y1 + uy * distance,
                    X2 = x1 + ux * end,
                    Y2 = y1 + uy * end,
                };
            }

            written++;
        }

        return written;
    }
}
