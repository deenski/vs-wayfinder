using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>
/// The user's building settings (design v2 §3 "Build moves" and the config defaults): <c>allowBuild</c>,
/// <c>requireBlocksInInventory</c>, <c>buildPenalty</c>, <c>maxBlocks</c>, <c>maxBridge</c>.
/// </summary>
public sealed record BuildPolicy
{
    /// <summary>Build moves off (the default until the user sets <c>allowBuild</c>).</summary>
    public static BuildPolicy Disabled { get; } = new();

    /// <summary>Build moves on with the design defaults.</summary>
    public static BuildPolicy EnabledDefaults { get; } = new() { AllowBuild = true };

    /// <summary><c>allowBuild</c>: whether build moves may be suggested. Default false.</summary>
    public bool AllowBuild { get; init; }

    /// <summary>
    /// <c>requireBlocksInInventory</c> (Baritone's rule): build moves are only used while the player carries
    /// placeable blocks, and the route's block cap becomes <c>min(maxBlocks, carried)</c>. Default true.
    /// </summary>
    public bool RequireBlocksInInventory { get; init; } = true;

    /// <summary><c>buildPenalty</c>: fixed cost per placed block, in blocks of walking. Default 24. Finite, ≥ 0.</summary>
    public double BuildPenalty { get; init; } = 24;

    /// <summary>
    /// <c>maxBlocks</c>: the block budget. Not enforced inside the search; a route over the cap
    /// (<see cref="BlockCap"/>) is re-planned once with <see cref="BuildPenalty"/> × <see cref="RerunPenaltyFactor"/>,
    /// and flagged if it is still over. Default 8. ≥ 0.
    /// </summary>
    public int MaxBlocks { get; init; } = 8;

    /// <summary>Penalty multiplier for the one over-cap re-run. Default 4. Finite, ≥ 1.</summary>
    public double RerunPenaltyFactor { get; init; } = 4;

    /// <summary><c>maxBridge</c>: longest bridge in gap cells. Default 4. 0 to 16.</summary>
    public int MaxBridge { get; init; } = 4;

    /// <summary>Highest climb a Step/Pillar covers. Default 4. 0 to 16.</summary>
    public int MaxPillarHeight { get; init; } = 4;

    /// <summary>Throws if any value is out of range. Allocates nothing.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public BuildPolicy Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBlocks, nameof(MaxBlocks));
        if (!double.IsFinite(RerunPenaltyFactor) || RerunPenaltyFactor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RerunPenaltyFactor), RerunPenaltyFactor, "RerunPenaltyFactor must be a finite number ≥ 1.");
        }

        if (!double.IsFinite(BuildPenalty) || BuildPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BuildPenalty), BuildPenalty, "BuildPenalty must be a finite number ≥ 0.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(MaxPillarHeight, nameof(MaxPillarHeight));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxPillarHeight, 16, nameof(MaxPillarHeight));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBridge, nameof(MaxBridge));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxBridge, 16, nameof(MaxBridge));
        return this;
    }

    /// <summary>True when build moves may be used with <paramref name="carriedBlocks"/> placeable blocks carried.</summary>
    /// <param name="carriedBlocks">Placeable blocks in the hotbar and backpack (the mod counts them).</param>
    public bool BuildingAllowed(int carriedBlocks) => AllowBuild && (!RequireBlocksInInventory || carriedBlocks > 0);

    /// <summary>
    /// The most blocks a route may place before it is re-planned and flagged: <see cref="MaxBlocks"/>, or
    /// <c>min(MaxBlocks, carried)</c> while <see cref="RequireBlocksInInventory"/> is on.
    /// </summary>
    /// <param name="carriedBlocks">Placeable blocks carried.</param>
    public int BlockCap(int carriedBlocks) => RequireBlocksInInventory ? Math.Min(MaxBlocks, Math.Max(0, carriedBlocks)) : MaxBlocks;

    /// <summary>The <see cref="BuildMoveOptions"/> for one search.</summary>
    /// <param name="carriedBlocks">Placeable blocks carried.</param>
    /// <param name="penaltyFactor">Multiplier on <see cref="BuildPenalty"/> (1, or the re-run factor).</param>
    /// <returns>Search-level build options.</returns>
    public BuildMoveOptions ToMoveOptions(int carriedBlocks, double penaltyFactor = 1) => new()
    {
        Enabled = BuildingAllowed(carriedBlocks),
        BlocksAvailable = true, // the carry rule is folded into Enabled
        BuildPenalty = BuildPenalty * penaltyFactor,
        MaxBridgeLength = MaxBridge,
        MaxPillarHeight = MaxPillarHeight,
    };
}

