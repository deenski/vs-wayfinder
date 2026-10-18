using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Cost;

/// <summary>
/// The headline cost model (design v2 §3): for a step from <c>a</c> to <c>b</c>,
/// <c>cost = horizontalDistance(a, b) + UphillPenalty × max(0, b.Y − a.Y) + DownhillPenalty × max(0, a.Y − b.Y) + terrainCost(b)</c>,
/// with hard limits <see cref="ClimbCostOptions.MaxClimb"/> / <see cref="ClimbCostOptions.MaxDrop"/>, water at
/// <see cref="ClimbCostOptions.WaterCost"/> × distance, and lava / hazards blocked.
/// </summary>
/// <remarks>
/// Immutable and thread-safe. Every cost is at least the horizontal distance, so plain horizontal distance
/// (<see cref="Heuristic"/>) is an admissible A* heuristic.
/// </remarks>
public sealed class ClimbCostModel
{
    /// <summary>Creates a model; the options are validated.</summary>
    /// <param name="options">Tuning; <see cref="ClimbCostOptions.Default"/> if null.</param>
    public ClimbCostModel(ClimbCostOptions? options = null)
    {
        Options = (options ?? ClimbCostOptions.Default).Validate();
    }

    /// <summary>The validated options.</summary>
    public ClimbCostOptions Options { get; }

    /// <summary>
    /// The cost formula alone, with no grid checks: for coarse planners (WF-13) that work on heights, not blocks.
    /// </summary>
    /// <param name="horizontalDistance">Horizontal distance of the step, in blocks.</param>
    /// <param name="deltaY">Destination Y minus source Y.</param>
    /// <param name="swim">True if the step is covered swimming.</param>
    /// <returns>The cost, or +∞ if the step breaks <see cref="ClimbCostOptions.MaxClimb"/> / <see cref="ClimbCostOptions.MaxDrop"/>.</returns>
    public double Formula(double horizontalDistance, int deltaY, bool swim = false)
    {
        if (deltaY > Options.MaxClimb || -(long)deltaY > Options.MaxDrop)
        {
            return double.PositiveInfinity;
        }

        double cost = horizontalDistance * (swim ? Options.WaterCost : 1.0);
        cost += deltaY > 0 ? Options.UphillPenalty * deltaY : Options.DownhillPenalty * -(double)deltaY;
        return cost;
    }

    /// <summary>Admissible lower bound on the cost between two positions: their horizontal distance.</summary>
    /// <param name="from">Start position.</param>
    /// <param name="to">Goal position.</param>
    public double Heuristic(GridPos from, GridPos to) => GridPos.HorizontalDistance(from, to);

    /// <summary>
    /// Evaluates one block-level step to an 8-neighbour column, from the occupiable position <paramref name="from"/>
    /// to the feet position <paramref name="to"/> (any Y). Never throws for grid contents, including Unknown.
    /// </summary>
    /// <remarks>
    /// Checks, in order: the climb/drop limits; that <paramref name="to"/> is occupiable (<see cref="Standability"/>);
    /// that the body has room for the step (the source column must be clear up to the higher of the two levels
    /// plus headroom, so you can jump up; the destination column likewise, so you can fall into it; and on a
    /// diagonal both corner columns, so you cannot cut a corner through a block or lava). Lava and hazards
    /// anywhere on that path block the move; any Unknown cell makes it <see cref="MoveVerdict.Unknown"/>;
    /// a known blocker always wins over Unknown.
    /// </remarks>
    /// <param name="grid">The terrain.</param>
    /// <param name="from">Current feet position; assumed occupiable (the search only stands on such cells).</param>
    /// <param name="to">Destination feet position.</param>
    /// <returns>The verdict and cost.</returns>
    /// <exception cref="ArgumentException"><paramref name="to"/> is not in one of the 8 neighbouring columns.</exception>
    public MoveCost Evaluate(IBlockGrid grid, GridPos from, GridPos to)
    {
        ArgumentNullException.ThrowIfNull(grid);
        long dxl = (long)to.X - from.X;
        long dzl = (long)to.Z - from.Z;
        if (dxl is < -1 or > 1 || dzl is < -1 or > 1 || (dxl == 0 && dzl == 0))
        {
            throw new ArgumentException("Evaluate handles single steps to one of the 8 neighbouring columns; use Formula for longer moves.", nameof(to));
        }

        long dyl = (long)to.Y - from.Y;
        if (dyl > Options.MaxClimb)
        {
            return MoveCost.Blocked(BlockReason.TooHigh);
        }

        if (-dyl > Options.MaxDrop)
        {
            return MoveCost.Blocked(BlockReason.TooDeep);
        }

        int dy = (int)dyl;
        StandKind dest = Standability.Classify(grid, to, Options.HeadroomCells);
        switch (dest)
        {
            case StandKind.Dangerous:
                return MoveCost.Blocked(DangerAt(grid, to, Options.HeadroomCells));
            case StandKind.None:
                return MoveCost.Blocked(BlockReason.NotStandable);
        }

        // Body sweep: [low, top] per column. 'top' is the highest cell the head passes through.
        long top = (long)Math.Max(from.Y, to.Y) + Options.HeadroomCells - 1;
        var sweep = new SweepResult();
        Sweep(grid, from.X, from.Z, from.Y, top, ref sweep);
        Sweep(grid, to.X, to.Z, to.Y, top, ref sweep);
        if (dxl != 0 && dzl != 0)
        {
            int low = Math.Max(from.Y, to.Y);
            Sweep(grid, to.X, from.Z, low, top, ref sweep);
            Sweep(grid, from.X, to.Z, low, top, ref sweep);
        }

        if (sweep.Lava)
        {
            return MoveCost.Blocked(BlockReason.Lava);
        }

        if (sweep.Hazard)
        {
            return MoveCost.Blocked(BlockReason.Hazard);
        }

        if (sweep.Solid)
        {
            return MoveCost.Blocked(BlockReason.Obstructed);
        }

        if (sweep.Unknown || dest == StandKind.Unknown)
        {
            return MoveCost.Unknown;
        }

        bool swim = dest == StandKind.Swim;
        double cost = Formula(GridPos.HorizontalDistance(from, to), dy, swim);
        return new MoveCost(MoveVerdict.Ok, cost, BlockReason.None, Math.Max(0, dy), Math.Max(0, -dy), swim);
    }

    private static BlockReason DangerAt(IBlockGrid grid, GridPos feet, int headroomCells)
    {
        for (long y = (long)feet.Y - 1; y <= (long)feet.Y + headroomCells - 1; y++)
        {
            if (y >= int.MinValue && y <= int.MaxValue && grid.GetClass(feet.X, (int)y, feet.Z) == CellClass.Lava)
            {
                return BlockReason.Lava;
            }
        }

        return BlockReason.Hazard;
    }

    private static void Sweep(IBlockGrid grid, int x, int z, int low, long top, ref SweepResult r)
    {
        if (top > int.MaxValue)
        {
            r.Unknown = true; // cells above the top of the coordinate space are never known
            top = int.MaxValue;
        }

        for (long y = low; y <= top; y++)
        {
            switch (grid.GetClass(x, (int)y, z))
            {
                case CellClass.Solid: r.Solid = true; break;
                case CellClass.Lava: r.Lava = true; break;
                case CellClass.Hazard: r.Hazard = true; break;
                case CellClass.Unknown: r.Unknown = true; break;
            }
        }
    }

    private struct SweepResult
    {
        public bool Solid;
        public bool Lava;
        public bool Hazard;
        public bool Unknown;
    }
}
