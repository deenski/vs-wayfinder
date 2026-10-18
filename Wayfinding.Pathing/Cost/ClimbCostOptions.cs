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
