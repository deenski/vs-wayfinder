namespace Wayfinding.Pathing.Map;

/// <summary>Horizontal distances on the map, in blocks.</summary>
public static class MapMath
{
    /// <summary>Euclidean distance in the X/Z plane.</summary>
    public static double HorizontalDistance(double x1, double z1, double x2, double z2)
    {
        double dx = x2 - x1;
        double dz = z2 - z1;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Rounds a block distance to whole meters. Non-positive and NaN become 0.</summary>
    public static int RoundedMeters(double distance)
    {
        if (double.IsNaN(distance) || distance <= 0)
        {
            return 0;
        }

        return (int)Math.Round(distance, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Mouse button that produced a map click.</summary>
public enum MapMouseButton
{
    /// <summary>Primary button.</summary>
    Left,

    /// <summary>Middle button.</summary>
    Middle,

    /// <summary>Secondary button.</summary>
    Right,

    /// <summary>Any other button.</summary>
    Other,
}

/// <summary>One map icon that a cursor can hit.</summary>
public readonly struct IconCandidate
{
    /// <summary>Icon center X in view pixels.</summary>
    public float X { get; init; }

    /// <summary>Icon center Y in view pixels.</summary>
    public float Y { get; init; }

    /// <summary>Half the icon's hit square, in pixels.</summary>
    public float HalfExtent { get; init; }

    /// <summary>Caller-defined icon id.</summary>
    public int Id { get; init; }
}

/// <summary>
/// Ctrl+left click on mouse-up. Right-click and drags are rejected so the map's
/// own waypoint dialog and pan keep working.
/// </summary>
public static class MapClick
{
    /// <summary>Movement past this many pixels is a pan, not a click.</summary>
    public const float DragThresholdPixels = 5f;

    /// <summary>True when the pointer moved farther than <see cref="DragThresholdPixels"/>.</summary>
    public static bool IsDrag(float accumulatedAbsPixels)
    {
        return accumulatedAbsPixels > DragThresholdPixels;
    }

    /// <summary>True for a Ctrl+left click on mouse-up inside the map that was not a drag.</summary>
    public static bool IsNavigationClick(MapMouseButton button, bool ctrlHeld, bool dragged, bool insideMap)
    {
        return insideMap && ctrlHeld && !dragged && button == MapMouseButton.Left;
    }
}

/// <summary>Picking map icons under the cursor.</summary>
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