/// <summary>How a route relates to the block budget. Check <see cref="FineRoute.Status"/> first.</summary>
public enum BuildBudgetStatus : byte
{
    /// <summary>There is no route (cancelled, invalid endpoints, no path).</summary>
    NoRoute = 0,

    /// <summary>The planned part places no blocks.</summary>
    NoBuild,

    /// <summary>The planned part places between 1 and <see cref="BuildPolicy.BlockCap"/> blocks.</summary>
    WithinBudget,

    /// <summary>
    /// <c>requireBlocksInInventory</c> is on and the route needs more blocks than the player carries, but no more
    /// than <see cref="BuildPolicy.MaxBlocks"/>, even after the re-run. The route is returned (the player may pick
    /// blocks up on the way); <see cref="BudgetedRoute.BlocksShort"/> is the shortfall.
    /// </summary>
    ShortOfBlocks,

    /// <summary>
    /// The planned part needs more than <see cref="BuildPolicy.MaxBlocks"/> blocks even after the re-run. The route
    /// is still returned and the HUD shows the exact count (<see cref="BudgetedRoute.BlocksNeeded"/>).
    /// </summary>
    OverBudget,
}

/// <summary>A fine route with the block-budget verdict.</summary>
/// <param name="Route">The chosen route, re-validated as a whole with all of its blocks placed.</param>
/// <param name="Budget">Budget verdict for <paramref name="Route"/>.</param>
/// <param name="BlocksNeeded">Exact blocks <paramref name="Route"/> places (the HUD's "+N blocks").</param>
/// <param name="Reran">Whether the first route was over the cap and the search was re-run with the higher penalty.</param>
/// <param name="CarriedBlocks">Placeable blocks the player carried when planning.</param>
public sealed record BudgetedRoute(FineRoute Route, BuildBudgetStatus Budget, int BlocksNeeded, bool Reran, int CarriedBlocks)
{
    /// <summary>Whether <see cref="Route"/> is the re-run's route (false when the re-run's route was worse or failed).</summary>
    public bool UsedRerun { get; init; }

    /// <summary>
    /// How many re-plans were needed because a route stepped into a block it placed earlier (0 normally).
    /// </summary>
    public int Repairs { get; init; }

    /// <summary>Fine positions expanded over every search of this plan (they share one node budget).</summary>
    public int Expanded { get; init; }

    /// <summary>True when over budget.</summary>
    public bool OverBudget => Budget == BuildBudgetStatus.OverBudget;

    /// <summary>Blocks still to gather: <see cref="BlocksNeeded"/> beyond <see cref="CarriedBlocks"/>, never negative.</summary>
    public int BlocksShort => Math.Max(0, BlocksNeeded - CarriedBlocks);
}

/// <summary>
/// Runs <see cref="FineAStar"/> under a <see cref="BuildPolicy"/> (WF-14): the carry-blocks rule decides whether
/// build moves exist at all and caps the blocks, <c>buildPenalty</c> prices them inside the search, the cap is
/// applied after it with one re-run at a higher penalty, and every route is re-validated with all of its blocks
/// placed.
/// </summary>
/// <remarks>
/// <para><b>One budget.</b> All searches of one call (first, re-run, repairs) share
/// <see cref="FineSearchOptions.MaxNodes"/> and <see cref="FineSearchOptions.MaxTime"/>: each gets what is left.
/// Cancellation returns a Cancelled route with no steps, whichever search it hits.</para>
/// <para><b>Re-run.</b> For two complete routes a higher penalty can only trade blocks for walking. A bounded
/// (partial) re-run can be worse, so its route is used only when it is complete, or when the first was partial
/// too and the re-run places no more blocks. If the budget is already spent, the first route stands.</para>
/// <para><b>Whole-route check.</b> <see cref="FineAStar"/> checks each build move against the real grid plus its
/// own blocks only, so a later step may walk into a block an earlier site placed (low penalties make that likely).
/// Each route is replayed with every block placed so far; on a conflict, the conflicting placements are
/// forbidden and the search re-run (at most <see cref="MaxRepairs"/> times), then once more without build moves.
/// Blocks beyond an Unknown tail are not known yet: the budget counts the planned part only.</para>
/// </remarks>
public static class BuildPlanner
{
    /// <summary>Re-plans allowed per search after a whole-route conflict before falling back to walking only.</summary>
    public const int MaxRepairs = 2;

