namespace Wayfinding.Pathing.Map;

/// <summary>
/// One view-space quad for a line segment. The unit quad is -1..1, so the draw
/// scales by the half extents, rotates by <see cref="AngleRadians"/>, then translates to the center.
/// Thickness is in the same pixel space as the endpoints (already GUI-scaled).
/// </summary>
public readonly struct SegmentQuad
{
    /// <summary>Center X in view pixels.</summary>
    public float CenterX { get; init; }

    /// <summary>Center Y in view pixels.</summary>
    public float CenterY { get; init; }

    /// <summary>Half the segment length in pixels.</summary>
    public float HalfLength { get; init; }

    /// <summary>Half the stroke thickness in pixels.</summary>
    public float HalfThickness { get; init; }

    /// <summary>Rotation of the segment, from <c>Atan2</c>, in radians.</summary>
    public float AngleRadians { get; init; }

    /// <summary>
    /// Builds a quad from endpoints. False when the segment is shorter than half a pixel
    /// or the thickness is not positive.
    /// </summary>
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

/// <summary>One dash in view pixels.</summary>
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
    /// Writes dashes into <paramref name="destination"/>.
    /// Returns 0 when the segment is shorter than 1 pixel, so there is nothing to draw.
    /// Returns -1 when the caller should draw one solid quad: <paramref name="dashLength"/> is not
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
