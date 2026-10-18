namespace Wayfinding.Pathing.Map;

/// <summary>When the map hover line may show, and when its cache must be dropped.</summary>
public static class HoverLabelPolicy
{
    /// <summary>The hover line is shown only while a target exists and the layer is still drawing.</summary>
    public static bool Show(bool hasTarget, bool layerEnabled)
    {
        return hasTarget && layerEnabled;
    }

    /// <summary>
    /// Drop the cached label when the target is cleared, the layer is disabled, or the target revision changes.
    /// </summary>
    public static bool ShouldReset(int seenRevision, int targetRevision, bool hasTarget, bool layerEnabled)
    {
        return !hasTarget || !layerEnabled || seenRevision != targetRevision;
    }
}

/// <summary>Clicks stop once an input read has failed.</summary>
public static class MapInputPolicy
{
    /// <summary>False after the input guard has disabled clicks.</summary>
    public static bool AllowsInput(bool inputDisabled)
    {
        return !inputDisabled;
    }
}

/// <summary>How often the distance texture may be uploaded.</summary>
public static class LabelUpload
{
    /// <summary>Minimum time between texture uploads while the rounded distance is changing.</summary>
    public const int MinIntervalMs = 250;

    /// <summary>
    /// Upload when no texture has been uploaded yet, or when the rounded meters changed and
    /// <see cref="MinIntervalMs"/> has elapsed. An unchanged distance does not upload again.
    /// </summary>
    /// <param name="meters">Rounded distance to show.</param>
    /// <param name="uploadedMeters">Meters in the current texture, or a negative value when nothing has been uploaded.</param>
    /// <param name="nowMs">Current time in milliseconds.</param>
    /// <param name="uploadedAtMs">Time of the last upload.</param>
    public static bool ShouldUpload(int meters, int uploadedMeters, long nowMs, long uploadedAtMs)
    {
        if (uploadedMeters < 0)
        {
            return true;
        }

        if (meters == uploadedMeters)
        {
            return false;
        }

        return nowMs - uploadedAtMs >= MinIntervalMs;
    }
}
