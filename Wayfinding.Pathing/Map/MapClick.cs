namespace Wayfinding.Pathing.Map;

public static class MapMath
{
    public static double HorizontalDistance(double x1, double z1, double x2, double z2)
    {
        double dx = x2 - x1;
        double dz = z2 - z1;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public static int RoundedMeters(double distance)
    {
        if (double.IsNaN(distance) || distance <= 0)
        {
            return 0;
        }

        return (int)Math.Round(distance, MidpointRounding.AwayFromZero);
    }
}

public enum MapMouseButton
{
    Left,
    Middle,
    Right,
    Other,
}

public readonly struct IconCandidate
{
    public float X { get; init; }
    public float Y { get; init; }
    public float HalfExtent { get; init; }
    public int Id { get; init; }
}

/// <summary>
/// Ctrl+left click on mouse-up. Right-click and drags are rejected so the map's
/// own waypoint dialog and pan keep working.
/// </summary>
public static class MapClick
{
    public const float DragThresholdPixels = 5f;

    public static bool IsDrag(float accumulatedAbsPixels)
    {
        return accumulatedAbsPixels > DragThresholdPixels;
    }

    public static bool IsNavigationClick(MapMouseButton button, bool ctrlHeld, bool dragged, bool insideMap)
    {
        return insideMap && ctrlHeld && !dragged && button == MapMouseButton.Left;
    }
}

public static class MapHitTest
{
    /// <summary>
    /// Axis-aligned square, matching vanilla icon tests (<c>abs(dx) &lt; half</c> and the same for y).
    /// </summary>
    public static bool InsideSquare(float iconX, float iconY, float mouseX, float mouseY, float halfExtent)
    {
        return MathF.Abs(iconX - mouseX) < halfExtent && MathF.Abs(iconY - mouseY) < halfExtent;
    }

    /// <summary>
    /// Closest icon whose square contains the cursor. Ties keep the earlier candidate.
    /// Returns -1 when nothing hits.
    /// </summary>
    public static int PickClosest(ReadOnlySpan<IconCandidate> icons, float mouseX, float mouseY)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < icons.Length; i++)
        {
            IconCandidate icon = icons[i];
            float dx = icon.X - mouseX;
            float dy = icon.Y - mouseY;
            if (MathF.Abs(dx) >= icon.HalfExtent || MathF.Abs(dy) >= icon.HalfExtent)
            {
                continue;
            }

            float distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