    /// <summary>Plans a fine route under <paramref name="policy"/>.</summary>
    /// <param name="grid">Loaded terrain.</param>
    /// <param name="start">Start feet position.</param>
    /// <param name="goal">Goal position.</param>
    /// <param name="policy">Building settings.</param>
    /// <param name="carriedBlocks">Placeable blocks carried (hotbar and backpack). ≥ 0.</param>
    /// <param name="search">Search options; their <see cref="FineSearchOptions.Build"/> is replaced and their budgets cover the whole call. Defaults if null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The route with its budget verdict.</returns>
    public static BudgetedRoute FindPath(IBlockGrid grid, GridPos start, GridPos goal, BuildPolicy policy, int carriedBlocks, FineSearchOptions? search = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(carriedBlocks);
        policy.Validate();
        search ??= FineSearchOptions.Default;
        search.Validate();

        var run = new Run(grid, start, goal, search, cancellationToken);
        int cap = policy.BlockCap(carriedBlocks);

        FineRoute first = run.Validated(policy.ToMoveOptions(carriedBlocks));
        if (run.Cancelled)
        {
            return run.Cancel(carriedBlocks);
        }

        if (first.BlocksNeeded <= cap)
        {
            return run.Verdict(first, policy, carriedBlocks, reran: false, usedRerun: false);
        }

        if (!run.HasBudget)
        {
            return run.Verdict(first, policy, carriedBlocks, reran: false, usedRerun: false);
        }

        FineRoute rerun = run.Validated(policy.ToMoveOptions(carriedBlocks, policy.RerunPenaltyFactor));
        if (run.Cancelled)
        {
            return run.Cancel(carriedBlocks);
        }

        bool useRerun = rerun.Found || (!first.Found && rerun.HasRoute && rerun.BlocksNeeded <= first.BlocksNeeded);
        return run.Verdict(useRerun ? rerun : first, policy, carriedBlocks, reran: true, usedRerun: useRerun);
    }

    /// <summary>
    /// Replays <paramref name="route"/> with every block placed so far and returns the placed cells (from earlier
    /// build sites) that a later step runs into; empty when the whole route is valid.
    /// </summary>
    /// <param name="grid">The real terrain.</param>
    /// <param name="route">A route from <see cref="FineAStar"/>.</param>
    /// <param name="model">The cost model the route was planned with.</param>
    /// <returns>The conflicting placements, or an empty list.</returns>
    public static IReadOnlyList<GridPos> FindConflicts(IBlockGrid grid, FineRoute route, ClimbCostModel model)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(model);
        if (route.BuildSites.Count == 0)
        {
            return [];
        }

