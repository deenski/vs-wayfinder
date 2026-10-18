using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Testing;

/// <summary>Outcome of a <see cref="ReferenceSearch"/> run.</summary>
public enum ReferenceSearchStatus : byte
{
    /// <summary>A cheapest path to the goal was found.</summary>
    Found = 0,

    /// <summary>Every reachable position was expanded without reaching the goal.</summary>
    NoPath,

    /// <summary>The expansion budget ran out first.</summary>
    BudgetExhausted,

    /// <summary>The start position is not occupiable (see <see cref="Standability"/>).</summary>
    InvalidStart,
}

/// <summary>Result of a <see cref="ReferenceSearch"/> run.</summary>
/// <param name="Status">How the search ended.</param>
/// <param name="Cost">Total cost of <paramref name="Path"/>; +∞ unless <see cref="ReferenceSearchStatus.Found"/>.</param>
/// <param name="Path">Feet positions from start to goal inclusive; empty unless found.</param>
/// <param name="Expanded">Positions expanded.</param>
/// <param name="UnknownMoves">Moves rejected because they depended on Unknown terrain.</param>
public sealed record ReferenceSearchResult(
    ReferenceSearchStatus Status,
    double Cost,
    IReadOnlyList<GridPos> Path,
    int Expanded,
    int UnknownMoves)
{
    /// <summary>True when a path was found.</summary>
    public bool Found => Status == ReferenceSearchStatus.Found;

    /// <summary>Highest feet Y on the path, or null if there is none.</summary>
    public int? MaxY => Path.Count == 0 ? null : Path.Max(p => p.Y);

    /// <summary>Sum of upward steps along the path (the HUD's ▲ figure).</summary>
    public int TotalClimb
    {
        get
        {
            int climb = 0;
            for (int i = 1; i < Path.Count; i++)
            {
                climb += Math.Max(0, Path[i].Y - Path[i - 1].Y);
            }

            return climb;
        }
    }
}

/// <summary>
/// <b>Test oracle only; lives in <c>Wayfinding.Pathing.Testing</c> so it never ships in the mod.</b> A deliberately
/// simple uniform-cost (Dijkstra) search over occupiable positions with 8-neighbour moves, used by the KAN-25 tests
/// to prove the <see cref="ClimbCostModel"/> and by the KAN-26 tests as the optimality oracle for the fine A*.
/// It has no heuristic, no Unknown-segment handling, no cancellation and no path simplification.
/// </summary>
/// <remarks>
/// From each position it tries every column in the 8 directions at every Y from <c>+MaxClimb</c> to
/// <c>−MaxDrop</c> and keeps the moves <see cref="ClimbCostModel.Evaluate"/> reports as Ok. Moves into Unknown
/// terrain are counted and skipped. Never throws for grid contents.
/// </remarks>
public static class ReferenceSearch
{
    private static readonly (int Dx, int Dz)[] Directions =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    /// <summary>Finds the cheapest path from <paramref name="start"/> to <paramref name="goal"/> (feet positions).</summary>
    /// <param name="grid">The terrain.</param>
    /// <param name="start">Start feet position.</param>
    /// <param name="goal">Goal feet position (must be matched exactly, Y included).</param>
    /// <param name="model">Cost model; defaults if null.</param>
    /// <param name="maxExpanded">Expansion budget.</param>
    /// <returns>The result.</returns>
    public static ReferenceSearchResult FindPath(IBlockGrid grid, GridPos start, GridPos goal, ClimbCostModel? model = null, int maxExpanded = 250_000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        model ??= new ClimbCostModel();
        if (!Standability.Classify(grid, start, model.Options.HeadroomCells).IsOccupiable())
        {
            return new ReferenceSearchResult(ReferenceSearchStatus.InvalidStart, double.PositiveInfinity, [], 0, 0);
        }

        var best = new Dictionary<GridPos, double> { [start] = 0 };
        var parent = new Dictionary<GridPos, GridPos>();
        var closed = new HashSet<GridPos>();
        var open = new PriorityQueue<GridPos, double>();
        open.Enqueue(start, 0);
        int expanded = 0;
        int unknownMoves = 0;
        int maxClimb = model.Options.MaxClimb;
        int maxDrop = model.Options.MaxDrop;

        while (open.TryDequeue(out GridPos current, out double g))
        {
            if (!closed.Add(current))
            {
                continue; // stale queue entry
            }

            if (current == goal)
            {
                return new ReferenceSearchResult(ReferenceSearchStatus.Found, g, Reconstruct(parent, start, goal), expanded, unknownMoves);
            }

            if (expanded >= maxExpanded)
            {
                return new ReferenceSearchResult(ReferenceSearchStatus.BudgetExhausted, double.PositiveInfinity, [], expanded, unknownMoves);
            }

            expanded++;
            foreach ((int dx, int dz) in Directions)
            {
                for (int dy = maxClimb; dy >= -maxDrop; dy--)
                {
                    GridPos next = current.Offset(dx, dy, dz);
                    if (closed.Contains(next))
                    {
                        continue;
                    }

                    MoveCost move = model.Evaluate(grid, current, next);
                    if (move.Verdict == MoveVerdict.Unknown)
                    {
                        unknownMoves++;
                        continue;
                    }

                    if (!move.IsOk)
                    {
                        continue;
                    }

                    double candidate = g + move.Cost;
                    if (!best.TryGetValue(next, out double known) || candidate < known)
                    {
                        best[next] = candidate;
                        parent[next] = current;
                        open.Enqueue(next, candidate);
                    }
                }
            }
        }

        return new ReferenceSearchResult(ReferenceSearchStatus.NoPath, double.PositiveInfinity, [], expanded, unknownMoves);
    }

    private static List<GridPos> Reconstruct(Dictionary<GridPos, GridPos> parent, GridPos start, GridPos goal)
    {
        var path = new List<GridPos> { goal };
        GridPos p = goal;
        while (p != start)
        {
            p = parent[p];
            path.Add(p);
        }

        path.Reverse();
        return path;
    }
}
