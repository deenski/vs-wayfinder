using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Coarse;

/// <summary>Identifies one coarse node: component <see cref="Index"/> of the 8×8 cell (<see cref="Cx"/>, <see cref="Cz"/>).</summary>
/// <param name="Cx">Cell X (block X >> 3).</param>
/// <param name="Cz">Cell Z (block Z >> 3).</param>
/// <param name="Index">Component index within the cell.</param>
public readonly record struct CoarseNodeId(int Cx, int Cz, int Index);

/// <summary>
/// The 8×8-cell abstraction (design v2 §3), built lazily per cell from an <see cref="IHeightGrid"/>. Each cell
/// is split into components: maximal sets of passable columns connected by 4-neighbour steps of at most
/// MaxClimb up or down (so a component can be walked within, both ways). Components are the coarse nodes. A directed
/// edge C → D exists when some column of C has a 4-neighbour in D that a player can step to (climb ≤ MaxClimb,
/// drop ≤ MaxDrop). Unknown columns join no component; a component next to one is a <i>frontier</i>.
/// </summary>
/// <remarks>Single-threaded: one graph per planning call. Cells are aligned to world coordinates (4 per chunk).</remarks>
internal sealed class CoarseGraph
{
    public const int CellSize = 8;
    private const int Shift = 3;
    private const int Columns = CellSize * CellSize;

    private static readonly (int Dx, int Dz)[] Neighbours4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly IHeightGrid _heights;
    private readonly ClimbCostOptions _o;
    private readonly Dictionary<(int, int), Cell> _cells = new();

    public CoarseGraph(IHeightGrid heights, ClimbCostOptions options)
    {
        _heights = heights;
        _o = options;
    }

    public int CellsBuilt => _cells.Count;

    public sealed class Component
    {
        public required CoarseNodeId Id { get; init; }

        public required GridPos Rep { get; init; }

        public required double WaterFraction { get; init; }

        public required int ColumnCount { get; init; }

        public bool Frontier { get; set; }

        public List<CoarseNodeId>? Edges { get; set; }
    }

    private sealed class Cell
    {
        public required int Cx { get; init; }

        public required int Cz { get; init; }

        public readonly HeightColumn[] Cols = new HeightColumn[Columns];
        public readonly sbyte[] Label = new sbyte[Columns];
        public readonly List<Component> Components = new();
        public bool EdgesBuilt;
    }

    public static int CellOf(int block) => block >> Shift;

    /// <summary>The node containing column (x, z), or null when the column is not passable.</summary>
    public CoarseNodeId? NodeAt(int x, int z)
    {
        Cell c = GetCell(CellOf(x), CellOf(z));
        int l = c.Label[Local(x, z)];
        return l < 0 ? null : c.Components[l].Id;
    }

    public Component Get(CoarseNodeId id) => GetCell(id.Cx, id.Cz).Components[id.Index];

    /// <summary>Outgoing edges (built on first use, together with the frontier flag).</summary>
    public Component Expand(CoarseNodeId id)
    {
        Cell cell = GetCell(id.Cx, id.Cz);
        if (!cell.EdgesBuilt)
        {
            BuildEdges(cell);
        }

        return cell.Components[id.Index];
    }

    private static int Local(int x, int z) => ((z & (CellSize - 1)) << Shift) | (x & (CellSize - 1));

    private HeightColumn Column(int x, int z) => GetCell(CellOf(x), CellOf(z)).Cols[Local(x, z)];

    private int LabelAt(int x, int z, out Cell cell)
    {
        cell = GetCell(CellOf(x), CellOf(z));
        return cell.Label[Local(x, z)];
    }

