namespace Wayfinding.Pathing.Grid;

/// <summary>
/// The movement class of one block cell, as seen by the planner. This is the only per-block information the
/// cost model needs; richer data goes in <see cref="CellFlags"/> or future fields of <see cref="BlockCell"/>.
/// </summary>
/// <remarks>
/// <para>
/// The numeric value <see cref="Unknown"/> is <c>0</c> on purpose: a freshly allocated cell array, a
/// <c>default(CellClass)</c>, or a cell a builder never wrote all read as <see cref="Unknown"/>, never as
/// <see cref="Air"/>. Unloaded terrain must never look walkable (design v2 §1: <c>GetBlock</c> returns air for
/// unloaded chunks, so the game side reads through <c>GetBlockOrNull</c> plus a loaded-chunk check).
/// </para>
/// <para>
/// Mapping for KAN-29 / WF-15 (SnapshotBuilder), agreed on PR #2. Classify on the main thread from the live
/// accessor, in this order: the fluid layer first (water is <see cref="Water"/>, lava is <see cref="Lava"/>);
/// then <c>GetTraversalCost(pos, Humanoid)</c> of 10000 or more is <see cref="Hazard"/>; then a block with no
/// collision boxes is <see cref="Air"/> for block id 0 and <see cref="Walkable"/> otherwise; any collision box
/// at all is <see cref="Solid"/>; an unloaded or null block stays <see cref="Unknown"/>.
/// </para>
/// <list type="table">
/// <item><term><see cref="Solid"/></term><description>Has a collision box tall enough to stand on and blocks
/// the body: stone, soil, logs, planks, glass, closed doors, fences, and leaves when they have a collision box.
/// Thin blocks with a small collision box (snow layers, slabs, stairs) are also <see cref="Solid"/>.
/// <see cref="BlockCell.StepHeight"/> records the max collision-box Y2 in eighths, but
/// <see cref="Standability"/> and the climb model ignore it until KAN-26, so a slab on top of a block still
/// reads as a 2-block climb and is blocked.</description></item>
/// <item><term><see cref="Walkable"/></term><description>Occupied by a block, but nothing in it blocks you:
/// you stand <i>in</i> it, not <i>on</i> it. It has no collision box: tall grass, flowers, crops, saplings,
/// torches, open doors, and ladders (no climbing routes in v1). Moves through it cost the same as through air,
/// and it never supports a player.</description></item>
/// <item><term><see cref="Water"/></term><description>Any non-harmful water (still or flowing, fresh or salt).
/// Costs extra to swim through.</description></item>
/// <item><term><see cref="Lava"/></term><description>Lava, still or flowing. Never entered.</description></item>
/// <item><term><see cref="Hazard"/></term><description>Anything else a route must never enter: boiling water,
/// burning fire pits and fire, damaging blocks. Corresponds to the game's very large traversal costs.</description></item>
/// <item><term><see cref="Air"/></term><description>Empty, loaded cell.</description></item>
/// <item><term><see cref="Unknown"/></term><description>Not loaded, not yet copied, or outside the grid.</description></item>
/// </list>
/// </remarks>
public enum CellClass : byte
{
    /// <summary>Not known: unloaded, unrevealed, not yet copied, or outside the grid. The default value (0).</summary>
    Unknown = 0,

    /// <summary>Empty, loaded cell. Passable, does not support a player standing above it.</summary>
    Air = 1,

    /// <summary>Blocks the body and supports a player standing on top of it.</summary>
    Solid = 2,

    /// <summary>
    /// A block you stand <i>in</i>, not <i>on</i>: occupied, but without a collision box, so nothing in it blocks
    /// you (grass, flowers, open doors, ladders). Moves like air and does not support a player.
    /// </summary>
    Walkable = 3,

    /// <summary>Swimmable water. Passable at an extra cost; a player whose feet are in it is swimming or wading.</summary>
    Water = 4,

    /// <summary>Lava. Always blocked.</summary>
    Lava = 5,

    /// <summary>Other harmful cell (boiling water, fire, burning fire pit...). Always blocked.</summary>
    Hazard = 6,
}
