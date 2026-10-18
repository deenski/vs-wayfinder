using System;
using System.Collections.Generic;

namespace Wayfinding.Infra;

public enum NavTargetKind
{
    None = 0,
    Waypoint = 1,
    Player = 2,
    Coordinate = 3,
}

/// <summary>
/// The current navigation target. Waypoint identity is the Guid, not the title.
/// </summary>
public sealed class NavTarget
{
    private NavTarget(NavTargetKind kind, Guid waypointId, string? name, AbsoluteCoord? coordinate, int? dimension)
    {
        Kind = kind;
        WaypointId = waypointId;
        Name = name;
        Coordinate = coordinate;
        Dimension = dimension;
    }

    public static NavTarget None { get; } = new(NavTargetKind.None, Guid.Empty, null, null, null);

    public NavTargetKind Kind { get; }

    public Guid WaypointId { get; }

    public string? Name { get; }

    public AbsoluteCoord? Coordinate { get; }

    /// <summary>
    /// Dimension stamped when a coordinate target is set. Null for every other
    /// kind, and for a coordinate saved before dimension stamping existed.
    /// Waypoint dimension is decoded from the live waypoint Y, not stored here.
    /// </summary>
    public int? Dimension { get; }

    public static NavTarget Waypoint(Guid id, string? name)
    {
        return new NavTarget(NavTargetKind.Waypoint, id, name ?? "", null, null);
    }

    public static NavTarget Player(string name)
    {
        return new NavTarget(NavTargetKind.Player, Guid.Empty, name, null, null);
    }

    public static NavTarget At(AbsoluteCoord coordinate, int? dimension = null)
    {
        return new NavTarget(NavTargetKind.Coordinate, Guid.Empty, null, coordinate, dimension);
    }
}

/// <summary>
/// Per-world navigation target file. Public fields are what the game config
/// serializer writes. A world with no record has no target.
/// </summary>
public sealed class NavTargetDocument
{
    public List<NavTargetRecord> Worlds = new();

    public NavTarget Read(string worldId)
    {
        if (string.IsNullOrEmpty(worldId) || Worlds == null)
        {
            return NavTarget.None;
        }

        for (var i = 0; i < Worlds.Count; i++)
        {
            var record = Worlds[i];
            if (record != null && string.Equals(record.WorldId, worldId, StringComparison.Ordinal))
            {
                return record.ToTarget();
            }
        }

        return NavTarget.None;
    }

    public void Write(string worldId, NavTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Worlds ??= new List<NavTargetRecord>();
        Worlds.RemoveAll(record => record == null || string.Equals(record.WorldId, worldId, StringComparison.Ordinal));
        if (target.Kind == NavTargetKind.None)
        {
            return;
        }

        Worlds.Add(NavTargetRecord.From(worldId, target));
    }

    public NavTargetDocument Clone()
    {
        var copy = new NavTargetDocument();
        if (Worlds == null)
        {
            return copy;
        }

        for (var i = 0; i < Worlds.Count; i++)
        {
            if (Worlds[i] != null)
            {
                copy.Worlds.Add(Worlds[i].Copy());
            }
        }

        return copy;
    }
}

public sealed class NavTargetRecord
{
    public string WorldId = "";

    public string Kind = "";

    public string WaypointId = "";

    public string WaypointName = "";

    public string PlayerName = "";

    public int X;

    public int Z;

    public bool HasY;

    public int Y;

    /// <summary>
    /// False on records written before coordinate dimension stamping. A missing
    /// field loads as false, which is not dimension 0.
    /// </summary>
    public bool HasDimension;

    public int Dimension;

    public static NavTargetRecord From(string worldId, NavTarget target)
    {
        var record = new NavTargetRecord { WorldId = worldId };
        switch (target.Kind)
        {
            case NavTargetKind.Waypoint:
                record.Kind = "waypoint";
                record.WaypointId = target.WaypointId.ToString("D");
                record.WaypointName = target.Name ?? "";
                return record;
            case NavTargetKind.Player:
                record.Kind = "player";
                record.PlayerName = target.Name ?? "";
                return record;
            case NavTargetKind.Coordinate:
                var coord = target.Coordinate
                    ?? throw new ArgumentException("A coordinate target needs a position.", nameof(target));
                record.Kind = "coordinate";
                record.X = coord.X;
                record.Z = coord.Z;
                record.HasY = coord.Y.HasValue;
                record.Y = coord.Y ?? 0;
                record.HasDimension = target.Dimension.HasValue;
                record.Dimension = target.Dimension ?? 0;
                return record;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "Cannot store an empty target.");
        }
    }

    public NavTarget ToTarget()
    {
        if (string.Equals(Kind, "waypoint", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(WaypointId, out var id) || id == Guid.Empty)
            {
                return NavTarget.None;
            }

            return NavTarget.Waypoint(id, WaypointName ?? "");
        }

        if (string.Equals(Kind, "player", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(PlayerName))
            {
                return NavTarget.None;
            }

            return NavTarget.Player(PlayerName);
        }

        if (string.Equals(Kind, "coordinate", StringComparison.Ordinal))
        {
            return NavTarget.At(new AbsoluteCoord(X, HasY ? Y : null, Z), HasDimension ? Dimension : null);
        }

        return NavTarget.None;
    }

    public NavTargetRecord Copy()
    {
        return new NavTargetRecord
        {
            WorldId = WorldId,
            Kind = Kind,
            WaypointId = WaypointId,
            WaypointName = WaypointName,
            PlayerName = PlayerName,
            X = X,
            Z = Z,
            HasY = HasY,
            Y = Y,
            HasDimension = HasDimension,
            Dimension = Dimension,
        };
    }
}

public interface INavTargetStore
{
    NavTargetDocument Load();

    void Save(NavTargetDocument document);
}
