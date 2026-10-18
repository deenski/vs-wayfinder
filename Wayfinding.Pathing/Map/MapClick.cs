namespace Wayfinding.Pathing.Map;

/// <summary>Mouse button for a map click, reduced to the buttons the map cares about.</summary>
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

/// <summary>One clickable map icon in view pixels.</summary>
public readonly struct IconCandidate
{
    /// <summary>Icon center X in view pixels.</summary>
    public float X { get; init; }

    /// <summary>Icon center Y in view pixels.</summary>
    public float Y { get; init; }

    /// <summary>Half the icon's click square, in view pixels.</summary>
    public float HalfExtent { get; init; }

    /// <summary>Caller-defined icon id. Hit testing returns the candidate index, not this id.</summary>
    public int Id { get; init; }
}

/// <summary>
/// Ctrl+left click on mouse-up. Right-click and drags are rejected so the map's
/// own waypoint dialog and pan keep working.
/// </summary>
public static class MapClick
{
    /// <summary>Movement, in pixels, above which a press is a drag rather than a click.</summary>
    public const float DragThresholdPixels = 5f;

    /// <summary>True when the pointer moved farther than <see cref="DragThresholdPixels"/>.</summary>
    /// <param name="accumulatedAbsPixels">Absolute pointer travel during the press.</param>
    public static bool IsDrag(float accumulatedAbsPixels)
    {
        return accumulatedAbsPixels > DragThresholdPixels;
    }

    /// <summary>True for a Ctrl+left click that stayed inside the map and was not a drag.</summary>
    /// <param name="button">Button that was released.</param>
    /// <param name="ctrlHeld">Whether Ctrl was held.</param>
    /// <param name="dragged">Whether the press crossed <see cref="DragThresholdPixels"/>.</param>
    /// <param name="insideMap">Whether the cursor was inside the map element.</param>
    public static bool IsNavigationClick(MapMouseButton button, bool ctrlHeld, bool dragged, bool insideMap)
    {
        return insideMap && ctrlHeld && !dragged && button == MapMouseButton.Left;
    }
}

/// <summary>Picks a map icon under the cursor.</summary>
public static class MapHitTest
{
    /// <summary>
    /// Axis-aligned square, matching vanilla icon tests (<c>abs(dx) &lt; half</c> and the same for y).
    /// </summary>
    /// <param name="iconX">Icon center X.</param>
    /// <param name="iconY">Icon center Y.</param>
    /// <param name="mouseX">Cursor X.</param>
    /// <param name="mouseY">Cursor Y.</param>
    /// <param name="halfExtent">Half the icon's click square.</param>
    public static bool InsideSquare(float iconX, float iconY, float mouseX, float mouseY, float halfExtent)
    {
        return MathF.Abs(iconX - mouseX) < halfExtent && MathF.Abs(iconY - mouseY) < halfExtent;
    }

    /// <summary>
    /// Closest icon whose square contains the cursor. Ties keep the earlier candidate.
    /// Returns -1 when nothing hits.
    /// </summary>
    /// <param name="icons">Icons in view pixels.</param>
    /// <param name="mouseX">Cursor X.</param>
    /// <param name="mouseY">Cursor Y.</param>
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
