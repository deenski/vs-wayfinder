namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Maps one <see cref="CellSample"/> to a <see cref="BlockCell"/> using the KAN-29 order.
/// </summary>
/// <remarks>
/// Order, from the <see cref="CellClass"/> contract:
/// a liquid in the fluid layer is <see cref="CellClass.Water"/> or <see cref="CellClass.Lava"/>
/// (boiling water is a liquid whose Humanoid traversal cost is 99999, so it becomes
/// <see cref="CellClass.Hazard"/>);
/// otherwise a Humanoid traversal cost of <see cref="CellSample.HazardTraversalCost"/> or more is
/// <see cref="CellClass.Hazard"/>;
/// otherwise a block with no collision boxes is <see cref="CellClass.Air"/> for block id 0 and
/// <see cref="CellClass.Walkable"/> otherwise;
/// otherwise any collision box is <see cref="CellClass.Solid"/>.
/// An unreadable sample is not classified: the caller leaves the cell <see cref="CellClass.Unknown"/>.
/// </remarks>
public static class CellClassifier
{
    /// <summary>
    /// Classifies a cell that was actually read.
    /// </summary>
    /// <param name="sample">Facts from the game, or from a test.</param>
    /// <param name="cell">The classified cell when this returns true. <see cref="BlockCell.Unknown"/> when it returns false.</param>
    /// <returns>False when <see cref="CellSample.Readable"/> is false. The caller must not write the cell, so it stays unknown rather than air.</returns>
    public static bool TryClassify(in CellSample sample, out BlockCell cell)
    {
        if (!sample.Readable)
        {
            cell = BlockCell.Unknown;
            return false;
        }

        if (sample.Fluid == ObservedFluid.Lava)
        {
            cell = new BlockCell(CellClass.Lava);
            return true;
        }

        if (sample.Fluid == ObservedFluid.Water)
        {
            cell = sample.TraversalCost >= CellSample.HazardTraversalCost
                ? new BlockCell(CellClass.Hazard)
                : new BlockCell(CellClass.Water);
            return true;
        }

        if (sample.TraversalCost >= CellSample.HazardTraversalCost)
        {
            cell = new BlockCell(CellClass.Hazard);
            return true;
        }

        if (!sample.HasCollision)
        {
            cell = new BlockCell(sample.BlockId == 0 ? CellClass.Air : CellClass.Walkable);
            return true;
        }

        cell = new BlockCell(CellClass.Solid);
        return true;
    }
}
