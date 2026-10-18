namespace Wayfinding.Pathing.Map;

/// <summary>
/// Clips a segment to an axis-aligned pixel rectangle (Liang-Barsky).
/// The world-map line uses the map element's inner rectangle.
/// </summary>
public static class MapClip
{
    /// <summary>
    /// Clips the segment to the rectangle. Returns false when no part of the segment is inside.
    /// The rectangle is inclusive on every edge.
    /// </summary>
    public static bool TryClip(
        float x1,
        float y1,
        float x2,
        float y2,
        float left,
        float top,
        float right,
        float bottom,
        out float cx1,
        out float cy1,
        out float cx2,
        out float cy2)
    {
        cx1 = x1;
        cy1 = y1;
        cx2 = x2;
        cy2 = y2;
        if (right < left || bottom < top)
        {
            return false;
        }

        float dx = x2 - x1;
        float dy = y2 - y1;
        float u1 = 0f;
        float u2 = 1f;
        if (!ClipEdge(-dx, x1 - left, ref u1, ref u2)
            || !ClipEdge(dx, right - x1, ref u1, ref u2)
            || !ClipEdge(-dy, y1 - top, ref u1, ref u2)
            || !ClipEdge(dy, bottom - y1, ref u1, ref u2))
        {
            return false;
        }

        cx1 = x1 + u1 * dx;
        cy1 = y1 + u1 * dy;
        cx2 = x1 + u2 * dx;
        cy2 = y1 + u2 * dy;
        return true;
    }

    static bool ClipEdge(float p, float q, ref float u1, ref float u2)
    {
        if (MathF.Abs(p) < 1e-6f)
        {
            return q >= -1e-4f;
        }

        float t = q / p;
        if (p < 0f)
        {
            if (t > u2)
            {
                return false;
            }

            if (t > u1)
            {
                u1 = t;
            }
        }
        else
        {
            if (t < u1)
            {
                return false;
            }

            if (t < u2)
            {
                u2 = t;
            }
        }

        return true;
    }
}
