using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;

namespace Wayfinding.Pathing.Coarse;

/// <summary>How a <see cref="CoarsePlanner"/> run ended.</summary>
public enum CoarseStatus : byte
{
    /// <summary>A corridor to the goal's component was found.</summary>
    Found = 0,

    /// <summary>The goal is not reachable over revealed terrain; the corridor ends at the best frontier and continues as one straight Unknown segment.</summary>
    UnknownBoundary,

    /// <summary>All reachable revealed terrain was searched, no Unknown was met, and the goal was not reached.</summary>
    NoPath,

    /// <summary>
    /// The node budget ran out; the corridor ends at the open (generated, not yet expanded) node closest to the goal
    /// by h (ties: lower g, then cell X, cell Z, component), plus an Unknown segment. Same rule as
    /// <see cref="FineSearchStatus.BudgetExhausted"/>.
    /// </summary>
    BudgetExhausted,

    /// <summary>The cancellation token fired. No corridor.</summary>
    Cancelled,

    /// <summary>The start column is not passable revealed terrain.</summary>
    InvalidStart,

    /// <summary>The goal column is known and impassable (wall, lava).</summary>
    InvalidGoal,

    /// <summary>The time budget ran out. Partial corridor chosen as for <see cref="BudgetExhausted"/>.</summary>
    TimedOut,
}

/// <summary>One straight piece of a coarse route.</summary>
/// <param name="From">Start of the piece.</param>
/// <param name="To">End of the piece.</param>
/// <param name="Kind">Rendering class: Walk, Swim (mostly-water node) or Unknown.</param>
public readonly record struct CoarseSegment(GridPos From, GridPos To, SegmentKind Kind);

/// <summary>Result of a <see cref="CoarsePlanner"/> run: a waypoint corridor for the fine A* to refine.</summary>
/// <param name="Status">How the run ended.</param>
/// <param name="Cost">Estimated cost of the planned part (rep-to-rep, see <see cref="CoarsePlanner"/>); +∞ without a corridor.</param>
/// <param name="Waypoints">Feet positions: the start, one representative per coarse node, and the goal when found.</param>
/// <param name="Cells">The 8×8 cells (cell coordinates) the corridor passes, in order: the area the fine search should stay near.</param>
/// <param name="Unknown">The single straight Unknown tail, or null when found.</param>
/// <param name="Expanded">Coarse nodes expanded.</param>
/// <param name="CellsBuilt">8×8 cells read from the height grid.</param>
public sealed record CoarseRoute(
    CoarseStatus Status,
    double Cost,
    IReadOnlyList<GridPos> Waypoints,
    IReadOnlyList<(int Cx, int Cz)> Cells,
    UnknownSegment? Unknown,
    int Expanded,
    int CellsBuilt)
{
    /// <summary>Rendering class of the piece ending at each waypoint (index 0, the start, is Walk): Swim for mostly-water nodes.</summary>
    public IReadOnlyList<SegmentKind> WaypointKinds { get; init; } = [];

    /// <summary>Water fraction of the node behind each waypoint (0 for the start), used for <see cref="CoarsePlanner.EdgeCost"/>.</summary>
    public IReadOnlyList<double> WaypointWater { get; init; } = [];

    /// <summary>True when the corridor reaches the goal.</summary>
    public bool Found => Status == CoarseStatus.Found;

    /// <summary>The route as straight pieces, the Unknown tail (if any) last.</summary>
    public IReadOnlyList<CoarseSegment> Segments
    {
        get
        {
            var list = new List<CoarseSegment>();
            for (int i = 1; i < Waypoints.Count; i++)
            {
                list.Add(new CoarseSegment(Waypoints[i - 1], Waypoints[i], i < WaypointKinds.Count ? WaypointKinds[i] : SegmentKind.Walk));
            }

            if (Unknown is { } u && u.From != u.To)
            {
                list.Add(new CoarseSegment(u.From, u.To, SegmentKind.Unknown));
            }

            return list;
        }
    }

    internal static CoarseRoute Empty(CoarseStatus status, int expanded = 0, int cells = 0) =>
        new(status, double.PositiveInfinity, [], [], null, expanded, cells);
}

/// <summary>Inputs for <see cref="CoarsePlanner"/>.</summary>
public sealed record CoarsePlannerOptions
{
    /// <summary>Defaults: default cost model, 50 000 nodes, no time limit.</summary>
    public static CoarsePlannerOptions Default { get; } = new();