        var placed = new HashSet<GridPos>();
        var view = new PlacedSetGrid(grid, placed);
        IReadOnlyList<RouteStep> steps = route.Steps;
        int site = 0;
        IReadOnlyList<GridPos> own = [];
        int headroom = model.Options.HeadroomCells;
        for (int i = 1; i < steps.Count; i++)
        {
            RouteStep s = steps[i];
            GridPos from = steps[i - 1].Pos;
            bool build = s.Kind is MoveKind.Step or MoveKind.Pillar or MoveKind.Bridge;
            if (build)
            {
                bool afterBuild = steps[i - 1].Kind is MoveKind.Step or MoveKind.Pillar or MoveKind.Bridge;
                bool newSite = site < route.BuildSites.Count && from == route.BuildSites[site].From
                    && (!afterBuild || (site > 0 && from == route.BuildSites[site - 1].To));
                if (newSite)
                {
                    own = route.BuildSites[site].Blocks;
                    var dup = own.Where(placed.Contains).ToArray();
                    if (dup.Length > 0)
                    {
                        return dup; // a block where an earlier site already put one
                    }

                    placed.UnionWith(own);
                    site++;
                }
            }
            else
            {
                own = [];
            }

            bool ok = build && from.X == s.Pos.X && from.Z == s.Pos.Z
                ? Standability.Classify(view, s.Pos, headroom).IsOccupiable()
                : model.Evaluate(view, from, s.Pos).IsOk;
            if (!ok)
            {
                // The placed blocks (not this site's own) in the cells the step touches.
                int y0 = Math.Min(from.Y, s.Pos.Y) - 1, y1 = Math.Max(from.Y, s.Pos.Y) + headroom;
                int x0 = Math.Min(from.X, s.Pos.X), x1 = Math.Max(from.X, s.Pos.X);
                int z0 = Math.Min(from.Z, s.Pos.Z), z1 = Math.Max(from.Z, s.Pos.Z);
                var hits = placed
                    .Where(p => p.X >= x0 && p.X <= x1 && p.Z >= z0 && p.Z <= z1 && p.Y >= y0 && p.Y <= y1 && !own.Contains(p))
                    .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z)
                    .ToArray();
                return hits.Length > 0 ? hits : placed.Where(p => !own.Contains(p)).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
            }
        }

        return [];
    }

    private sealed class Run(IBlockGrid grid, GridPos start, GridPos goal, FineSearchOptions search, CancellationToken token)
    {
        private readonly long _t0 = search.TimeProvider.GetTimestamp();
        private readonly HashSet<GridPos> _forbidden = [];
        private int _expanded;
        private int _repairs;

        public bool Cancelled => token.IsCancellationRequested;

        public bool HasBudget => _expanded < search.MaxNodes && (Remaining() is not { } t || t > TimeSpan.Zero);

        /// <summary>A search plus the whole-route check and repairs, under the shared budget.</summary>
        public FineRoute Validated(BuildMoveOptions build)
        {
            FineRoute r = Search(build);
            for (int attempt = 0; r.BuildSites.Count > 0 && !Cancelled; attempt++)
            {
                IReadOnlyList<GridPos> conflicts = FindConflicts(grid, r, search.Model);
                if (conflicts.Count == 0)
                {
                    return r;
                }

                _repairs++;
                _forbidden.UnionWith(conflicts);
                if (!HasBudget)
                {
                    break;
                }

                // After MaxRepairs forbidden-cell re-plans, walk only: that route has no blocks to conflict with.
                r = Search(attempt < MaxRepairs ? build : BuildMoveOptions.Disabled);
            }

            if (r.BuildSites.Count > 0 && !Cancelled && FindConflicts(grid, r, search.Model).Count > 0)
            {
                // Out of budget with an invalid route: report no route rather than one that cannot be walked.
                return FineRoute.Empty(FineSearchStatus.NoPath, r.Expanded, r.UnknownMoves);
            }

            return r;
        }

        public BudgetedRoute Cancel(int carried) =>
            new(FineRoute.Empty(FineSearchStatus.Cancelled), BuildBudgetStatus.NoRoute, 0, false, carried) { Repairs = _repairs, Expanded = _expanded };

        public BudgetedRoute Verdict(FineRoute route, BuildPolicy policy, int carried, bool reran, bool usedRerun)
        {
            int blocks = route.BlocksNeeded;
            BuildBudgetStatus status =
                !route.HasRoute ? BuildBudgetStatus.NoRoute
                : blocks == 0 ? BuildBudgetStatus.NoBuild
                : blocks <= policy.BlockCap(carried) ? BuildBudgetStatus.WithinBudget
                : blocks <= policy.MaxBlocks ? BuildBudgetStatus.ShortOfBlocks
                : BuildBudgetStatus.OverBudget;
            return new BudgetedRoute(route, status, blocks, reran, carried) { UsedRerun = usedRerun, Repairs = _repairs, Expanded = _expanded };
        }

        private FineRoute Search(BuildMoveOptions build)
        {
            if (Cancelled)
            {
                return FineRoute.Empty(FineSearchStatus.Cancelled);
            }

            IBlockGrid view = _forbidden.Count > 0 ? new ForbidGrid(grid, _forbidden) : grid;
            FineRoute r = FineAStar.FindPath(view, start, goal, search with
            {
                Build = build,
                MaxNodes = Math.Max(1, search.MaxNodes - _expanded),
                MaxTime = Remaining(),
            }, token);
            _expanded += r.Expanded;
            return r;
        }

        private TimeSpan? Remaining()
        {
            if (search.MaxTime is not { } max)
            {
                return null;
            }

            TimeSpan left = max - search.TimeProvider.GetElapsedTime(_t0);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>The real grid with a set of blocks placed (they read as Solid).</summary>
    private sealed class PlacedSetGrid(IBlockGrid inner, HashSet<GridPos> placed) : IBlockGrid
    {
        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z) => placed.Contains(new GridPos(x, y, z)) ? CellClass.Solid : inner.GetClass(x, y, z);

        public BlockCell GetCell(int x, int y, int z) => placed.Contains(new GridPos(x, y, z)) ? new BlockCell(CellClass.Solid) : inner.GetCell(x, y, z);
    }

    /// <summary>The real grid with some cells additionally flagged <see cref="CellFlags.BuildForbidden"/>.</summary>
    private sealed class ForbidGrid(IBlockGrid inner, HashSet<GridPos> forbidden) : IBlockGrid
    {
        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z) => inner.GetClass(x, y, z);

        public BlockCell GetCell(int x, int y, int z)
        {
            BlockCell c = inner.GetCell(x, y, z);
            return forbidden.Contains(new GridPos(x, y, z)) ? c with { Flags = c.Flags | CellFlags.BuildForbidden } : c;
        }
    }
}
