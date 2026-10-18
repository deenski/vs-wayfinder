using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;

namespace Wayfinding.Pathing.Coarse;

/// <summary>The horizontal rectangle of loaded terrain the fine search may refine in (inclusive bounds).</summary>
/// <param name="MinX">Lowest X.</param>
/// <param name="MinZ">Lowest Z.</param>
/// <param name="MaxX">Highest X.</param>
/// <param name="MaxZ">Highest Z.</param>
public readonly record struct FineWindow(int MinX, int MinZ, int MaxX, int MaxZ)
{
    /// <summary>A window centred on <paramref name="centre"/> reaching <paramref name="radius"/> blocks each way (the design's ~96-block window is radius 48).</summary>
    /// <param name="centre">Centre column.</param>
    /// <param name="radius">Half-width in blocks.</param>
    public static FineWindow Around(GridPos centre, int radius) =>
        new(centre.X - radius, centre.Z - radius, centre.X + radius, centre.Z + radius);

    /// <summary>True when the column of <paramref name="p"/> is inside.</summary>
    /// <param name="p">Position.</param>
    public bool Contains(GridPos p) => p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ;
}

/// <summary>Inputs for <see cref="HierarchicalPlanner"/>.</summary>
public sealed record HierarchicalOptions
{
    /// <summary>Defaults.</summary>
    public static HierarchicalOptions Default { get; } = new();

    /// <summary>Fine A* options for each refinement leg. Its cost model should match <see cref="Coarse"/>'s.</summary>
    public FineSearchOptions Fine { get; init; } = FineSearchOptions.Default;

    /// <summary>Coarse planner options.</summary>
    public CoarsePlannerOptions Coarse { get; init; } = CoarsePlannerOptions.Default;

    /// <summary>
    /// When the goal is inside the window, try one direct fine search first (optimal within the window) before the
    /// corridor. Default true; tests turn it off to measure corridor refinement on its own.
    /// </summary>
    public bool DirectWhenGoalInWindow { get; init; } = true;

    /// <summary>
    /// How many 8×8 cells either side of the coarse corridor the fine search may use. Default 1. ≥ 0.
    /// </summary>
    public int CorridorMargin { get; init; } = 1;

    /// <summary>Longest horizontal distance one fallback refinement leg aims at. Default 32 blocks (4 cells). ≥ 1.</summary>
    public int LegLength { get; init; } = 32;
}

/// <summary>Result of a <see cref="HierarchicalPlanner"/> run.</summary>
/// <param name="Status">Planning outcome over all terrain (the coarse status, or Found when the fine search reached the goal).</param>
/// <param name="FineSteps">The refined, block-level part from the start.</param>
/// <param name="FineCost">Cost of <paramref name="FineSteps"/>.</param>
/// <param name="BuildSites">Build sites on the refined part.</param>
/// <param name="CoarseTail">Coarse waypoints after the refined part (the goal last when found).</param>
/// <param name="CoarseTailCost">Coarse estimate for <paramref name="CoarseTail"/>.</param>
/// <param name="Unknown">The single straight Unknown tail, if any.</param>
/// <param name="Coarse">The coarse corridor, or null when the goal was reached by the fine search alone.</param>
public sealed record HierarchicalRoute(
    CoarseStatus Status,
    IReadOnlyList<RouteStep> FineSteps,
    double FineCost,
    IReadOnlyList<BuildSite> BuildSites,
    IReadOnlyList<GridPos> CoarseTail,
    double CoarseTailCost,
    UnknownSegment? Unknown,
    CoarseRoute? Coarse)
{
    /// <summary>True when block-level steps reach the goal.</summary>
    public bool RefinedToGoal => Status == CoarseStatus.Found && CoarseTail.Count == 0 && FineSteps.Count > 0;

    /// <summary><see cref="FineCost"/> + <see cref="CoarseTailCost"/>.</summary>
    public double TotalCost => FineCost + CoarseTailCost;

    /// <summary>Last refined position (the stitch point), or null with no refined part.</summary>
    public GridPos? StitchPoint => FineSteps.Count > 0 ? FineSteps[^1].Pos : null;

    /// <summary>All pieces for drawing: fine steps, coarse tail, then the Unknown tail.</summary>
    public IReadOnlyList<CoarseSegment> Segments
    {
        get
        {
            var list = new List<CoarseSegment>();
            for (int i = 1; i < FineSteps.Count; i++)
            {
                list.Add(new CoarseSegment(FineSteps[i - 1].Pos, FineSteps[i].Pos, FineSteps[i].Segment));
            }

            GridPos? prev = StitchPoint;
            foreach (GridPos w in CoarseTail)
            {
                if (prev is { } p)
                {
                    list.Add(new CoarseSegment(p, w, SegmentKind.Walk));
                }

                prev = w;
            }

            if (Unknown is { } u && u.From != u.To)
            {
                list.Add(new CoarseSegment(u.From, u.To, SegmentKind.Unknown));
            }

            return list;
        }
    }
}

