namespace Wayfinding.Pathing.Grid;

/// <summary>What the fluid layer contained when a cell was read.</summary>
public enum ObservedFluid : byte
{
    /// <summary>No liquid in the fluid layer. Ice and an empty layer are both none.</summary>
    None = 0,

    /// <summary>A non-lava liquid (fresh water, salt water). Boiling water is still this value; the traversal cost marks it as a hazard.</summary>
    Water = 1,

    /// <summary>Lava, still or flowing.</summary>
    Lava = 2,
}

/// <summary>
/// The facts the game adapter reads for one cell, with no game types attached.
/// </summary>
/// <param name="Readable">False when the chunk was unloaded, disposed, not yet received, still packed, or <c>GetBlockOrNull</c> returned null. An unreadable cell stays <see cref="CellClass.Unknown"/> and is never air.</param>
/// <param name="Fluid">Liquid in the fluid layer, if any.</param>
/// <param name="BlockId">Block id of the block being classified. Zero is air. Ignored when <paramref name="Readable"/> is false.</param>
/// <param name="TraversalCost"><c>GetTraversalCost</c> for the Humanoid creature type. A value of <see cref="HazardTraversalCost"/> or more is a hazard, unless the cell is lava.</param>
/// <param name="HasCollision">True when the block has at least one collision box.</param>
public readonly record struct CellSample(
    bool Readable,
    ObservedFluid Fluid,
    int BlockId,
    float TraversalCost,
    bool HasCollision)
{
    /// <summary>
    /// Humanoid traversal cost at or above this value is impassable.
    /// The game documents the same threshold on <c>Block.GetTraversalCost</c>.
    /// </summary>
    public const float HazardTraversalCost = 10000f;
}
