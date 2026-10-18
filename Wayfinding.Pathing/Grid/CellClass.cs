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
/// Mapping guidance for KAN-29 / WF-15 (SnapshotBuilder). Classify by what the block does to a walking humanoid:
/// </para>
/// <list type="table">
/// <item><term><see cref="Solid"/></term><description>Has a collision box tall enough to stand on and blocks
/// the body: stone, soil, logs, planks, glass, closed doors, fences, and leaves when they have a collision box.
/// Slabs and stairs are <see cref="Solid"/> for now (see the open question on step heights in the PR).</description></item>
/// <item><term><see cref="Walkable"/></term><description>Not empty but passable and not supporting: tall grass,
/// flowers, crops, saplings, snow layers, carpets, torches, open doors, ladders. Moves through it cost the same
/// as through air.</description></item>
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

    /// <summary>Passable non-empty block (plants, snow layer, open door...). Moves like air; does not support.</summary>
    Walkable = 3,

    /// <summary>Swimmable water. Passable at an extra cost; a player whose feet are in it is swimming or wading.</summary>
    Water = 4,

    /// <summary>Lava. Always blocked.</summary>
    Lava = 5,

    /// <summary>Other harmful cell (boiling water, fire, burning fire pit...). Always blocked.</summary>
    Hazard = 6,
}
