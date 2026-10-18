namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Everything a grid knows about one block cell: its <see cref="CellClass"/> plus optional <see cref="CellFlags"/>.
/// </summary>
/// <remarks>
/// This struct is the extension point for per-cell data (for example a fractional step height later). New fields
/// are added with defaults that mean "no information", so existing <see cref="IBlockGrid"/> implementations keep
/// working. <c>default(BlockCell)</c> is an <see cref="CellClass.Unknown"/> cell with no flags.
/// </remarks>
/// <param name="Class">The movement class of the cell.</param>
/// <param name="Flags">Extra bits; <see cref="CellFlags.None"/> when nothing else is known.</param>
public readonly record struct BlockCell(CellClass Class, CellFlags Flags = CellFlags.None)
{
    /// <summary>An unknown cell with no flags; what grids return outside known terrain.</summary>
    public static BlockCell Unknown => default;
}
