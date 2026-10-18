namespace Wayfinding.Pathing.Grid;

/// <summary>Movement properties of each <see cref="CellClass"/>, shared by every planner.</summary>
public static class CellClassExtensions
{
    /// <summary>
    /// True when a player's body can occupy the cell: <see cref="CellClass.Air"/>, <see cref="CellClass.Walkable"/>
    /// or <see cref="CellClass.Water"/>. <see cref="CellClass.Unknown"/> is not passable (it is not known to be).
    /// </summary>
    /// <param name="cell">The class to test.</param>
    public static bool IsPassable(this CellClass cell) =>
        cell is CellClass.Air or CellClass.Walkable or CellClass.Water;

    /// <summary>True when a player can stand on top of the cell. Only <see cref="CellClass.Solid"/> supports.</summary>
    /// <param name="cell">The class to test.</param>
    public static bool IsSupporting(this CellClass cell) => cell == CellClass.Solid;

    /// <summary>True for cells a route must never enter: <see cref="CellClass.Lava"/> and <see cref="CellClass.Hazard"/>.</summary>
    /// <param name="cell">The class to test.</param>
    public static bool IsDangerous(this CellClass cell) => cell is CellClass.Lava or CellClass.Hazard;
}
