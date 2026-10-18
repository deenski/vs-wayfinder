using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wayfinding.Infra;

/// <summary>
/// One entry from the player's waypoint list, in <c>/waypoint list</c> order.
/// </summary>
public readonly struct WaypointRef
{
    private WaypointRef(Guid id, string name, bool hasId)
    {
        Id = id;
        Name = name;
        HasId = hasId;
    }

    public WaypointRef(Guid id, string? name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A waypoint id is required.", nameof(id));
        }

        Id = id;
        Name = (name ?? "").Trim();
        HasId = true;
    }

    public static WaypointRef Missing(string? name)
    {
        return new WaypointRef(Guid.Empty, (name ?? "").Trim(), false);
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool HasId { get; }
}

public readonly struct WaypointSelection
{
    private WaypointSelection(bool ok, WaypointRef waypoint, string message)
    {
        Ok = ok;
        Waypoint = waypoint;
        Message = message;
    }

    public bool Ok { get; }

    public WaypointRef Waypoint { get; }

    public string Message { get; }

    public static WaypointSelection Found(WaypointRef waypoint)
    {
        return new WaypointSelection(true, waypoint, "");
    }

    public static WaypointSelection Fail(string message)
    {
        return new WaypointSelection(false, default, message);
    }
}

/// <summary>
/// Resolves a waypoint name or a <c>/waypoint list</c> index (0-based) to a Guid.
/// <c>#0</c> is always the first waypoint. A bare number is that index unless a
/// waypoint title is the same text, in which case the player has to disambiguate.
/// </summary>
public static class WaypointSelector
{
    public const string Usage =
        "Use .nav waypoint <name> or .nav waypoint <index>. Indexes match /waypoint list and start at 0. #0 is the first waypoint.";

    public static WaypointSelection Select(string? argument, IReadOnlyList<WaypointRef> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        var text = (argument ?? "").Trim();
        if (text.Length == 0)
        {
            return WaypointSelection.Fail(Usage);
        }

        if (TryQuoted(text, out var quoted))
        {
            return SelectName(quoted, waypoints);
        }

        if (text[0] == '#')
        {
            return SelectIndex(text, text.Substring(1), waypoints);
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            var names = FindNames(text, waypoints);
            var inRange = index >= 0 && index < waypoints.Count;
            if (names.Count == 1 && !inRange)
            {
                return Finish(names[0].Waypoint);
            }

            if (names.Count > 1 && !inRange)
            {
                return WaypointSelection.Fail(AmbiguousNames(text, names));
            }

            if (names.Count > 0 && inRange)
            {
                return WaypointSelection.Fail(
                    "\"" + text + "\" is both waypoint index " + index.ToString(CultureInfo.InvariantCulture)
                    + " and a waypoint name. Use #" + index.ToString(CultureInfo.InvariantCulture)
                    + " for the index, or \".nav waypoint \\\"" + text + "\\\"\" for the name.");
            }

            return SelectIndex(text, text, waypoints);
        }

        return SelectName(text, waypoints);
    }

    private static WaypointSelection SelectIndex(string original, string body, IReadOnlyList<WaypointRef> waypoints)
    {
        if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return WaypointSelection.Fail("\"" + original + "\" is not a waypoint index.");
        }

        if (index < 0)
        {
            return WaypointSelection.Fail("Waypoint indexes start at 0.");
        }

        if (waypoints.Count == 0)
        {
            return WaypointSelection.Fail("You have no waypoints.");
        }

        if (index >= waypoints.Count)
        {
            var last = (waypoints.Count - 1).ToString(CultureInfo.InvariantCulture);
            return WaypointSelection.Fail(
                "There is no waypoint " + index.ToString(CultureInfo.InvariantCulture)
                + ". Valid indexes are 0.." + last + ".");
        }

        return Finish(waypoints[index]);
    }

    private static WaypointSelection SelectName(string name, IReadOnlyList<WaypointRef> waypoints)
    {
        if (name.Trim().Length == 0)
        {
            return WaypointSelection.Fail(Usage);
        }

        if (waypoints.Count == 0)
        {
            return WaypointSelection.Fail("You have no waypoints.");
        }

        var names = FindNames(name, waypoints);
        if (names.Count == 0)
        {
            return WaypointSelection.Fail("No waypoint named \"" + name.Trim() + "\".");
        }

        if (names.Count > 1)
        {
            return WaypointSelection.Fail(AmbiguousNames(name.Trim(), names));
        }

        return Finish(names[0].Waypoint);
    }

    private static WaypointSelection Finish(WaypointRef waypoint)
    {
        if (!waypoint.HasId)
        {
            if (waypoint.Name.Length == 0)
            {
                return WaypointSelection.Fail("That waypoint has no id, so it can't be tracked.");
            }

            return WaypointSelection.Fail("Waypoint \"" + waypoint.Name + "\" has no id, so it can't be tracked.");
        }

        return WaypointSelection.Found(waypoint);
    }

    private static List<NameHit> FindNames(string name, IReadOnlyList<WaypointRef> waypoints)
    {
        var trimmed = name.Trim();
        var hits = new List<NameHit>();
        for (var i = 0; i < waypoints.Count; i++)
        {
            if (string.Equals(waypoints[i].Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new NameHit(i, waypoints[i]));
            }
        }

        return hits;
    }

    private static string AmbiguousNames(string name, List<NameHit> hits)
    {
        var list = "";
        for (var i = 0; i < hits.Count; i++)
        {
            if (i > 0)
            {
                list += i == hits.Count - 1 ? " or " : ", ";
            }

            list += "#" + hits[i].Index.ToString(CultureInfo.InvariantCulture);
        }

        return "More than one waypoint is named \"" + name + "\". Use an index (" + list + ").";
    }

    private static bool TryQuoted(string text, out string name)
    {
        if (text.Length >= 2)
        {
            var quote = text[0];
            if ((quote == '"' || quote == '\'') && text[text.Length - 1] == quote)
            {
                name = text.Substring(1, text.Length - 2);
                return true;
            }
        }

        name = "";
        return false;
    }

    private readonly struct NameHit
    {
        public NameHit(int index, WaypointRef waypoint)
        {
            Index = index;
            Waypoint = waypoint;
        }

        public int Index { get; }

        public WaypointRef Waypoint { get; }
    }
}
