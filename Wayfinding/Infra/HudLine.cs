using System;

namespace Wayfinding.Infra;

/// <summary>
/// The short line under the minimap. Distance text comes from the map layer;
/// this only assembles the label, that distance, and the badges.
/// </summary>
public static class HudLine
{
    public const string ModeBadge = "Local";

    public static bool Show(bool mapAllowed, bool resolved)
    {
        return mapAllowed && resolved;
    }

    public static string Label(NavTargetKind kind, string? name)
    {
        if (kind != NavTargetKind.None && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return "target";
    }

    public static string Status(NavTargetKind kind)
    {
        return kind switch
        {
            NavTargetKind.Waypoint => "waypoint",
            NavTargetKind.Player => "player",
            NavTargetKind.Coordinate => "coordinate",
            _ => "",
        };
    }

    public static string Format(string label, string distance, string mode, string status)
    {
        var text = "→ " + label + " " + distance;
        if (!string.IsNullOrEmpty(mode))
        {
            text += "  " + mode;
        }

        if (!string.IsNullOrEmpty(status))
        {
            text += "  " + status;
        }

        return text;
    }

    /// <summary>
    /// Appends the map label's age for a last-known or stale tether and greys that line.
    /// A live fix is unchanged. A hidden tether never reaches this; the caller has no line.
    /// </summary>
    public static string WithTether(string line, TeammateFixKind kind, TimeSpan age, out bool grey)
    {
        var suffix = TeammateAgeText.For(kind, age);
        grey = suffix != null;
        return suffix == null ? line : line + " " + suffix;
    }
}