/// <summary>
/// Coarse plus fine (design v2 §3): the coarse corridor over revealed terrain, refined by <see cref="FineAStar"/>
/// leg by leg inside the loaded fine window, then stitched: block-level steps up to the last refined waypoint,
/// coarse waypoints beyond it, and the single Unknown tail if the coarse plan has one.
/// </summary>
/// <remarks>
/// <para>Refinement aims at the last corridor waypoint inside the window (the goal, if the corridor ends there) with
/// one fine search confined to the corridor's 8×8 cells plus <see cref="HierarchicalOptions.CorridorMargin"/>
/// cells either side: cells outside read as Unknown, so the search cannot leave the corridor. That keeps the
/// fine search small and lets it cut the corners the coarse waypoints would force.</para>
/// <para>If that fails (an obstacle the 8×8 abstraction missed), it falls back to waypoint-to-waypoint legs of at
/// most <see cref="HierarchicalOptions.LegLength"/> over the whole window, trying nearer waypoints when a leg fails;
/// if none works, refinement stops and the coarse tail continues from the last refined point. A goal inside the
/// window is first tried directly with an unconfined fine search.</para>
/// </remarks>
public static class HierarchicalPlanner
{
    /// <summary>Plans a stitched route.</summary>
    /// <param name="fine">Loaded block terrain.</param>
    /// <param name="window">Where <paramref name="fine"/> may be refined.</param>
    /// <param name="heights">Revealed terrain heights (should cover the window too).</param>
    /// <param name="start">Start feet position.</param>
    /// <param name="goal">Goal position.</param>
    /// <param name="options">Options; defaults if null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The route.</returns>
    public static HierarchicalRoute Plan(IBlockGrid fine, FineWindow window, IHeightGrid heights, GridPos start, GridPos goal, HierarchicalOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fine);
        ArgumentNullException.ThrowIfNull(heights);
        options ??= HierarchicalOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LegLength, 1);

        if (options.DirectWhenGoalInWindow && window.Contains(goal))
        {
            FineRoute direct = FineAStar.FindPath(fine, start, goal, options.Fine, cancellationToken);
            if (direct.Found)
            {
                return new HierarchicalRoute(CoarseStatus.Found, direct.Steps, direct.Cost, direct.BuildSites, [], 0, null, null);
            }
        }

        CoarseRoute coarse = CoarsePlanner.Plan(heights, start, goal, options.Coarse, cancellationToken);
        if (coarse.Waypoints.Count == 0)
        {
            return new HierarchicalRoute(coarse.Status, [], double.PositiveInfinity, [], [], double.PositiveInfinity, null, coarse);
        }

        IReadOnlyList<GridPos> w = coarse.Waypoints;
        var steps = new List<RouteStep> { new(start, MoveKind.Start, 0, 0) };
        var sites = new List<BuildSite>();
        double fineCost = 0;
        int at = 0;

        // 1. One fine search to the last corridor waypoint inside the window, confined to the corridor cells.
        int last = 0;
        while (last + 1 < w.Count && window.Contains(w[last + 1]))
        {
            last++;
        }

        if (last > 0)
        {
            var corridor = new CorridorGrid(fine, CorridorCells(w, last, options.CorridorMargin), window);
            FineRoute r = FineAStar.FindPath(corridor, start, w[last], options.Fine, cancellationToken);
            if (r.Found)
            {
                Append(r);
                at = last;
            }
        }

        // 2. Fallback: waypoint-to-waypoint legs over the whole window, where the corridor was too narrow.
        while (at < last && !cancellationToken.IsCancellationRequested)
        {
            // Farthest waypoint in the window within the leg length (at least the next one).
            int target = at + 1;
            while (target + 1 <= last && GridPos.HorizontalDistance(w[at], w[target + 1]) <= options.LegLength)
            {
                target++;
            }

            FineRoute? leg = null;
            for (; target > at; target--)
            {
                FineRoute r = FineAStar.FindPath(fine, w[at], w[target], options.Fine, cancellationToken);
                if (r.Found)
                {
                    leg = r;
                    break;
                }
            }

            if (leg is null)
            {
                break; // refinement stalls; the coarse tail continues from here
            }

            Append(leg);
            at = target;
        }

        void Append(FineRoute r)
        {
            for (int i = 1; i < r.Steps.Count; i++)
            {
                steps.Add(r.Steps[i]);
            }

            sites.AddRange(r.BuildSites);
            fineCost += r.Cost;
        }

        var tail = new List<GridPos>();
        double tailCost = 0;
        for (int j = at + 1; j < w.Count; j++)
        {
            tail.Add(w[j]);
            tailCost += CoarsePlanner.EdgeCost(options.Coarse.Model.Options, w[j - 1], w[j], 0);
        }

        UnknownSegment? unknown = coarse.Unknown is { } u ? new UnknownSegment(w[^1], u.To) : null;
        return new HierarchicalRoute(coarse.Status, steps, fineCost, sites, tail, tailCost, unknown, coarse);
    }

    private static HashSet<(int, int)> CorridorCells(IReadOnlyList<GridPos> w, int last, int margin)
    {
        var core = new HashSet<(int, int)>();
        for (int i = 1; i <= last; i++)
        {
            GridPos a = w[i - 1], b = w[i];
            int samples = Math.Max(1, (int)Math.Ceiling(GridPos.HorizontalDistance(a, b) / 2));
            for (int k = 0; k <= samples; k++)
            {
                double t = (double)k / samples;
                int x = (int)Math.Round(a.X + (b.X - a.X) * t);
                int z = (int)Math.Round(a.Z + (b.Z - a.Z) * t);
                core.Add((CoarseGraph.CellOf(x), CoarseGraph.CellOf(z)));
            }
        }

        var cells = new HashSet<(int, int)>(core);
        foreach ((int cx, int cz) in core)
        {
            for (int dz = -margin; dz <= margin; dz++)
            {
                for (int dx = -margin; dx <= margin; dx++)
                {
                    cells.Add((cx + dx, cz + dz));
                }
            }
        }

        return cells;
    }

    /// <summary>The fine grid seen through the corridor: columns outside the corridor cells or the window read as Unknown.</summary>
    private sealed class CorridorGrid(IBlockGrid inner, HashSet<(int, int)> cells, FineWindow window) : IBlockGrid
    {
        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z) => Inside(x, z) ? inner.GetClass(x, y, z) : CellClass.Unknown;

        public BlockCell GetCell(int x, int y, int z) => Inside(x, z) ? inner.GetCell(x, y, z) : BlockCell.Unknown;

        private bool Inside(int x, int z) =>
            x >= window.MinX && x <= window.MaxX && z >= window.MinZ && z <= window.MaxZ
            && cells.Contains((CoarseGraph.CellOf(x), CoarseGraph.CellOf(z)));
    }
}
