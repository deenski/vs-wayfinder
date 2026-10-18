using Wayfinding.Pathing.Cost;
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
    public bool Contains(GridPos p) => Contains(p.X, p.Z);

    /// <summary>True when column (<paramref name="x"/>, <paramref name="z"/>) is inside.</summary>
    /// <param name="x">X.</param>
    /// <param name="z">Z.</param>
    public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

/// <summary>Inputs for <see cref="HierarchicalPlanner"/>.</summary>
/// <remarks>
/// The budgets here (<see cref="MaxFineNodes"/>, <see cref="MaxGridReads"/>, <see cref="MaxTime"/>) cover the whole
/// <see cref="HierarchicalPlanner.Plan"/> call: the coarse step and every fine search share them. The per-search
/// <see cref="FineSearchOptions.MaxNodes"/>, <see cref="FineSearchOptions.MaxTime"/>,
/// <see cref="FineSearchOptions.TimeProvider"/>, <see cref="CoarsePlannerOptions.MaxTime"/> and
/// <see cref="CoarsePlannerOptions.TimeProvider"/> of <see cref="Fine"/> and <see cref="Coarse"/> are replaced
/// by what is left. <see cref="CoarsePlannerOptions.MaxNodes"/> still caps the coarse step (different units).
/// </remarks>
public sealed record HierarchicalOptions
{
    /// <summary>Defaults.</summary>
    public static HierarchicalOptions Default { get; } = new();

    /// <summary>Fine A* options for each refinement search (cost model and build moves; budgets are replaced).</summary>
    public FineSearchOptions Fine { get; init; } = FineSearchOptions.Default;

    /// <summary>Coarse planner options. Its cost model must equal <see cref="Fine"/>'s (checked by <see cref="Validate"/>).</summary>
    public CoarsePlannerOptions Coarse { get; init; } = CoarsePlannerOptions.Default;

    /// <summary>
    /// When the goal is inside the window, try one direct fine search first (optimal within the window) before the
    /// corridor. Default true; tests turn it off to measure corridor refinement on its own.
    /// </summary>
    public bool DirectWhenGoalInWindow { get; init; } = true;

    /// <summary>Longest horizontal distance one fallback refinement leg aims at. Default 32 blocks (4 cells). ≥ 1.</summary>
    public int LegLength { get; init; } = 32;

    /// <summary>How many 8×8 cells either side of the coarse corridor the fine search may use. Default 1. ≥ 0.</summary>
    public int CorridorMargin { get; init; } = 1;

    /// <summary>
    /// How far (blocks, each way horizontally and vertically) a fine target may move from a coarse representative
    /// that is not occupiable on the fine grid (e.g. a height map that counted leaves). Default 3. 0 to 8.
    /// </summary>
    public int SnapRadius { get; init; } = 3;

    /// <summary>Fine positions expanded, summed over all fine searches of one plan. Default 100 000. ≥ 1.</summary>
    public int MaxFineNodes { get; init; } = 100_000;

    /// <summary>
    /// Block-grid reads, summed over all fine searches of one plan. Default 2 000 000 (a full 96-block window
    /// flood costs about half a million). ≥ 1.
    /// </summary>
    public long MaxGridReads { get; init; } = 2_000_000;

    /// <summary>Wall-clock budget for the whole plan, or null for none.</summary>
    public TimeSpan? MaxTime { get; init; }

