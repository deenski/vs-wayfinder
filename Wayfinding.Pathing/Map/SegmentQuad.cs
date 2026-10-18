namespace Wayfinding.Pathing.Map;

/// <summary>
/// One view-space quad for a line segment. The unit quad is -1..1, so the draw
/// scales by the half extents, rotates by <see cref="AngleRadians"/>, then translates to the center.
/// </summary>
public readonly struct SegmentQuad
{
    /// <summary>Center of the quad, in the same pixel space as the endpoints.</summary>
    public float CenterX { get; init; }

    /// <summary>Center of the quad, in the same pixel space as the endpoints.</summary>
    public float CenterY { get; init; }

    /// <summary>Half the segment length. The unit quad is scaled by this along X before rotation.</summary>
    public float HalfLength { get; init; }

    /// <summary>Half the stroke thickness.</summary>
    public float HalfThickness { get; init; }

    /// <summary>Rotation of the segment, in radians.</summary>
    public float AngleRadians { get; init; }

    /// <summary>Builds a quad. Returns false when the segment is shorter than half a pixel or the thickness is not positive.</summary>
    public static bool TryFromEndpoints(float x1, float y1, float x2, float y2, float thickness, out SegmentQuad quad)
    {
        quad = default;
        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f || thickness <= 0f)
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

/// <summary>One dash in the same pixel space as the segment it came from.</summary>
public readonly struct DashSegment
{
    /// <summary>Start X.</summary>
    public float X1 { get; init; }

    /// <summary>Start Y.</summary>
    public float Y1 { get; init; }

    /// <summary>End X.</summary>
    public float X2 { get; init; }

    /// <summary>End Y.</summary>
    public float Y2 { get; init; }
}

/// <summary>Splits a screen-space segment into dashes.</summary>
public static class DashLayout
{
    /// <summary>
    /// Returns 0 when the segment is shorter than 1 pixel, so there is nothing to draw.
    /// Returns -1 when the caller should draw one solid quad: the dash length is not
    /// positive, <paramref name="maxDashes"/> is below 1, the dash step is not positive, the segment
    /// is no longer than one dash, or the dash count would exceed <paramref name="maxDashes"/>.
    /// A positive return is how many dashes the segment produces. Only
    /// <c>min(count, destination.Length)</c> of them are written.
    /// </summary>
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
        if (length < 1f || dashLength <= 0f || maxDashes < 1)
        {
            return length < 1f ? 0 : -1;
        }

        float step = dashLength + Math.Max(0f, gapLength);
        if (step <= 0f || length <= dashLength || length / step > maxDashes)
        {
            return -1;
        }

        float ux = dx / length;
        float uy = dy / length;
        int written = 0;
        for (float distance = 0f; distance < length; distance += step)
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
