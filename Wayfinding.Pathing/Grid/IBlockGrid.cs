namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Read-only view of block terrain that the planner searches. This is the boundary between game code and the
/// game-free pathing library: implementations are filled from the game on the main thread (KAN-29 /
/// WF-15 <c>SnapshotBuilder</c>) and then read by planners on a background thread.
/// </summary>
/// <remarks>
/// <para><b>Contract every implementation must honour:</b></para>
/// <list type="number">
/// <item><description><b>Read-only and thread-safe to read.</b> Any number of threads may call the members
/// concurrently. Results for a given coordinate never change during the grid's lifetime. Implementations never
/// touch live game objects (chunks, block accessors, the world) from these members.</description></item>
/// <item><description><b>Never throws for a coordinate.</b> Every <see cref="int"/> triple is a valid argument,
/// including <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>. Anything outside known terrain returns
/// <see cref="CellClass.Unknown"/> (and <see cref="BlockCell.Unknown"/>).</description></item>
/// <item><description><b>Unknown, never air.</b> Unloaded or not-yet-copied terrain is
/// <see cref="CellClass.Unknown"/>. Reporting <see cref="CellClass.Air"/> for terrain that was not actually read
/// would let routes walk off into unloaded chunks.</description></item>
/// <item><description><b>Integer block coordinates</b> in world space (<see cref="GridPos"/>), within one
/// <see cref="Dimension"/>. Y is always the in-dimension Y and never includes the game's dimension offset
/// (<c>dim * 32768</c> in <c>BlockPos.InternalY</c>); KAN-29 converts.</description></item>
/// <item><description><see cref="GetClass"/> equals <c>GetCell(x, y, z).Class</c> for every coordinate.</description></item>
/// </list>
/// <para>The concrete implementation is <see cref="BlockGridSnapshot"/>; prefer it over custom implementations.</para>
/// </remarks>
public interface IBlockGrid
{
    /// <summary>
    /// The game dimension the cells belong to (Vintage Story's <c>Dimension</c> id; 0 is the normal world).
    /// Planners refuse to route between positions in different dimensions (design v2 §3, special cases).
    /// </summary>
    int Dimension { get; }

    /// <summary>Returns the movement class of the block at the given world coordinate.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <returns>The class, or <see cref="CellClass.Unknown"/> outside known terrain. Never throws.</returns>
    CellClass GetClass(int x, int y, int z);

    /// <summary>Returns the class, flags, and optional step height of the block at the given world coordinate.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <returns>The cell, or <see cref="BlockCell.Unknown"/> outside known terrain. Never throws.</returns>
    BlockCell GetCell(int x, int y, int z);
}

/// <summary>Convenience overloads for <see cref="IBlockGrid"/>.</summary>
public static class BlockGridExtensions
{
    /// <summary>Returns the class of the block at <paramref name="pos"/>.</summary>
    /// <param name="grid">The grid to read.</param>
    /// <param name="pos">World block position.</param>
    public static CellClass GetClass(this IBlockGrid grid, GridPos pos) => grid.GetClass(pos.X, pos.Y, pos.Z);

    /// <summary>Returns the cell at <paramref name="pos"/>.</summary>
    /// <param name="grid">The grid to read.</param>
    /// <param name="pos">World block position.</param>
    public static BlockCell GetCell(this IBlockGrid grid, GridPos pos) => grid.GetCell(pos.X, pos.Y, pos.Z);
}
