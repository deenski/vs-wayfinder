using System.Diagnostics;
using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>
/// The block-level planner (KAN-26 / WF-12, design v2 §3 "fine A*"): A* over occupiable positions of an
/// <see cref="IBlockGrid"/>, with the <see cref="ClimbCostModel"/> moves, optional soft-cost build moves, node and
/// time budgets, cancellation, an Unknown boundary, and deterministic tie-breaking.
/// </summary>
/// <remarks>
/// <para><b>Moves.</b> From each position, every column in the 8 directions at every Y from <c>+MaxClimb</c> to
/// <c>−MaxDrop</c>, kept when <see cref="ClimbCostModel.Evaluate"/> says Ok (the same move set as
/// <c>ReferenceSearch</c>, so with build moves off the two always agree on cost). Build moves are added when
/// <see cref="BuildMoveOptions.Allowed"/>; see <see cref="BuildMoveOptions"/>.</para>
/// <para><b>Heuristic.</b> <c>h = octile(dx, dz) + UphillPenalty × max(0, goal.Y − y) + DownhillPenalty ×
/// max(0, y − goal.Y)</c>. Every move costs at least its horizontal distance plus its own climb/drop penalties
/// (water multiplies distance by ≥ 1 and build penalties are ≥ 0), so h is admissible and consistent and the
/// first time the goal is dequeued its cost is optimal.</para>
/// <para><b>Ties.</b> The open list is ordered by (f, h, X, Y, Z), a total order, and a node's parent only changes
/// on a strictly cheaper path, so the same grid and endpoints always give the same route.</para>
/// <para><b>Unknown.</b> Moves that depend on Unknown cells are never taken. If the goal cannot be reached over
/// known terrain but Unknown was met, the route runs to the boundary position with the lowest
/// (g + h, h, X, Y, Z) and ends in a straight <see cref="UnknownSegment"/> to the goal. Budget and time
/// exhaustion return the same kind of partial route from the best expanded position.</para>
/// <para>Reads the grid only, allocates per call, and keeps no static state, so concurrent calls are safe.</para>
/// </remarks>
public static class FineAStar
{
    private const double Epsilon = 1e-9;

    private static readonly (int Dx, int Dz)[] Directions =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    /// <summary>Plans a route from <paramref name="start"/> to <paramref name="goal"/> (feet positions).</summary>
    /// <param name="grid">The terrain.</param>
    /// <param name="start">Start feet position; must be occupiable.</param>
    /// <param name="goal">Goal feet position, matched exactly (Y included). May lie in Unknown terrain.</param>
    /// <param name="options">Budgets, cost model and build moves; <see cref="FineSearchOptions.Default"/> if null.</param>
    /// <param name="cancellationToken">Stops the search with <see cref="FineSearchStatus.Cancelled"/>.</param>
    /// <returns>The route. Never throws for grid contents.</returns>
    public static FineRoute FindPath(IBlockGrid grid, GridPos start, GridPos goal, FineSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        options = (options ?? FineSearchOptions.Default).Validate();
        return new Search(grid, start, goal, options, cancellationToken).Run();
    }

    /// <summary>The admissible, consistent heuristic used by <see cref="FindPath"/>.</summary>
    /// <param name="model">Cost model (penalties).</param>
    /// <param name="from">Position.</param>
    /// <param name="goal">Goal.</param>
    /// <returns>A lower bound on the cost from <paramref name="from"/> to <paramref name="goal"/>.</returns>
    public static double Heuristic(ClimbCostModel model, GridPos from, GridPos goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        // Octile distance: the cheapest way to cover (dx, dz) with unit and diagonal (√2) column steps. Tighter
        // than the Euclidean ClimbCostModel.Heuristic and still a lower bound for every move (bridges are straight).
        double ax = Math.Abs((double)goal.X - from.X);
        double az = Math.Abs((double)goal.Z - from.Z);
        double h = Math.Max(ax, az) + (Math.Sqrt(2) - 1) * Math.Min(ax, az);
        long dy = (long)goal.Y - from.Y;
        return dy >= 0 ? h + model.Options.UphillPenalty * dy : h + model.Options.DownhillPenalty * -(double)dy;
    }

    private readonly record struct Key(double F, double H, int X, int Y, int Z);

