namespace Wayfinding.Pathing.Map;

/// <summary>Edge arrow placement for a point that sits outside the minimap.</summary>
public static class MapEdge
{
    /// <summary>Inset used by the game's pinned-icon clamp, in pixels.</summary>
    public const int ArrowBorder = 2;

    /// <summary>
    /// When the point is outside the minimap, writes the edge position and the direction
    /// from that point toward the original point. Returns false when the point is already inside.
    /// </summary>
    public static bool TryEdgePoint(
        float x,
        float y,
        double innerWidth,
        double innerHeight,
        int border,
        out float edgeX,
        out float edgeY,
        out float angleRadians)
    {
        edgeX = x;
        edgeY = y;
        angleRadians = 0f;
        if (MapProjection.IsInsideClampRegion(x, y, innerWidth, innerHeight, border))
        {
            return false;
        }

        MapProjection.ClampPreserveAngle(ref edgeX, ref edgeY, innerWidth, innerHeight, border);
        angleRadians = MathF.Atan2(y - edgeY, x - edgeX);
        return true;
    }

    /// <summary>A point <paramref name="offset"/> pixels from the edge toward the view center.</summary>
    public static void LabelAnchor(
        float edgeX,
        float edgeY,
        double innerWidth,
        double innerHeight,
        float offset,
        out float x,
        out float y)
    {
        float centerX = (float)(innerWidth / 2);
        float centerY = (float)(innerHeight / 2);
        float dx = centerX - edgeX;
        float dy = centerY - edgeY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f)
        {
            x = edgeX;
            y = edgeY;
            return;
        }

        x = edgeX + dx / length * offset;
        y = edgeY + dy / length * offset;
    }
}