    /// <summary>The cost model (penalties, MaxClimb/MaxDrop, water). Default <see cref="ClimbCostOptions.Default"/>.</summary>
    public ClimbCostModel Model { get; init; } = new();

    /// <summary>
    /// Coarse nodes expanded before giving up. Default 50 000 (a 2 000-block route needs about 3 000; each node holds
    /// a few hundred bytes of state and cell data, so this bounds memory to tens of MB). ≥ 1.
    /// </summary>
    public int MaxNodes { get; init; } = 50_000;

    /// <summary>Wall-clock budget, or null for none. Checked every <see cref="TimeCheckInterval"/> expansions.</summary>
    public TimeSpan? MaxTime { get; init; }

    /// <summary>Clock for <see cref="MaxTime"/>. Default <see cref="TimeProvider.System"/>; tests pass a fake.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>How many expansions pass between clock reads.</summary>
    public const int TimeCheckInterval = 16;

    /// <summary>Throws if any value is out of range.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public CoarsePlannerOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Model, nameof(Model));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxNodes, 1, nameof(MaxNodes));
        if (MaxTime is { } t && t < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTime), t, "MaxTime must not be negative.");
        }

        return this;
    }
}

/// <summary>
/// The coarse planner (KAN-27 / WF-13, design v2 §3): A* over the 8×8-cell component graph of an
/// <see cref="IHeightGrid"/>, producing a waypoint corridor that <see cref="HierarchicalPlanner"/> refines with
/// <see cref="FineAStar"/>.
/// </summary>
/// <remarks>
/// <para><b>Nodes and edges</b> are described on the internal graph: per cell, components of columns walkable
/// within the cell; an edge where a column steps to a 4-neighbour of another component within MaxClimb/MaxDrop.
/// Lava and Blocked columns never join a component; Unknown columns make a component a frontier.</para>
/// <para><b>Edge cost</b> between representatives a and b (real surface positions):
/// <c>|ab|<sub>horizontal</sub> × (1 + (WaterCost − 1) × mean water fraction) + UphillPenalty × max(0, Δy) +
/// DownhillPenalty × max(0, −Δy)</c>, the headline formula at coarse resolution. A long slope is one big Δy, so
/// climbing is charged the same as in the fine search; MaxClimb/MaxDrop are enforced by the edges, not the cost.</para>
/// <para><b>Heuristic</b>: Euclidean horizontal distance plus the vertical penalties to the goal: consistent for
/// these edge costs. Ties are broken on (f, h, cell X, cell Z, component), so results are deterministic.</para>
/// <para><b>Unrevealed terrain</b> is never planned through. If the goal cannot be reached, the corridor runs to
/// the expanded frontier node closest to the goal (lowest h, then lowest g, cell X, cell Z, component) and ends in
/// <b>one</b> straight Unknown segment to the goal. (The fine search ranks its Unknown boundary by f instead; at
/// coarse scale a min-f frontier next to the start, such as the edge of the revealed map, would beat real progress.)
/// Node budget and time-out end the same way at the <b>open</b> node closest to the goal, with the same (h, g, …)
/// order: the rule <see cref="FineAStar"/> uses for its budgets.</para>
/// <para><b>Cost scale.</b> Coarse costs are an estimate: Euclidean distance between representatives and net Δy
/// only, so rolling terrain is undercharged compared with the fine search's per-step ups and downs.</para>
/// </remarks>
public static class CoarsePlanner
{
    /// <summary>The cell size in blocks (8, design v2 §3).</summary>
    public const int CellSize = CoarseGraph.CellSize;

    private readonly record struct Key(double F, double H, int Cx, int Cz, int Index);