    private sealed class KeyComparer : IComparer<Key>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(Key a, Key b)
        {
            int c = a.F.CompareTo(b.F);
            if (c != 0) return c;
            c = a.H.CompareTo(b.H);
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            c = a.Y.CompareTo(b.Y);
            return c != 0 ? c : a.Z.CompareTo(b.Z);
        }
    }

    /// <summary>The intermediate positions and placements of a build move.</summary>
    private sealed record BuildDetail(BuildKind Kind, RouteStep[] Via, GridPos[] Placed);

    private struct Node
    {
        public GridPos Pos;
        public double G;
        public double H;
        public int Parent;
        public MoveKind Kind;
        public double MoveCost;
        public BuildDetail? Detail;
        public bool Closed;
    }

    private sealed class Search
    {
        private readonly IBlockGrid _grid;
        private readonly GridPos _start;
        private readonly GridPos _goal;
        private readonly FineSearchOptions _o;
        private readonly ClimbCostModel _model;
        private readonly CancellationToken _ct;
        private readonly Dictionary<GridPos, int> _index = new();
        private readonly List<Node> _nodes = new();
        private readonly PriorityQueue<int, Key> _open = new(KeyComparer.Instance);
        private readonly PlacedBlocksGrid _overlay;
        private int _unknownMoves;

        public Search(IBlockGrid grid, GridPos start, GridPos goal, FineSearchOptions o, CancellationToken ct)
        {
            _grid = grid;
            _start = start;
            _goal = goal;
            _o = o;
            _model = o.Model;
            _ct = ct;
            _overlay = new PlacedBlocksGrid(grid, Math.Max(o.Build.MaxBridgeLength, o.Build.MaxPillarHeight));
        }

        public FineRoute Run()
        {
            int headroom = _model.Options.HeadroomCells;
            if (_ct.IsCancellationRequested)
            {
                return FineRoute.Empty(FineSearchStatus.Cancelled);
            }

            if (!Standability.Classify(_grid, _start, headroom).IsOccupiable())
            {
                return FineRoute.Empty(FineSearchStatus.InvalidStart);
            }

            if (Standability.Classify(_grid, _goal, headroom) is StandKind.None or StandKind.Dangerous)
            {
                return FineRoute.Empty(FineSearchStatus.InvalidGoal);
            }

            long t0 = _o.TimeProvider.GetTimestamp();
            Push(_start, 0, -1, MoveKind.Start, 0, null);
            int expanded = 0;
            int bestExpanded = -1;
            int bestBoundary = -1;

            while (_open.TryDequeue(out int ni, out Key key))
            {
                Node node = _nodes[ni];
                if (node.Closed || key.F - node.H > node.G + Epsilon)
                {
                    continue; // stale entry
                }

                if (_ct.IsCancellationRequested)
                {
                    return FineRoute.Empty(FineSearchStatus.Cancelled, expanded, _unknownMoves);
                }

                if (node.Pos == _goal)
                {
                    return Build(FineSearchStatus.Found, ni, null, expanded);
                }

                if (expanded >= _o.MaxNodes)
                {
                    return Partial(FineSearchStatus.BudgetExhausted, bestExpanded, expanded);
                }

                if (_o.MaxTime is { } maxTime && expanded % FineSearchOptions.TimeCheckInterval == 0
                    && _o.TimeProvider.GetElapsedTime(t0) > maxTime)
                {
                    return Partial(FineSearchStatus.TimedOut, bestExpanded, expanded);
                }

                node.Closed = true;
                _nodes[ni] = node;
                expanded++;
                if (bestExpanded < 0 || Better(ni, bestExpanded))
                {
                    bestExpanded = ni;
                }

                int unknownBefore = _unknownMoves;
                ExpandWalking(ni);
                if (_o.Build.Allowed)
                {
                    ExpandPillars(ni);
                    ExpandBridges(ni);
                }

                if (_unknownMoves > unknownBefore && (bestBoundary < 0 || Better(ni, bestBoundary)))
                {
                    bestBoundary = ni;
                }
            }

            return bestBoundary >= 0
                ? Partial(FineSearchStatus.UnknownBoundary, bestBoundary, expanded)
                : FineRoute.Empty(FineSearchStatus.NoPath, expanded, _unknownMoves);
        }

        private bool Better(int a, int b)
        {
            Node na = _nodes[a];
            Node nb = _nodes[b];
            return KeyComparer.Instance.Compare(
                new Key(na.G + na.H, na.H, na.Pos.X, na.Pos.Y, na.Pos.Z),
                new Key(nb.G + nb.H, nb.H, nb.Pos.X, nb.Pos.Y, nb.Pos.Z)) < 0;
        }

        private bool IsClosed(GridPos p) => _index.TryGetValue(p, out int i) && _nodes[i].Closed;

        private void Push(GridPos pos, double g, int parent, MoveKind kind, double moveCost, BuildDetail? detail)
        {
            if (_index.TryGetValue(pos, out int i))
            {
                Node n = _nodes[i];
                if (n.Closed || g >= n.G)
                {
                    return; // strictly cheaper only: keeps the first-found parent on ties
                }

                n.G = g;
                n.Parent = parent;
                n.Kind = kind;
                n.MoveCost = moveCost;
                n.Detail = detail;
                _nodes[i] = n;
                _open.Enqueue(i, new Key(g + n.H, n.H, pos.X, pos.Y, pos.Z));
                return;
            }

            double h = Heuristic(_model, pos, _goal);
            i = _nodes.Count;
            _nodes.Add(new Node { Pos = pos, G = g, H = h, Parent = parent, Kind = kind, MoveCost = moveCost, Detail = detail });
            _index[pos] = i;
            _open.Enqueue(i, new Key(g + h, h, pos.X, pos.Y, pos.Z));
        }

        private void ExpandWalking(int ni)
        {
            GridPos cur = _nodes[ni].Pos;
            double g = _nodes[ni].G;
            int maxClimb = _model.Options.MaxClimb;
            int maxDrop = _model.Options.MaxDrop;
            foreach ((int dx, int dz) in Directions)
            {
                for (int dy = maxClimb; dy >= -maxDrop; dy--)
                {
                    GridPos next = cur.Offset(dx, dy, dz);
                    if (IsClosed(next))
                    {
                        continue;
                    }

                    MoveCost move = _model.Evaluate(_grid, cur, next);
                    if (move.Verdict == MoveVerdict.Unknown)
                    {
                        _unknownMoves++;
                    }
                    else if (move.IsOk)
                    {
                        Push(next, g + move.Cost, ni, move.Swim ? MoveKind.Swim : MoveKind.Walk, move.Cost, null);
                    }
                }
            }
        }

        /// <summary>Step (climb 2) and Pillar (climb 3..MaxPillarHeight): stack blocks in the own column, then step up.</summary>
        private void ExpandPillars(int ni)
        {
            GridPos cur = _nodes[ni].Pos;
            if (_grid.GetClass(cur) == CellClass.Water)
            {
                return; // no pillaring while swimming
            }

            double g = _nodes[ni].G;
            int headroom = _model.Options.HeadroomCells;
            double uphill = _model.Options.UphillPenalty;
            double penalty = _o.Build.BuildPenalty;
            int top = _o.Build.MaxPillarHeight;
            for (int climb = _model.Options.MaxClimb + 1; climb <= top; climb++)
            {
                int blocks = climb - 1;

                // The placement cells (feet upward) must take a block; the cells above them must hold the body.
                bool ok = true;
                bool unknown = false;
                for (int k = 0; k < blocks + headroom && ok; k++)
                {
                    long y = (long)cur.Y + k;
                    if (y > int.MaxValue)
                    {
                        unknown = true;
                        break;
                    }

                    BlockCell cell = _grid.GetCell(cur.X, (int)y, cur.Z);
                    if (cell.Class == CellClass.Unknown)
                    {
                        unknown = true;
                    }
                    else if (k < blocks)
                    {
                        ok = CanPlace(cell);
                    }
                    else
                    {
                        ok = cell.Class.IsPassable();
                    }
                }

                if (!ok)
                {
                    return; // a taller pillar would hit the same obstruction
                }

                if (unknown)
                {
                    _unknownMoves++;
                    return;
                }

                _overlay.Clear();
                for (int k = 0; k < blocks; k++)
                {
                    _overlay.Add(cur.Offset(0, k, 0));
                }

                var pillarTop = cur.Offset(0, blocks, 0);
                double climbCost = uphill * blocks + penalty * blocks;
                foreach ((int dx, int dz) in Directions)
                {
                    GridPos dest = cur.Offset(dx, climb, dz);
                    if (IsClosed(dest))
                    {
                        continue;
                    }

                    MoveCost step = _model.Evaluate(_overlay, pillarTop, dest);
                    if (step.Verdict == MoveVerdict.Unknown)
                    {
                        _unknownMoves++;
                        continue;
                    }

                    if (!step.IsOk)
                    {
                        continue;
                    }

                    MoveKind kind = climb == 2 ? MoveKind.Step : MoveKind.Pillar;
                    var detail = new BuildDetail(
                        climb == 2 ? BuildKind.Step : BuildKind.Pillar,
                        [new RouteStep(pillarTop, kind, climbCost, blocks)],
                        _overlay.ToArray());
                    Push(dest, g + climbCost + step.Cost, ni, kind, step.Cost, detail);
                }
            }
        }

        /// <summary>Bridge: 1..MaxBridgeLength gap cells in a straight line at the same feet level.</summary>
        private void ExpandBridges(int ni)
        {
            GridPos cur = _nodes[ni].Pos;
            double g = _nodes[ni].G;
            int headroom = _model.Options.HeadroomCells;
            double penalty = _o.Build.BuildPenalty;
            int maxGap = _o.Build.MaxBridgeLength;
            for (int d = 0; d < 4; d++)
            {
                (int dx, int dz) = Directions[d];
                _overlay.Clear();
                for (int i = 1; i <= maxGap + 1; i++)
                {
                    GridPos col = cur.Offset(dx * i, 0, dz * i);
                    StandKind stand = Standability.Classify(_grid, col, headroom);
                    if (stand == StandKind.Ground || stand == StandKind.Swim)
                    {
                        if (i >= 2)
                        {
                            TryBridge(ni, cur, g, col, dx, dz, penalty);
                        }

                        break; // landed (or no gap at all)
                    }

                    if (i > maxGap)
                    {
                        break;
                    }

                    BlockCell support = _grid.GetCell(col.X, col.Y - 1, col.Z);
                    if (support.Class == CellClass.Unknown)
                    {
                        _unknownMoves++;
                        break;
                    }

                    if (!CanPlaceBridge(support))
                    {
                        break;
                    }

                    _overlay.Add(col.Down);
                }
            }
        }

        private void TryBridge(int ni, GridPos cur, double g, GridPos end, int dx, int dz, double penalty)
        {
            if (IsClosed(end))
            {
                return;
            }

            int gaps = _overlay.Count;
            var via = new RouteStep[gaps];
            double total = 0;
            GridPos from = cur;
            for (int i = 1; i <= gaps + 1; i++)
            {
                GridPos to = cur.Offset(dx * i, 0, dz * i);
                MoveCost step = _model.Evaluate(_overlay, from, to);
                if (step.Verdict == MoveVerdict.Unknown)
                {
                    _unknownMoves++;
                    return;
                }

                if (!step.IsOk)
                {
                    return;
                }

                if (i <= gaps)
                {
                    double c = step.Cost + penalty;
                    via[i - 1] = new RouteStep(to, MoveKind.Bridge, c, 1);
                    total += c;
                }
                else
                {
                    total += step.Cost;
                    Push(end, g + total, ni, MoveKind.Bridge, step.Cost, new BuildDetail(BuildKind.Bridge, via, _overlay.ToArray()));
                }

                from = to;
            }
        }

        private static bool CanPlace(BlockCell cell) =>
            (cell.Flags & CellFlags.BuildForbidden) == 0 && cell.Class is CellClass.Air or CellClass.Walkable;

        private static bool CanPlaceBridge(BlockCell cell) =>
            (cell.Flags & CellFlags.BuildForbidden) == 0 && cell.Class is CellClass.Air or CellClass.Walkable or CellClass.Lava;

        private FineRoute Partial(FineSearchStatus status, int best, int expanded)
        {
            if (best < 0)
            {
                best = _index[_start];
            }

            return Build(status, best, new UnknownSegment(_nodes[best].Pos, _goal), expanded);
        }

        private FineRoute Build(FineSearchStatus status, int end, UnknownSegment? unknown, int expanded)
        {
            var steps = new List<RouteStep>();
            var sites = new List<BuildSite>();
            for (int i = end; i >= 0; i = _nodes[i].Parent)
            {
                Node n = _nodes[i];
                steps.Add(new RouteStep(n.Pos, n.Kind, n.MoveCost, 0)); // a build move's blocks sit on its Via steps
                if (n.Detail is { } d)
                {
                    for (int v = d.Via.Length - 1; v >= 0; v--)
                    {
                        steps.Add(d.Via[v]);
                    }

                    sites.Add(new BuildSite(d.Kind, _nodes[n.Parent].Pos, n.Pos, d.Placed));
                }
            }

            steps.Reverse();
            sites.Reverse();
            Debug.Assert(steps[0].Kind == MoveKind.Start, "routes begin at the start node");
            return new FineRoute(status, _nodes[end].G, steps, sites, unknown, expanded, _unknownMoves);
        }
    }
}