    /// <summary>Clock for <see cref="MaxTime"/>. Default <see cref="TimeProvider.System"/>; tests pass a fake.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Throws if any value is out of range or the two cost models differ.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    /// <exception cref="ArgumentException">The fine and coarse cost models differ.</exception>
    public HierarchicalOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Fine, nameof(Fine));
        ArgumentNullException.ThrowIfNull(Coarse, nameof(Coarse));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
        Fine.Validate();
        Coarse.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(LegLength, 1, nameof(LegLength));
        ArgumentOutOfRangeException.ThrowIfNegative(CorridorMargin, nameof(CorridorMargin));
        ArgumentOutOfRangeException.ThrowIfNegative(SnapRadius, nameof(SnapRadius));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SnapRadius, 8, nameof(SnapRadius));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFineNodes, 1, nameof(MaxFineNodes));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxGridReads, 1, nameof(MaxGridReads));
        if (MaxTime is { } t && t < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTime), t, "MaxTime must not be negative.");
        }

        // MaxClimb/MaxDrop/HeadroomCells must agree or coarse corridors can be infeasible for the fine search;
        // the penalties must agree or the stitched costs mix two scales of a different kind.
        if (Fine.Model.Options != Coarse.Model.Options)
        {
            throw new ArgumentException("Fine.Model and Coarse.Model must use the same ClimbCostOptions.", nameof(Coarse));
        }

        return this;
    }
}

