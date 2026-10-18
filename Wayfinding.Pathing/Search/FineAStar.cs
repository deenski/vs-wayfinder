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
/// <c>−GeneratedDrop</c> (<see cref="ClimbCostOptions.MaxDrop"/>, or <see cref="ClimbCostOptions.MaxWaterDrop"/>
/// when that is larger), kept when <see cref="ClimbCostModel.Evaluate"/> says Ok (the same move set as
/// <c>ReferenceSearch</c>, so with build moves off the two always agree on cost). A drop past
/// <see cref="ClimbCostOptions.MaxDrop"/> survives only when Evaluate accepts it as a landing in deep water.
/// Build moves are added when <see cref="BuildMoveOptions.Allowed"/>; see <see cref="BuildMoveOptions"/>.</para>
/// <para><b>Heuristic.</b> <c>h = octile(dx, dz) + UphillPenalty × max(0, goal.Y − y) + DownhillPenalty ×
/// max(0, y − goal.Y)</c>. Every move costs at least its horizontal distance plus its own climb/drop penalties
/// (water multiplies distance by ≥ 1, an allowed water drop still pays <see cref="ClimbCostOptions.DownhillPenalty"/>
/// per block, and build penalties are ≥ 0), so h is admissible and consistent and the
/// first time the goal is dequeued its cost is optimal.</para>
/// <para><b>Ties.</b> The open list is ordered by (f, h, X, Y, Z), a total order, and a node's parent only changes
/// on a strictly cheaper path, so the same grid and endpoints always give the same route.</para>
/// <para><b>Unknown.</b> Moves that depend on Unknown cells are never taken. If the goal cannot be reached over
/// known terrain but Unknown was met, the route runs to the boundary position with the lowest
/// (g + h, h, X, Y, Z) and ends in a straight <see cref="UnknownSegment"/> to the goal (min-f fits there: the
/// tail really is an optimistic guess over Unknown terrain).</para>
/// <para><b>Budgets.</b> When the node or time budget runs out, the route runs to the open-frontier position
/// with the lowest (h, g, X, Y, Z): the position nearest the goal that the search was about to explore, reached
/// over an optimal prefix. It then ends in an <see cref="UnknownSegment"/> to the goal. That tail is the
/// unplanned remainder drawn as a straight line. It is not a walkable claim and may pass through known solid
/// blocks; renderers must style it as <see cref="SegmentKind.Unknown"/>, never as a Known segment.</para>
/// <para>Reads the grid only, allocates per call, and keeps no static state, so concurrent calls are safe.</para>
/// </remarks>
public static class FineAStar
{
    private const double Epsilon = 1e-9;
    private const double SqrtTwoMinusOne = 0.41421356237309515; // Math.Sqrt(2) - 1

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
        // than the Euclidean ClimbCostModel.Heuristic and a lower bound for every move made of such steps.
        double ax = Math.Abs((double)goal.X - from.X);
        double az = Math.Abs((double)goal.Z - from.Z);
        double h = Math.Max(ax, az) + SqrtTwoMinusOne * Math.Min(ax, az);
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
            int bestBoundary = -1; // Unknown fallback: lowest (g + h, h, X, Y, Z) among positions next to Unknown

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
                    return Partial(FineSearchStatus.BudgetExhausted, ClosestFrontier(), expanded);
                }

                if (_o.MaxTime is { } maxTime && expanded % FineSearchOptions.TimeCheckInterval == 0
                    && _o.TimeProvider.GetElapsedTime(t0) > maxTime)
                {
                    return Partial(FineSearchStatus.TimedOut, ClosestFrontier(), expanded);
                }

                node.Closed = true;
                _nodes[ni] = node;
                expanded++;
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

        /// <summary>
        /// The budget / time-out fallback (PR #6 review, must-fix 1): the open (generated, not yet expanded) position
        /// nearest the goal by h, ties to the lower g, then X, Y, Z. Its parent is expanded, so the route to it is an
        /// optimal prefix plus one valid move.
        /// </summary>
        /// <remarks>
        /// Neither expanded-set rule makes progress around an obstacle. Min-f stalls at the last position before
        /// the first detour, because f never decreases in expansion order with a consistent h. Min-h over expanded
        /// positions stalls too: the wall face straight towards the goal stays the expanded position with the
        /// lowest h until the search has nearly finished. On the review's 60×40 wall field (746 expansions to the
        /// goal), both rules returned (19, 1, 35) at every budget from 50 to 700. The open frontier is where the
        /// search is about to go, and its lowest-h member moves along the wall towards the gap as the budget grows.
        /// One O(n) scan, done only when the search gives up.
        /// </remarks>
        private int ClosestFrontier()
        {
            int best = -1;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (!_nodes[i].Closed && (best < 0 || Closer(i, best)))
                {
                    best = i;
                }
            }

            return best;
        }

        private bool Closer(int a, int b)
        {
            Node na = _nodes[a];
            Node nb = _nodes[b];
            return KeyComparer.Instance.Compare(
                new Key(na.H, na.G, na.Pos.X, na.Pos.Y, na.Pos.Z),
                new Key(nb.H, nb.G, nb.Pos.X, nb.Pos.Y, nb.Pos.Z)) < 0;
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
            int maxDrop = _model.Options.GeneratedDrop;
            foreach ((int dx, int dz) in Directions)
            {
                for (int dy = maxClimb; dy >= -maxDrop; dy--)
                {
                    long ny = (long)cur.Y + dy;
                    if (ny < int.MinValue || ny > int.MaxValue)
                    {
                        continue;
                    }

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

        /// <summary>
        /// Step and Pillar: for each climb from <c>MaxClimb + 1</c> to <c>MaxPillarHeight</c>, stack
        /// <c>climb − MaxClimb</c> blocks in the own column (feet cell upward), stand on them, then make an ordinary
        /// step of <c>+MaxClimb</c> into a neighbouring column. With the default <c>MaxClimb = 1</c> that is the
        /// design's "climb − 1 blocks". 1 block is a <see cref="BuildKind.Step"/>, more is a <see cref="BuildKind.Pillar"/>.
        /// </summary>
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
            int maxClimb = _model.Options.MaxClimb;
            for (int climb = maxClimb + 1; climb <= top; climb++)
            {
                int blocks = climb - maxClimb; // ≥ 1; the final walking leg climbs maxClimb (possibly 0)

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

                    MoveKind kind = blocks == 1 ? MoveKind.Step : MoveKind.Pillar;
                    var detail = new BuildDetail(
                        blocks == 1 ? BuildKind.Step : BuildKind.Pillar,
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
