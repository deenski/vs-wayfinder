namespace Wayfinding.Pathing.Cost;

/// <summary>
/// Tuning for the headline cost: <c>horizontalDistance + UphillPenalty × gain + DownhillPenalty × loss + terrain</c>
/// (design v2 §3). Defaults match the design's client config (<c>ModConfig/wayfinding.json</c>).
/// </summary>
/// <remarks>All costs are in "blocks of flat walking". Call <see cref="Validate"/> after loading user config.</remarks>
public sealed record ClimbCostOptions
{
    /// <summary>The design defaults.</summary>
    public static ClimbCostOptions Default { get; } = new();

    /// <summary>Extra cost per block of elevation gained. Default 4; 0 gives the pure shortest route. Must be ≥ 0.</summary>
    public double UphillPenalty { get; init; } = 4;

    /// <summary>Extra cost per block of elevation lost. Default 1. Must be ≥ 0.</summary>
    public double DownhillPenalty { get; init; } = 1;

    /// <summary>Largest climb, in blocks, allowed in one step. Default 1. Higher steps are blocked (or, later, build moves).</summary>
    public int MaxClimb { get; init; } = 1;

    /// <summary>Largest drop, in blocks, allowed in one step. Default 3 (fall damage starts above 3.5). Higher drops are blocked.</summary>
    public int MaxDrop { get; init; } = 3;

    /// <summary>
    /// Largest drop, in blocks, allowed when the landing is water at least <see cref="MinWaterDepth"/> deep
    /// (KAN-64). 0, the default, disables the rule: a drop above <see cref="MaxDrop"/> is always blocked,
    /// including into deep water. The rule applies only when this is greater than <see cref="MaxDrop"/>;
    /// drops from <c>MaxDrop + 1</c> through this value may then land in deep water, and anything deeper
    /// stays blocked. Must be between 0 and <see cref="MaxWaterDropLimit"/> inclusive.
    /// </summary>
    public int MaxWaterDrop { get; init; } = 0;

    /// <summary>
    /// Water cells required before a drop above <see cref="MaxDrop"/> is allowed, counting the feet cell and
    /// the contiguous water directly below it. Default 2, so the feet are in water and the cell under them is
    /// water too: a one-block puddle does not qualify. Must be between 1 and <see cref="MaxWaterDropLimit"/>
    /// inclusive. Ignored while <see cref="MaxWaterDrop"/> does not exceed <see cref="MaxDrop"/>.
    /// </summary>
    public int MinWaterDepth { get; init; } = 2;

    /// <summary>Upper bound for <see cref="MaxWaterDrop"/> and <see cref="MinWaterDepth"/>, one world column.</summary>
    public const int MaxWaterDropLimit = 256;

    /// <summary>
    /// Deepest downward step, in blocks, that <see cref="ClimbCostModel.Evaluate"/> will consider.
    /// <see cref="MaxDrop"/>, or <see cref="MaxWaterDrop"/> when that is larger. Fine A* and
    /// <c>ReferenceSearch</c> both generate steps down to this limit; narrowing only one of them would
    /// make the two disagree. While water drops are disabled this equals <see cref="MaxDrop"/>.
    /// </summary>
    public int GeneratedDrop => Math.Max(MaxDrop, MaxWaterDrop);

    /// <summary>
    /// Cost multiplier for horizontal distance covered while swimming or wading (feet in water). Default 5, the
    /// game's own water traversal cost. It multiplies horizontal distance (×5, about +4 per block), never a flat
    /// per-step charge, so diagonal swimming is not cheaper than straight swimming (confirmed in review of PR #2).
    /// Must be ≥ 1 so the horizontal-distance heuristic stays admissible.
    /// </summary>
    public double WaterCost { get; init; } = 5;

    /// <summary>Body height in cells. Default 2 (ceil of the 1.85-block player). Must be ≥ 1.</summary>
    public int HeadroomCells { get; init; } = Grid.Standability.DefaultHeadroomCells;

    /// <summary>Throws if any value is out of range.</summary>
    /// <returns>This instance, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative, NaN, or infinite where that is not allowed.</exception>
    public ClimbCostOptions Validate()
    {
        RequireFinite(UphillPenalty, 0, nameof(UphillPenalty));
        RequireFinite(DownhillPenalty, 0, nameof(DownhillPenalty));
        RequireFinite(WaterCost, 1, nameof(WaterCost));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxClimb, nameof(MaxClimb));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxDrop, nameof(MaxDrop));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxWaterDrop, nameof(MaxWaterDrop));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxWaterDrop, MaxWaterDropLimit, nameof(MaxWaterDrop));
        ArgumentOutOfRangeException.ThrowIfLessThan(MinWaterDepth, 1, nameof(MinWaterDepth));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinWaterDepth, MaxWaterDropLimit, nameof(MinWaterDepth));
        ArgumentOutOfRangeException.ThrowIfLessThan(HeadroomCells, 1, nameof(HeadroomCells));
        return this;
    }

    private static void RequireFinite(double value, double min, string name)
    {
        if (!double.IsFinite(value) || value < min)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number ≥ {min}.");
        }
    }
}