    private sealed class KeyComparer : IComparer<Key>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(Key a, Key b)
        {
            int c = a.F.CompareTo(b.F);
            if (c != 0) return c;
            c = a.H.CompareTo(b.H);
            if (c != 0) return c;
            c = a.Cx.CompareTo(b.Cx);
            if (c != 0) return c;
            c = a.Cz.CompareTo(b.Cz);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        }
    }

    private sealed class NodeState
    {
        public double G;
        public double H;
        public CoarseNodeId? Parent;
        public bool Closed;
    }

    /// <summary>Plans a corridor from <paramref name="start"/> to <paramref name="goal"/>.</summary>
    /// <param name="heights">Revealed terrain heights.</param>
    /// <param name="start">Start feet position (its column must be Land or Water).</param>
    /// <param name="goal">Goal position; its column may be Unknown.</param>
    /// <param name="options">Options; defaults if null.</param>
    /// <param name="cancellationToken">Stops the run with <see cref="CoarseStatus.Cancelled"/>.</param>
    /// <returns>The corridor. Never throws for grid contents.</returns>
    public static CoarseRoute Plan(IHeightGrid heights, GridPos start, GridPos goal, CoarsePlannerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heights);
        options ??= CoarsePlannerOptions.Default;
        options.Validate();
        long t0 = options.TimeProvider.GetTimestamp();
        ClimbCostOptions o = options.Model.Options;
        var graph = new CoarseGraph(heights, o);

        CoarseNodeId? startNode = graph.NodeAt(start.X, start.Z);
        if (startNode is null)
        {
            return CoarseRoute.Empty(CoarseStatus.InvalidStart, 0, graph.CellsBuilt);
        }

        HeightColumn goalCol = heights.GetColumn(goal.X, goal.Z);
        if (goalCol.Kind is ColumnKind.Lava or ColumnKind.Blocked)
        {
            return CoarseRoute.Empty(CoarseStatus.InvalidGoal, 0, graph.CellsBuilt);
        }

        CoarseNodeId? goalNode = graph.NodeAt(goal.X, goal.Z);
        var states = new Dictionary<CoarseNodeId, NodeState>();
        var open = new PriorityQueue<CoarseNodeId, Key>(KeyComparer.Instance);

        double H(GridPos p) => Heuristic(o, p, goal);

        GridPos startRep = start;
        var s0 = new NodeState { G = 0, H = H(start) };
        states[startNode.Value] = s0;
        open.Enqueue(startNode.Value, new Key(s0.H, s0.H, startNode.Value.Cx, startNode.Value.Cz, startNode.Value.Index));

        int expanded = 0;
        CoarseNodeId? bestFrontier = null;
        Key bestFrontierKey = default;

        // The start node is represented by the start position itself; every other node by its representative column.
        GridPos PosOf(CoarseNodeId id) => id == startNode.Value ? startRep : graph.Get(id).Rep;

        while (open.TryDequeue(out CoarseNodeId id, out Key key))
        {
            NodeState st = states[id];
            if (st.Closed || key.F - st.H > st.G + 1e-9)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return CoarseRoute.Empty(CoarseStatus.Cancelled, expanded, graph.CellsBuilt);
            }

            if (goalNode is { } gn && id == gn)
            {
                return Build(CoarseStatus.Found, id, null);
            }

            if (expanded >= options.MaxNodes)
            {
                return Build(CoarseStatus.BudgetExhausted, ClosestOpen(), goal);
            }

            if (options.MaxTime is { } maxTime && expanded % CoarsePlannerOptions.TimeCheckInterval == 0
                && options.TimeProvider.GetElapsedTime(t0) > maxTime)
            {
                return Build(CoarseStatus.TimedOut, ClosestOpen(), goal);
            }

            st.Closed = true;
            expanded++;
            CoarseGraph.Component comp = graph.Expand(id);
            // Partial routes end where the remaining straight distance is smallest: rank by (h, g, id), not by f, so
            // a frontier next to the start (with an optimistic f) never beats real progress towards the goal.
            var thisKey = new Key(st.H, st.G, id.Cx, id.Cz, id.Index);
            if (comp.Frontier && (bestFrontier is null || KeyComparer.Instance.Compare(thisKey, bestFrontierKey) < 0))
            {
                bestFrontier = id;
                bestFrontierKey = thisKey;
            }

            GridPos a = PosOf(id);
            foreach (CoarseNodeId next in comp.Edges!)
            {
                CoarseGraph.Component nc = graph.Get(next);
                GridPos b = nc.Rep;
                double g = st.G + EdgeCost(o, a, b, (comp.WaterFraction + nc.WaterFraction) / 2);
                if (states.TryGetValue(next, out NodeState? ns))
                {
                    if (ns.Closed || g >= ns.G)
                    {
                        continue;
                    }
                }
                else
                {
                    ns = new NodeState { G = double.PositiveInfinity, H = H(b) };
                    states[next] = ns;
                }

                ns.G = g;
                ns.Parent = id;
                open.Enqueue(next, new Key(g + ns.H, ns.H, next.Cx, next.Cz, next.Index));
            }
        }

        return bestFrontier is { } bf
            ? Build(CoarseStatus.UnknownBoundary, bf, goal)
            : CoarseRoute.Empty(CoarseStatus.NoPath, expanded, graph.CellsBuilt);

        // Budget / time-out fallback: the open node nearest the goal by (h, g, cx, cz, index). Its parent is expanded,
        // so its chain is valid. One O(n) scan, only when giving up. The dequeued node itself is still open.
        CoarseNodeId ClosestOpen()
        {
            CoarseNodeId? best = null;
            Key bestKey = default;
            foreach ((CoarseNodeId nid, NodeState ns) in states)
            {
                if (ns.Closed)
                {
                    continue;
                }

                var k = new Key(ns.H, ns.G, nid.Cx, nid.Cz, nid.Index);
                if (best is null || KeyComparer.Instance.Compare(k, bestKey) < 0)
                {
                    best = nid;
                    bestKey = k;
                }
            }

            return best ?? startNode.Value;
        }

        CoarseRoute Build(CoarseStatus status, CoarseNodeId end, GridPos? unknownTo)
        {
            var chain = new List<CoarseNodeId>();
            for (CoarseNodeId? n = end; n is { } v; n = states[v].Parent)
            {
                chain.Add(v);
            }

            chain.Reverse();
            var waypoints = new List<GridPos> { start };
            var kinds = new List<SegmentKind> { SegmentKind.Walk };
            var water = new List<double> { 0 };
            var cells = new List<(int, int)>();
            foreach (CoarseNodeId n in chain)
            {
                if (cells.Count == 0 || cells[^1] != (n.Cx, n.Cz))
                {
                    cells.Add((n.Cx, n.Cz));
                }

                if (n == startNode.Value)
                {
                    continue;
                }

                CoarseGraph.Component c = graph.Get(n);
                waypoints.Add(c.Rep);
                kinds.Add(c.WaterFraction > 0.5 ? SegmentKind.Swim : SegmentKind.Walk);
                water.Add(c.WaterFraction);
            }

            if (status == CoarseStatus.Found)
            {
                // The goal itself stands in for its node's representative.
                if (waypoints.Count > 1)
                {
                    waypoints[^1] = goal;
                }
                else if (goal != start)
                {
                    waypoints.Add(goal);
                    kinds.Add(SegmentKind.Walk);
                    water.Add(graph.Get(chain[^1]).WaterFraction);
                }
            }

            double cost = 0;
            for (int i = 1; i < waypoints.Count; i++)
            {
                cost += EdgeCost(o, waypoints[i - 1], waypoints[i], (water[i - 1] + water[i]) / 2);
            }

            UnknownSegment? unknown = unknownTo is { } to ? new UnknownSegment(waypoints[^1], to) : null;
            return new CoarseRoute(status, cost, waypoints, cells, unknown, expanded, graph.CellsBuilt) { WaypointKinds = kinds, WaypointWater = water };
        }
    }

    /// <summary>The coarse edge cost between two surface positions (see the class remarks).</summary>
    /// <param name="o">Cost options.</param>
    /// <param name="a">From.</param>
    /// <param name="b">To.</param>
    /// <param name="waterFraction">Share of water columns along the edge, 0..1.</param>
    /// <returns>The cost.</returns>
    public static double EdgeCost(ClimbCostOptions o, GridPos a, GridPos b, double waterFraction)
    {
        ArgumentNullException.ThrowIfNull(o);
        double h = GridPos.HorizontalDistance(a, b) * (1 + (o.WaterCost - 1) * Math.Clamp(waterFraction, 0, 1));
        long dy = (long)b.Y - a.Y;
        return dy >= 0 ? h + o.UphillPenalty * dy : h + o.DownhillPenalty * -(double)dy;
    }

    /// <summary>The coarse heuristic: horizontal distance plus vertical penalties to the goal.</summary>
    /// <param name="o">Cost options.</param>
    /// <param name="from">Position.</param>
    /// <param name="goal">Goal.</param>
    /// <returns>A lower bound on <see cref="EdgeCost"/> sums from <paramref name="from"/> to <paramref name="goal"/>.</returns>
    public static double Heuristic(ClimbCostOptions o, GridPos from, GridPos goal) => EdgeCost(o, from, goal, 0);
}
