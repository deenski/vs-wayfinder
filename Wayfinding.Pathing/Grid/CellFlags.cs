namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Extra per-cell bits that sit beside the <see cref="CellClass"/>. The cost model in this ticket ignores them;
/// they exist so later tickets can add data without changing <see cref="IBlockGrid"/>.
/// </summary>
/// <remarks>
/// Adding a flag is a non-breaking change. Implementations that do not know about a flag simply never set it,
/// and consumers must treat an unset flag as "no information" rather than "allowed by the server".
/// </remarks>
[Flags]
public enum CellFlags : ushort
{
    /// <summary>No extra information.</summary>
    None = 0,

    /// <summary>
    /// Placing a block here is not allowed or not suggested, for example inside another player's land claim.
    /// Used by WF-14 build moves; walking through the cell is unaffected.
    /// </summary>
    BuildForbidden = 1 << 0,
}
