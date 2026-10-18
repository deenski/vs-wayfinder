namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Everything a grid knows about one block cell: its <see cref="CellClass"/>, optional <see cref="CellFlags"/>,
/// and an optional <see cref="StepHeight"/>.
/// </summary>
/// <remarks>
/// New fields are added with defaults that mean "no information", so existing <see cref="IBlockGrid"/>
/// implementations keep working. <c>default(BlockCell)</c> is an <see cref="CellClass.Unknown"/> cell with no
/// flags and no measured step height.
/// <see cref="StepHeight"/> is recorded here. <see cref="Standability"/> and the climb model do not read it yet
/// (that fence and slab step is a follow-up), so a slab stays <see cref="CellClass.Solid"/>.
/// </remarks>
/// <param name="Class">The movement class of the cell.</param>
/// <param name="Flags">Extra bits; <see cref="CellFlags.None"/> when nothing else is known.</param>
/// <param name="StepHeight">
/// Maximum collision-box Y2, quantized to eighths of a block. A full block is 8. Null means the height was not
/// measured, which is what <see cref="Unknown"/> and every cell classified without a collision box stay.
/// </param>
public readonly record struct BlockCell(CellClass Class, CellFlags Flags = CellFlags.None, byte? StepHeight = null)
{
    /// <summary>An unknown cell with no flags and no measured step height; what grids return outside known terrain.</summary>
    public static BlockCell Unknown => default;
}