/// <summary>Result of a <see cref="HierarchicalPlanner"/> run.</summary>
/// <param name="Status">
/// Planning outcome: the coarse status (Found when the fine search reached the goal), or BudgetExhausted /
/// TimedOut when the shared plan budget ran out (the route is then the stitched prefix so far plus the coarse
/// tail, if any), or Cancelled (no route).
/// </param>
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
    /// <summary>Rendering class of the piece ending at each <see cref="CoarseTail"/> waypoint (Walk or Swim).</summary>
    public IReadOnlyList<SegmentKind> CoarseTailKinds { get; init; } = [];

    /// <summary>Fine positions expanded over all fine searches of the plan.</summary>
    public int FineExpanded { get; init; }

    /// <summary>Block-grid reads over all fine searches of the plan.</summary>
    public long GridReads { get; init; }

    /// <summary>True when block-level steps reach the goal.</summary>
    public bool RefinedToGoal => Status == CoarseStatus.Found && CoarseTail.Count == 0 && FineSteps.Count > 0;

    /// <summary>True when there is something to draw.</summary>
    public bool HasRoute => FineSteps.Count > 0;

    /// <summary>
    /// <see cref="FineCost"/> + <see cref="CoarseTailCost"/>. An estimate that mixes two scales: the fine part is
    /// exact, the coarse tail is rep-to-rep with net Δy only (it undercharges rolling terrain).
    /// </summary>
    public double TotalCost => FineCost + CoarseTailCost;

    /// <summary>Last refined position (the stitch point), or null with no refined part.</summary>
    public GridPos? StitchPoint => FineSteps.Count > 0 ? FineSteps[^1].Pos : null;

    /// <summary>
    /// All pieces for drawing: fine steps, coarse tail, then the Unknown tail. Simplify only the fine part, with
    /// the real fine grid (never the corridor view); leave the coarse tail as it is.
    /// </summary>
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
            for (int i = 0; i < CoarseTail.Count; i++)
            {
                GridPos w = CoarseTail[i];
                if (prev is { } p)
                {
                    list.Add(new CoarseSegment(p, w, i < CoarseTailKinds.Count ? CoarseTailKinds[i] : SegmentKind.Walk));
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

    internal static HierarchicalRoute Empty(CoarseStatus status, CoarseRoute? coarse, int expanded, long reads) =>
        new(status, [], double.PositiveInfinity, [], [], double.PositiveInfinity, null, coarse) { FineExpanded = expanded, GridReads = reads };
}

/// <summary>
/// Coarse plus fine (design v2 §3): the coarse corridor over revealed terrain, refined by <see cref="FineAStar"/>
/// inside the loaded fine window, then stitched: block-level steps up to the last refined waypoint, coarse
/// waypoints beyond it, and the single Unknown tail if the coarse plan has one.
/// </summary>
/// <remarks>
/// <para>Refinement aims at the last corridor waypoint inside the window (the goal, if the corridor ends there) with
/// one fine search confined to the corridor's 8×8 cells plus <see cref="HierarchicalOptions.CorridorMargin"/>
/// cells either side: cells outside read as Unknown, so the search cannot leave the corridor. That keeps the
/// fine search small and lets it cut the corners the coarse waypoints would force.</para>
/// <para>If that fails (an obstacle the 8×8 abstraction missed), it falls back to waypoint-to-waypoint legs of at
/// most <see cref="HierarchicalOptions.LegLength"/> confined to the window, trying nearer waypoints when a leg
/// fails; if none works, refinement stops and the coarse tail continues from the last refined point. A goal
/// inside the window is first tried directly with a window-confined fine search. Fine targets that are not
/// occupiable on the fine grid are snapped to the nearest occupiable position within
/// <see cref="HierarchicalOptions.SnapRadius"/>.</para>
/// <para><b>One budget per plan.</b> Fine nodes, block-grid reads and wall-clock time are shared by the coarse
/// step and every fine search. When one runs out, the plan stops with BudgetExhausted or TimedOut and returns the
/// stitched prefix so far (and the coarse tail, if the coarse step finished). Cancellation always returns
/// Cancelled with no route.</para>
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
        options.Validate();
        return new Run(fine, window, heights, start, goal, options, cancellationToken).Plan();
    }

    private sealed class Run
    {
        private readonly FineWindow _window;
        private readonly IHeightGrid _heights;
        private readonly GridPos _start;
        private readonly GridPos _goal;
        private readonly HierarchicalOptions _o;
        private readonly CancellationToken _user;
        private readonly CancellationTokenSource _stop;
        private readonly BudgetGrid _grid;
        private readonly CorridorGrid _windowView;
        private readonly long _t0;
        private readonly List<RouteStep> _steps;
        private readonly List<BuildSite> _sites = [];
        private double _fineCost;
        private int _expanded;
        private CoarseStatus? _stopped;

        public Run(IBlockGrid fine, FineWindow window, IHeightGrid heights, GridPos start, GridPos goal, HierarchicalOptions o, CancellationToken user)
        {
            _window = window;
            _heights = heights;
            _start = start;
            _goal = goal;
            _o = o;
            _user = user;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(user);
            _grid = new BudgetGrid(fine, o.MaxGridReads, _stop);
            _windowView = new CorridorGrid(_grid, null, window);
            _t0 = o.TimeProvider.GetTimestamp();
            _steps = [new RouteStep(start, MoveKind.Start, 0, 0)];
        }

        private GridPos Here => _steps[^1].Pos;

        public HierarchicalRoute Plan()
        {
            try
            {
                return PlanCore();
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private HierarchicalRoute PlanCore()
        {
            if (_o.DirectWhenGoalInWindow && _window.Contains(_goal))
            {
                FineRoute direct = Fine(_windowView, _start, _goal);
                if (_user.IsCancellationRequested)
                {
                    return Cancelled(null);
                }

                if (direct.Found)
                {
                    Append(direct);
                    return Finish(CoarseStatus.Found, null, [], [], [], null);
                }

                if (_stopped is { } s)
                {
                    return Finish(s, null, [], [], [], null);
                }
            }

            CoarseRoute coarse = CoarsePlanner.Plan(_heights, _start, _goal, _o.Coarse with
            {
                MaxTime = Remaining(),
                TimeProvider = _o.TimeProvider,
            }, _stop.Token);
            if (_user.IsCancellationRequested || coarse.Status == CoarseStatus.Cancelled)
            {
                return Cancelled(coarse);
            }

            if (coarse.Waypoints.Count == 0)
            {
                return HierarchicalRoute.Empty(coarse.Status, coarse, _expanded, _grid.Reads);
            }

            IReadOnlyList<GridPos> w = coarse.Waypoints;
            if (coarse.Status == CoarseStatus.TimedOut)
            {
                return Stitch(CoarseStatus.TimedOut, coarse, 0);
            }

            // 1. One fine search to the last corridor waypoint inside the window, confined to the corridor cells.
            int last = 0;
            while (last + 1 < w.Count && _window.Contains(w[last + 1]))
            {
                last++;
            }

            int at = 0;
            if (last > 0 && !TimeUp())
            {
                var corridor = new CorridorGrid(_grid, CorridorCells(w, last, _o.CorridorMargin), _window);
                FineRoute r = Fine(corridor, _start, Target(w[last]));
                if (_user.IsCancellationRequested)
                {
                    return Cancelled(coarse);
                }

                if (r.Found)
                {
                    Append(r);
                    at = last;
                }
            }

            // 2. Fallback: waypoint-to-waypoint legs over the whole window, where the corridor was too narrow.
            while (at < last && _stopped is null)
            {
                // Farthest waypoint in the window within the leg length (at least the next one).
                int target = at + 1;
                while (target + 1 <= last && GridPos.HorizontalDistance(Here, w[target + 1]) <= _o.LegLength)
                {
                    target++;
                }

                FineRoute? leg = null;
                for (; target > at && _stopped is null; target--)
                {
                    FineRoute r = Fine(_windowView, Here, Target(w[target]));
                    if (_user.IsCancellationRequested)
                    {
                        return Cancelled(coarse);
                    }

                    if (r.Found)
                    {
                        leg = r;
                        break;
                    }
                }

                if (leg is null)
                {
                    break; // refinement stalls (or the budget ran out); the coarse tail continues from here
                }

                Append(leg);
                at = target;
            }

            return Stitch(_stopped ?? coarse.Status, coarse, at);
        }

        private HierarchicalRoute Stitch(CoarseStatus status, CoarseRoute coarse, int at)
        {
            IReadOnlyList<GridPos> w = coarse.Waypoints;
            var tail = new List<GridPos>();
            var kinds = new List<SegmentKind>();
            var water = new List<double>();
            for (int j = at + 1; j < w.Count; j++)
            {
                tail.Add(w[j]);
                kinds.Add(j < coarse.WaypointKinds.Count ? coarse.WaypointKinds[j] : SegmentKind.Walk);
                water.Add(j < coarse.WaypointWater.Count ? coarse.WaypointWater[j] : 0);
            }

            double atWater = at < coarse.WaypointWater.Count ? coarse.WaypointWater[at] : 0;
            UnknownSegment? unknown = coarse.Unknown is { } u ? new UnknownSegment(tail.Count > 0 ? tail[^1] : Here, u.To) : null;
            return Finish(status, coarse, tail, kinds, water, unknown, atWater);
        }

        private HierarchicalRoute Finish(CoarseStatus status, CoarseRoute? coarse, List<GridPos> tail, List<SegmentKind> kinds, List<double> water, UnknownSegment? unknown, double stitchWater = 0)
        {
            ClimbCostOptions co = _o.Coarse.Model.Options;
            double tailCost = 0;
            GridPos prev = Here;
            double prevWater = stitchWater;
            for (int i = 0; i < tail.Count; i++)
            {
                tailCost += CoarsePlanner.EdgeCost(co, prev, tail[i], (prevWater + water[i]) / 2);
                prev = tail[i];
                prevWater = water[i];
            }

            return new HierarchicalRoute(status, _steps, _fineCost, _sites, tail, tailCost, unknown, coarse)
            {
                CoarseTailKinds = kinds,
                FineExpanded = _expanded,
                GridReads = _grid.Reads,
            };
        }

        private HierarchicalRoute Cancelled(CoarseRoute? coarse) =>
            HierarchicalRoute.Empty(CoarseStatus.Cancelled, coarse, _expanded, _grid.Reads);

        private void Append(FineRoute r)
        {
            for (int i = 1; i < r.Steps.Count; i++)
            {
                _steps.Add(r.Steps[i]);
            }

            _sites.AddRange(r.BuildSites);
            _fineCost += r.Cost;
        }

        /// <summary>One fine search under what is left of the plan budget; records why the plan must stop.</summary>
        private FineRoute Fine(IBlockGrid grid, GridPos from, GridPos to)
        {
            if (_stopped is not null || TimeUp())
            {
                return FineRoute.Empty(FineSearchStatus.TimedOut);
            }

            int nodesLeft = _o.MaxFineNodes - _expanded;
            FineRoute r = FineAStar.FindPath(grid, from, to, _o.Fine with
            {
                MaxNodes = nodesLeft,
                MaxTime = Remaining(),
                TimeProvider = _o.TimeProvider,
            }, _stop.Token);
            _expanded += r.Expanded;

            if (_user.IsCancellationRequested)
            {
                return r;
            }

            if (_grid.Exhausted || r.Status == FineSearchStatus.BudgetExhausted || _expanded >= _o.MaxFineNodes)
            {
                _stopped = CoarseStatus.BudgetExhausted;
            }
            else if (r.Status == FineSearchStatus.TimedOut)
            {
                _stopped = CoarseStatus.TimedOut;
            }

            return r;
        }

        private TimeSpan? Remaining()
        {
            if (_o.MaxTime is not { } max)
            {
                return null;
            }

            TimeSpan left = max - _o.TimeProvider.GetElapsedTime(_t0);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        private bool TimeUp()
        {
            if (_stopped is null && Remaining() is { } left && left <= TimeSpan.Zero)
            {
                _stopped = CoarseStatus.TimedOut;
            }

            return _stopped is not null;
        }

        /// <summary>
        /// The fine target for a coarse waypoint: the waypoint itself when occupiable on the fine grid, else the
        /// nearest occupiable position within the snap radius (by horizontal distance², then |Δy|, then X, Y, Z), else
        /// the waypoint unchanged. The goal is never moved.
        /// </summary>
        private GridPos Target(GridPos p)
        {
            if (p == _goal || Occupiable(p))
            {
                return p;
            }

            int r = _o.SnapRadius;
            GridPos? best = null;
            (long D, int Dy, int X, int Y, int Z) bestKey = default;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    long x = (long)p.X + dx, z = (long)p.Z + dz;
                    if (x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue || !_window.Contains((int)x, (int)z))
                    {
                        continue;
                    }

                    for (int dy = -r; dy <= r; dy++)
                    {
                        long y = (long)p.Y + dy;
                        if (y is < int.MinValue or > int.MaxValue)
                        {
                            continue;
                        }

                        var q = new GridPos((int)x, (int)y, (int)z);
                        var key = ((long)dx * dx + (long)dz * dz, Math.Abs(dy), q.X, q.Y, q.Z);
                        if (best is not null && key.CompareTo(bestKey) >= 0)
                        {
                            continue;
                        }

                        if (Occupiable(q))
                        {
                            best = q;
                            bestKey = key;
                        }
                    }
                }
            }

            return best ?? p;
        }

        private bool Occupiable(GridPos p) =>
            _window.Contains(p) && Standability.Classify(_grid, p, _o.Fine.Model.Options.HeadroomCells).IsOccupiable();
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

    /// <summary>
    /// The fine grid seen through the window and, when <paramref name="cells"/> is set, the corridor: columns outside
    /// read as Unknown, which <see cref="FineAStar"/> never enters.
    /// </summary>
    internal sealed class CorridorGrid(IBlockGrid inner, HashSet<(int, int)>? cells, FineWindow window) : IBlockGrid
    {
        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z) => Inside(x, z) ? inner.GetClass(x, y, z) : CellClass.Unknown;

        public BlockCell GetCell(int x, int y, int z) => Inside(x, z) ? inner.GetCell(x, y, z) : BlockCell.Unknown;

        private bool Inside(int x, int z) =>
            window.Contains(x, z) && (cells is null || cells.Contains((CoarseGraph.CellOf(x), CoarseGraph.CellOf(z))));
    }

    /// <summary>Counts block reads for the plan budget; once over, stops the running search through the shared token.</summary>
    private sealed class BudgetGrid(IBlockGrid inner, long maxReads, CancellationTokenSource stop) : IBlockGrid
    {
        private long _reads;

        public long Reads => _reads;

        public bool Exhausted => _reads > maxReads;

        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z)
        {
            Count();
            return inner.GetClass(x, y, z);
        }

        public BlockCell GetCell(int x, int y, int z)
        {
            Count();
            return inner.GetCell(x, y, z);
        }

        private void Count()
        {
            if (++_reads == maxReads + 1)
            {
                stop.Cancel();
            }
        }
    }
}
