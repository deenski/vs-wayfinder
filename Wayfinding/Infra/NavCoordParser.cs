using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wayfinding.Infra;

/// <summary>
/// Spawn block X/Z. The map and <c>/waypoint list</c> print X and Z relative to
/// this point. Y is not part of the spawn offset.
/// </summary>
public readonly struct SpawnOrigin
{
    public SpawnOrigin(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }

    public int Z { get; }
}

/// <summary>
/// Absolute world position. Y is null when the player gave X and Z only.
/// </summary>
public readonly struct AbsoluteCoord
{
    public AbsoluteCoord(int x, int? y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public int X { get; }

    public int? Y { get; }

    public int Z { get; }
}

public readonly struct ParsedCoord
{
    public ParsedCoord(int displayX, int? displayY, int displayZ, AbsoluteCoord absolute)
    {
        DisplayX = displayX;
        DisplayY = displayY;
        DisplayZ = displayZ;
        Absolute = absolute;
    }

    public int DisplayX { get; }

    public int? DisplayY { get; }

    public int DisplayZ { get; }

    public AbsoluteCoord Absolute { get; }
}

/// <summary>
/// Parses <c>.nav coord</c> text. X and Z are spawn-relative, the way the map
/// and <c>/waypoint list</c> show them. Y, when present, is absolute height.
/// </summary>
public static class NavCoordParser
{
    public const string Usage =
        "Use .nav coord <x> <z> or .nav coord <x> <y> <z>. X and Z are spawn-relative, the way the map shows them. Y is absolute height.";

    public static bool TryParse(string? text, SpawnOrigin spawn, out ParsedCoord coord, out string error)
    {
        coord = default;
        if (!TryTokens(text, out var tokens, out error))
        {
            return false;
        }

        if (tokens.Count == 1 || tokens.Count > 3)
        {
            error = tokens.Count > 3 ? "Too many coordinates. " + Usage : Usage;
            return false;
        }

        var numbers = new int[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
            {
                error = "\"" + tokens[i] + "\" is not a whole number.";
                return false;
            }
        }

        var displayX = numbers[0];
        int? displayY = tokens.Count == 3 ? numbers[1] : null;
        var displayZ = tokens.Count == 3 ? numbers[2] : numbers[1];

        if (!TryAdd(spawn.X, displayX, out var absoluteX) || !TryAdd(spawn.Z, displayZ, out var absoluteZ))
        {
            error = "That coordinate is outside the world.";
            return false;
        }

        coord = new ParsedCoord(displayX, displayY, displayZ, new AbsoluteCoord(absoluteX, displayY, absoluteZ));
        error = "";
        return true;
    }

    private static bool TryTokens(string? text, out List<string> tokens, out string error)
    {
        tokens = new List<string>();
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = Usage;
            return false;
        }

        var pieces = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var piece in pieces)
        {
            var body = piece;
            if (body.EndsWith(",", StringComparison.Ordinal))
            {
                body = body.Substring(0, body.Length - 1);
            }

            if (body.Length == 0)
            {
                error = "\"" + trimmed + "\" is not a coordinate.";
                return false;
            }

            var bits = body.Split(',');
            for (var i = 0; i < bits.Length; i++)
            {
                var bit = bits[i].Trim();
                if (bit.Length == 0)
                {
                    error = "\"" + trimmed + "\" is not a coordinate.";
                    return false;
                }

                tokens.Add(bit);
            }
        }

        error = "";
        return true;
    }

    private static bool TryAdd(int spawn, int relative, out int absolute)
    {
        var sum = (long)spawn + relative;
        if (sum < int.MinValue || sum > int.MaxValue)
        {
            absolute = 0;
            return false;
        }

        absolute = (int)sum;
        return true;
    }
}
