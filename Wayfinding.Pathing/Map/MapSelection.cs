namespace Wayfinding.Pathing.Map;

/// <summary>Which mouse gesture sets a navigation target. Stored in <c>wayfinding.json</c>.</summary>
public enum ClickBinding
{
    /// <summary>Ctrl+left click. The default.</summary>
    CtrlLeft,

    /// <summary>Middle click.</summary>
    Middle,

    /// <summary>Ctrl+left click or middle click.</summary>
    Both,
}

/// <summary>Reads <c>clickBinding</c> from the client config.</summary>
public static class ClickBindings
{
    /// <summary>
    /// Maps <paramref name="text"/> to a binding. Blank text is <see cref="ClickBinding.CtrlLeft"/>.
    /// An unknown value is also <see cref="ClickBinding.CtrlLeft"/> and <paramref name="recognized"/> is false.
    /// </summary>
    public static ClickBinding Parse(string? text, out bool recognized)
    {
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "CtrlLeft", StringComparison.OrdinalIgnoreCase))
        {
            recognized = true;
            return ClickBinding.CtrlLeft;
        }

        if (string.Equals(text, "Middle", StringComparison.OrdinalIgnoreCase))
        {
            recognized = true;
            return ClickBinding.Middle;
        }

        if (string.Equals(text, "Both", StringComparison.OrdinalIgnoreCase))
        {
            recognized = true;
            return ClickBinding.Both;
        }

        recognized = false;
        return ClickBinding.CtrlLeft;
    }
}

/// <summary>
/// Whether a mouse-up on the world map selects a target. Right-click never does.
/// The press and the release must be the same button and no more than
/// <see cref="MapClick.DragThresholdPixels"/> apart.
/// </summary>
public static class MapSelection
{
    /// <summary>
    /// True when this release should set a target.
    /// </summary>
    /// <param name="binding">Configured gesture.</param>
    /// <param name="button">Button that was released.</param>
    /// <param name="ctrlHeld">Whether the rebindable Ctrl key or right Ctrl is down.</param>
    /// <param name="insideMap">Whether the cursor is inside the map.</param>
    /// <param name="worldMap">Whether the open map is the full world-map dialog, not the minimap HUD.</param>
    /// <param name="hasPress">Whether a mouse-down for <paramref name="button"/> was recorded.</param>
    /// <param name="pressX">Mouse-down X in window pixels.</param>
    /// <param name="pressY">Mouse-down Y in window pixels.</param>
    /// <param name="releaseX">Mouse-up X in window pixels.</param>
    /// <param name="releaseY">Mouse-up Y in window pixels.</param>
    public static bool IsSelection(
        ClickBinding binding,
        MapMouseButton button,
        bool ctrlHeld,
        bool insideMap,
        bool worldMap,
        bool hasPress,
        int pressX,
        int pressY,
        int releaseX,
        int releaseY)
    {
        if (!worldMap || !insideMap || !hasPress || !ButtonMatches(binding, button, ctrlHeld))
        {
            return false;
        }

        return WithinPress(pressX, pressY, releaseX, releaseY);
    }

    /// <summary>True when the button matches the binding. Right-click and other buttons never match.</summary>
    public static bool ButtonMatches(ClickBinding binding, MapMouseButton button, bool ctrlHeld)
    {
        bool ctrlLeft = button == MapMouseButton.Left && ctrlHeld;
        bool middle = button == MapMouseButton.Middle;
        return binding switch
        {
            ClickBinding.CtrlLeft => ctrlLeft,
            ClickBinding.Middle => middle,
            ClickBinding.Both => ctrlLeft || middle,
            _ => false,
        };
    }

    /// <summary>True when the release is at most <see cref="MapClick.DragThresholdPixels"/> from the press.</summary>
    public static bool WithinPress(int pressX, int pressY, int releaseX, int releaseY)
    {
        long dx = (long)pressX - releaseX;
        long dy = (long)pressY - releaseY;
        long limit = (long)MapClick.DragThresholdPixels;
        return dx * dx + dy * dy <= limit * limit;
    }
}

/// <summary>
/// Player icons may be hit only after <c>ITeammateVisibility</c> accepts the uid.
/// A hidden player is not a hit, so the click falls through to a coordinate.
/// </summary>
public static class PlayerPrivacy
{
    /// <summary>
    /// True when <paramref name="uid"/> is non-empty and <paramref name="isVisibleTeammate"/> is true.
    /// Call this before using the uid in a hit, a name, a log line, or chat.
    /// </summary>
    public static bool MayHit(string? uid, bool isVisibleTeammate)
    {
        return !string.IsNullOrEmpty(uid) && isVisibleTeammate;
    }
}
