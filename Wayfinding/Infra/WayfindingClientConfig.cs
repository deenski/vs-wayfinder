using System;
using System.Globalization;

namespace Wayfinding.Infra;

/// <summary>
/// Client <c>ModConfig/wayfinding.json</c>. Public fields are what the game serializer writes.
/// Call <see cref="WayfindingConfig.Normalize"/> before use.
/// </summary>
public sealed class WayfindingClientConfig
{
    public const string ClickCtrlLeft = "CtrlLeft";

    public const string ClickMiddle = "Middle";

    public const string ClickBoth = "Both";

    public int debugVerbosity = 0;

    public string knownColor = WayfindingConfig.DefaultKnownColor;

    public string unknownColor = WayfindingConfig.DefaultUnknownColor;

    public string buildColor = WayfindingConfig.DefaultBuildColor;

    public double knownThickness = WayfindingConfig.DefaultKnownThickness;

    public double unknownThickness = WayfindingConfig.DefaultUnknownThickness;

    public double buildThickness = WayfindingConfig.DefaultBuildThickness;

    public bool showOnMinimap = true;

    public bool showOnWorldMap = true;

    public string clickBinding = ClickCtrlLeft;

    public float waypointClickRadius = WayfindingConfig.DefaultWaypointRadius;

    public float playerClickRadius = WayfindingConfig.DefaultPlayerRadius;
}

public static class WayfindingConfig
{
    public const string DefaultKnownColor = "0.27,0.51,0.71,0.95";

    public const string DefaultUnknownColor = "0.75,0.75,0.75,0.55";

    public const string DefaultBuildColor = "1,0.62,0.12,0.95";

    public const double DefaultKnownThickness = 2.5;

    public const double DefaultUnknownThickness = 2.0;

    public const double DefaultBuildThickness = 4.5;

    public const float DefaultWaypointRadius = 8f;

    public const float DefaultPlayerRadius = 5f;

    public const double MinThickness = 0.5;

    public const double MaxThickness = 16;

    public const float MinRadius = 1f;

    public const float MaxRadius = 64f;

    /// <summary>
    /// Clamps out-of-range numbers and replaces unreadable values with defaults.
    /// Returns true when <paramref name="config"/> was changed.
    /// </summary>
    public static bool Normalize(WayfindingClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var changed = false;
        var verbosity = WayfindingLog.ClampVerbosity(config.debugVerbosity);
        if (verbosity != config.debugVerbosity)
        {
            config.debugVerbosity = verbosity;
            changed = true;
        }

        changed |= Color(ref config.knownColor, DefaultKnownColor);
        changed |= Color(ref config.unknownColor, DefaultUnknownColor);
        changed |= Color(ref config.buildColor, DefaultBuildColor);
        changed |= Thickness(ref config.knownThickness, DefaultKnownThickness);
        changed |= Thickness(ref config.unknownThickness, DefaultUnknownThickness);
        changed |= Thickness(ref config.buildThickness, DefaultBuildThickness);
        changed |= Binding(ref config.clickBinding);
        changed |= Radius(ref config.waypointClickRadius, DefaultWaypointRadius);
        changed |= Radius(ref config.playerClickRadius, DefaultPlayerRadius);
        return changed;
    }

    internal static bool TryColor(string? text, out float r, out float g, out float b, out float a)
    {
        r = g = b = a = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        return TryComponent(parts[0], out r)
            && TryComponent(parts[1], out g)
            && TryComponent(parts[2], out b)
            && TryComponent(parts[3], out a);
    }

    private static bool Color(ref string value, string fallback)
    {
        if (!TryColor(value, out var r, out var g, out var b, out var a))
        {
            value = fallback;
            return true;
        }

        var text = FormatColor(Clamp01(r), Clamp01(g), Clamp01(b), Clamp01(a));
        if (text == value)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool Thickness(ref double value, double fallback)
    {
        if (!double.IsFinite(value))
        {
            value = fallback;
            return true;
        }

        var clamped = Math.Clamp(value, MinThickness, MaxThickness);
        if (clamped == value)
        {
            return false;
        }

        value = clamped;
        return true;
    }

    private static bool Radius(ref float value, float fallback)
    {
        if (!float.IsFinite(value))
        {
            value = fallback;
            return true;
        }

        var clamped = Math.Clamp(value, MinRadius, MaxRadius);
        if (clamped == value)
        {
            return false;
        }

        value = clamped;
        return true;
    }

    private static bool Binding(ref string value)
    {
        if (string.Equals(value, WayfindingClientConfig.ClickCtrlLeft, StringComparison.OrdinalIgnoreCase))
        {
            if (value == WayfindingClientConfig.ClickCtrlLeft)
            {
                return false;
            }

            value = WayfindingClientConfig.ClickCtrlLeft;
            return true;
        }

        if (string.Equals(value, WayfindingClientConfig.ClickMiddle, StringComparison.OrdinalIgnoreCase))
        {
            if (value == WayfindingClientConfig.ClickMiddle)
            {
                return false;
            }

            value = WayfindingClientConfig.ClickMiddle;
            return true;
        }

        if (string.Equals(value, WayfindingClientConfig.ClickBoth, StringComparison.OrdinalIgnoreCase))
        {
            if (value == WayfindingClientConfig.ClickBoth)
            {
                return false;
            }

            value = WayfindingClientConfig.ClickBoth;
            return true;
        }

        value = WayfindingClientConfig.ClickCtrlLeft;
        return true;
    }

    private static bool TryComponent(string text, out float value)
    {
        return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value);
    }

    private static float Clamp01(float value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 1)
        {
            return 1;
        }

        return value;
    }

    private static string FormatColor(float r, float g, float b, float a)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{r:0.##},{g:0.##},{b:0.##},{a:0.##}");
    }
}
