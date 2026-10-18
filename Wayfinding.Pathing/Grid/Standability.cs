namespace Wayfinding.Pathing.Grid;

/// <summary>What a player whose feet are in a given cell would be doing there.</summary>
public enum StandKind : byte
{
    /// <summary>Not a valid position: inside a block, floating in air, under water, or without headroom.</summary>
    None = 0,

    /// <summary>Standing on a <see cref="CellClass.Solid"/> block with passable, dry feet and head cells.</summary>
    Ground = 1,

    /// <summary>Feet in <see cref="CellClass.Water"/> with a non-water head cell: swimming at the surface or wading.</summary>
    Swim = 2,

    /// <summary>The feet, head, or supporting cell is <see cref="CellClass.Lava"/> or <see cref="CellClass.Hazard"/>.</summary>
    Dangerous = 3,

    /// <summary>A cell needed to decide is <see cref="CellClass.Unknown"/> (unloaded or outside the grid).</summary>
    Unknown = 4,
}

/// <summary>
/// The "can a player be here" rule shared by every planner and by debug overlays, so the game side and the
/// library agree on what a standable position is.
/// </summary>
/// <remarks>
/// <para>A position is the cell the player's feet occupy. With the default headroom of 2 cells (the player is
/// 1.85 blocks tall), the rule reads the support cell <c>y - 1</c>, the feet cell <c>y</c> and the head cell
/// <c>y + 1</c>:</para>
/// <list type="number">
/// <item><description>Any of feet, head, or support is Lava/Hazard: <see cref="StandKind.Dangerous"/>.</description></item>
/// <item><description>Feet or head is Solid: <see cref="StandKind.None"/>.</description></item>
/// <item><description>Feet or head is Unknown: <see cref="StandKind.Unknown"/>.</description></item>
/// <item><description>Feet is Water: <see cref="StandKind.Swim"/> if the head cell is not Water, otherwise
/// <see cref="StandKind.None"/> (no underwater routing).</description></item>
/// <item><description>Feet is Air/Walkable: <see cref="StandKind.Ground"/> if support is Solid,
/// <see cref="StandKind.Unknown"/> if support is Unknown, otherwise <see cref="StandKind.None"/>.</description></item>
/// </list>
/// <para>A swim landing stops at the feet cell: the cells below it are not read, so a puddle and deep water
/// classify the same. Water depth for a drop past <c>MaxDrop</c> is the climb cost model's job (KAN-64).
/// An Unknown cell is never treated as air here.</para>
/// </remarks>
public static class Standability
{
    /// <summary>Cells of clearance a player needs: ceil(1.85 blocks) = 2 (feet cell plus head cell).</summary>
    public const int DefaultHeadroomCells = 2;

    /// <summary>Classifies the position whose feet cell is <paramref name="feet"/>. Never throws.</summary>
    /// <param name="grid">The grid to read.</param>
    /// <param name="feet">World position of the feet cell.</param>
    /// <param name="headroomCells">Body height in cells (feet cell included); at least 1.</param>
    public static StandKind Classify(IBlockGrid grid, GridPos feet, int headroomCells = DefaultHeadroomCells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (headroomCells < 1)
        {
            headroomCells = 1;
        }

        CellClass support = grid.GetClass(feet.X, feet.Y - 1, feet.Z);
        CellClass feetCell = grid.GetClass(feet.X, feet.Y, feet.Z);
        if (support.IsDangerous() || feetCell.IsDangerous())
        {
            return StandKind.Dangerous;
        }

        bool anySolid = feetCell == CellClass.Solid;
        bool anyUnknown = feetCell == CellClass.Unknown;
        for (int i = 1; i < headroomCells; i++)
        {
            CellClass c = grid.GetClass(feet.X, feet.Y + i, feet.Z);
            if (c.IsDangerous())
            {
                return StandKind.Dangerous;
            }

            anySolid |= c == CellClass.Solid;
            anyUnknown |= c == CellClass.Unknown;
        }

        if (anySolid)
        {
            return StandKind.None;
        }

        if (anyUnknown)
        {
            return StandKind.Unknown;
        }

        if (feetCell == CellClass.Water)
        {
            CellClass above = headroomCells > 1 ? grid.GetClass(feet.X, feet.Y + 1, feet.Z) : CellClass.Air;
            return above == CellClass.Water ? StandKind.None : StandKind.Swim;
        }

        return support switch
        {
            CellClass.Solid => StandKind.Ground,
            CellClass.Unknown => StandKind.Unknown,
            _ => StandKind.None,
        };
    }

    /// <summary>True when the position is <see cref="StandKind.Ground"/> or <see cref="StandKind.Swim"/>.</summary>
    /// <param name="kind">The classification.</param>
    public static bool IsOccupiable(this StandKind kind) => kind is StandKind.Ground or StandKind.Swim;
}
