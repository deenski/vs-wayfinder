namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Maps one <see cref="CellSample"/> to a <see cref="BlockCell"/> using the KAN-29 order.
/// </summary>
/// <remarks>
/// Order, from the <see cref="CellClass"/> contract:
/// a collision box that also holds water or another fluid is <see cref="CellClass.Solid"/>
/// (fluid wins only when the cell has no collision box);
/// otherwise a liquid is <see cref="CellClass.Water"/> or <see cref="CellClass.Lava"/>
/// (boiling water is a liquid whose Humanoid traversal cost is 99999, so it becomes
/// <see cref="CellClass.Hazard"/>);
/// otherwise a Humanoid traversal cost of <see cref="CellSample.HazardTraversalCost"/> or more is
/// <see cref="CellClass.Hazard"/>;
/// otherwise a block with no collision boxes is <see cref="CellClass.Air"/> for block id 0 and
/// <see cref="CellClass.Walkable"/> otherwise;
/// otherwise any collision box is <see cref="CellClass.Solid"/>.
/// An unreadable sample is not classified: the caller leaves the cell <see cref="CellClass.Unknown"/>.
/// When the sample has a collision box and a measured finite max Y2, <see cref="BlockCell.StepHeight"/> is that Y2
/// in eighths of a block. NaN is left unmeasured. The class does not change, so a slab stays <see cref="CellClass.Solid"/>.
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

        // A fence, slab, or stairs block sitting in water is a blocker, not a swim.
        if (sample.HasCollision && sample.Fluid != ObservedFluid.None)
        {
            cell = Classified(CellClass.Solid, sample);
            return true;
        }

        if (sample.Fluid == ObservedFluid.Lava)
        {
            cell = Classified(CellClass.Lava, sample);
            return true;
        }

        if (sample.Fluid == ObservedFluid.Water)
        {
            cell = Classified(
                sample.TraversalCost >= CellSample.HazardTraversalCost ? CellClass.Hazard : CellClass.Water,
                sample);
            return true;
        }

        if (sample.TraversalCost >= CellSample.HazardTraversalCost)
        {
            cell = Classified(CellClass.Hazard, sample);
            return true;
        }

        if (!sample.HasCollision)
        {
            cell = Classified(sample.BlockId == 0 ? CellClass.Air : CellClass.Walkable, sample);
            return true;
        }

        cell = Classified(CellClass.Solid, sample);
        return true;
    }

    /// <summary>
    /// Quantizes a collision box's maximum Y2 to eighths of a block.
    /// </summary>
    /// <param name="maxCollisionY2">The largest collision-box Y2, in blocks. A full block is 1.</param>
    /// <returns>The height in eighths, clamped to a byte. 1 becomes 8.</returns>
    public static byte QuantizeStepHeight(float maxCollisionY2)
    {
        double eighths = Math.Round(maxCollisionY2 * 8.0, MidpointRounding.AwayFromZero);
        if (eighths <= 0)
        {
            return 0;
        }

        if (eighths >= byte.MaxValue)
        {
            return byte.MaxValue;
        }

        return (byte)eighths;
    }

    private static BlockCell Classified(CellClass cls, in CellSample sample)
    {
        byte? step = null;
        if (sample.HasCollision && sample.MaxCollisionY2 is float y2 && !float.IsNaN(y2))
        {
            step = QuantizeStepHeight(y2);
        }

        return new BlockCell(cls, CellFlags.None, step);
    }
}
