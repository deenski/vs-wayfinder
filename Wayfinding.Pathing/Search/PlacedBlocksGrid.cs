using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>
/// A grid view with a few hypothetical blocks placed: those cells read as a full <see cref="CellClass.Solid"/>
/// block (<see cref="BlockCell.StepHeight"/> 8). Lets build moves reuse <see cref="Cost.ClimbCostModel.Evaluate"/>
/// unchanged. Reused within one search; not thread-safe and never shared.
/// </summary>
internal sealed class PlacedBlocksGrid : IBlockGrid
{
    private readonly IBlockGrid _inner;
    private readonly GridPos[] _placed;
    private int _count;

    public PlacedBlocksGrid(IBlockGrid inner, int capacity)
    {
        _inner = inner;
        _placed = new GridPos[Math.Max(1, capacity)];
    }

    public int Dimension => _inner.Dimension;

    public int Count => _count;

    public void Clear() => _count = 0;

    public void Add(GridPos pos) => _placed[_count++] = pos;

    public GridPos[] ToArray() => _placed.AsSpan(0, _count).ToArray();

    public CellClass GetClass(int x, int y, int z) => IsPlaced(x, y, z) ? CellClass.Solid : _inner.GetClass(x, y, z);

    public BlockCell GetCell(int x, int y, int z) =>
        IsPlaced(x, y, z) ? new BlockCell(CellClass.Solid, CellFlags.None, 8) : _inner.GetCell(x, y, z);

    private bool IsPlaced(int x, int y, int z)
    {
        for (int i = 0; i < _count; i++)
        {
            GridPos p = _placed[i];
            if (p.X == x && p.Y == y && p.Z == z)
            {
                return true;
            }
        }

        return false;
    }
}