    private Cell GetCell(int cx, int cz)
    {
        if (_cells.TryGetValue((cx, cz), out Cell? cell))
        {
            return cell;
        }

        cell = new Cell { Cx = cx, Cz = cz };
        int x0 = cx << Shift, z0 = cz << Shift;
        for (int i = 0; i < Columns; i++)
        {
            cell.Cols[i] = _heights.GetColumn(x0 + (i & (CellSize - 1)), z0 + (i >> Shift));
            cell.Label[i] = -1;
        }

        // Flood fill components, scanning in (z, x) order so labels are deterministic.
        Span<int> queue = stackalloc int[Columns];
        var members = new List<int>(Columns);
        for (int seed = 0; seed < Columns; seed++)
        {
            if (cell.Label[seed] >= 0 || !cell.Cols[seed].IsPassable)
            {
                continue;
            }

            sbyte label = (sbyte)cell.Components.Count;
            int head = 0, tail = 0;
            queue[tail++] = seed;
            cell.Label[seed] = label;
            members.Clear();
            while (head < tail)
            {
                int i = queue[head++];
                members.Add(i);
                int lx = i & (CellSize - 1), lz = i >> Shift;
                foreach ((int dx, int dz) in Neighbours4)
                {
                    int nx = lx + dx, nz = lz + dz;
                    if ((uint)nx >= CellSize || (uint)nz >= CellSize)
                    {
                        continue;
                    }

                    int j = (nz << Shift) | nx;
                    if (cell.Label[j] < 0 && cell.Cols[j].IsPassable
                        && Math.Abs((long)cell.Cols[j].FeetY - cell.Cols[i].FeetY) <= _o.MaxClimb)
                    {
                        cell.Label[j] = label;
                        queue[tail++] = j;
                    }
                }
            }

            cell.Components.Add(MakeComponent(cell, label, members, x0, z0));
        }

        _cells[(cx, cz)] = cell;
        return cell;
    }

    private static Component MakeComponent(Cell cell, int label, List<int> members, int x0, int z0)
    {
        double sx = 0, sz = 0;
        int water = 0;
        foreach (int i in members)
        {
            sx += i & (CellSize - 1);
            sz += i >> Shift;
            water += cell.Cols[i].Kind == ColumnKind.Water ? 1 : 0;
        }

        double mx = sx / members.Count, mz = sz / members.Count;
        int best = members[0];
        double bestD = double.MaxValue;
        foreach (int i in members)
        {
            // Prefer land over water as the waypoint, then the column nearest the centroid; ties keep scan order.
            double d = Sq((i & (CellSize - 1)) - mx) + Sq((i >> Shift) - mz) + (cell.Cols[i].Kind == ColumnKind.Water ? 1000 : 0);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return new Component
        {
            Id = new CoarseNodeId(cell.Cx, cell.Cz, label),
            Rep = new GridPos(x0 + (best & (CellSize - 1)), cell.Cols[best].FeetY, z0 + (best >> Shift)),
            WaterFraction = (double)water / members.Count,
            ColumnCount = members.Count,
        };
    }

    private static double Sq(double v) => v * v;

    private void BuildEdges(Cell cell)
    {
        var edges = new List<CoarseNodeId>[cell.Components.Count];
        var seen = new HashSet<CoarseNodeId>[cell.Components.Count];
        for (int k = 0; k < edges.Length; k++)
        {
            edges[k] = new List<CoarseNodeId>();
            seen[k] = new HashSet<CoarseNodeId>();
        }

        int x0 = cell.Cx << Shift, z0 = cell.Cz << Shift;
        for (int i = 0; i < Columns; i++)
        {
            int from = cell.Label[i];
            if (from < 0)
            {
                continue;
            }

            int x = x0 + (i & (CellSize - 1)), z = z0 + (i >> Shift);
            int y = cell.Cols[i].FeetY;
            foreach ((int dx, int dz) in Neighbours4)
            {
                long nxl = (long)x + dx, nzl = (long)z + dz;
                if (nxl is < int.MinValue or > int.MaxValue || nzl is < int.MinValue or > int.MaxValue)
                {
                    cell.Components[from].Frontier = true;
                    continue;
                }

                int nx = (int)nxl, nz = (int)nzl;
                HeightColumn n = Column(nx, nz);
                if (n.Kind == ColumnKind.Unknown)
                {
                    cell.Components[from].Frontier = true;
                    continue;
                }

                int to = LabelAt(nx, nz, out Cell other);
                if (to < 0)
                {
                    continue;
                }

                long dy = (long)n.FeetY - y;
                if (dy > _o.MaxClimb || -dy > _o.MaxDrop)
                {
                    continue;
                }

                CoarseNodeId target = other.Components[to].Id;
                if (target != cell.Components[from].Id && seen[from].Add(target))
                {
                    edges[from].Add(target);
                }
            }
        }

        for (int k = 0; k < edges.Length; k++)
        {
            cell.Components[k].Edges = edges[k];
        }

        cell.EdgesBuilt = true;
    }
}
