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
    /// placeable blocks. Default true.
    /// </summary>
    public bool RequireBlocksInInventory { get; init; } = true;

    /// <summary><c>buildPenalty</c>: fixed cost per placed block, in blocks of walking. Default 24. Finite, ≥ 0.</summary>
    public double BuildPenalty { get; init; } = 24;

    /// <summary>
    /// <c>maxBlocks</c>: the block budget. Not enforced inside the search; a route over it is re-planned once with
    /// <see cref="BuildPenalty"/> × <see cref="RerunPenaltyFactor"/>, and marked <see cref="BuildBudgetStatus.OverBudget"/>
    /// if it is still over. Default 8. ≥ 0.
    /// </summary>
    public int MaxBlocks { get; init; } = 8;

    /// <summary>Penalty multiplier for the one over-budget re-run. Default 4. Finite, ≥ 1.</summary>
    public double RerunPenaltyFactor { get; init; } = 4;

    /// <summary><c>maxBridge</c>: longest bridge in gap cells. Default 4. 0 to 16.</summary>
    public int MaxBridge { get; init; } = 4;

    /// <summary>Highest climb a Step/Pillar covers. Default 4. 0 to 16.</summary>
    public int MaxPillarHeight { get; init; } = 4;

    /// <summary>Throws if any value is out of range.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public BuildPolicy Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBlocks, nameof(MaxBlocks));
        if (!double.IsFinite(RerunPenaltyFactor) || RerunPenaltyFactor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RerunPenaltyFactor), RerunPenaltyFactor, "RerunPenaltyFactor must be a finite number ≥ 1.");
        }

        ToMoveOptions(carriedBlocks: 0, penaltyFactor: 1).Validate();
        return this;
    }

    /// <summary>True when build moves may be used with <paramref name="carriedBlocks"/> placeable blocks carried.</summary>
    /// <param name="carriedBlocks">Placeable blocks in the hotbar and backpack (the mod counts them).</param>
    public bool BuildingAllowed(int carriedBlocks) => AllowBuild && (!RequireBlocksInInventory || carriedBlocks > 0);

    /// <summary>The <see cref="BuildMoveOptions"/> for one search.</summary>
    /// <param name="carriedBlocks">Placeable blocks carried.</param>
    /// <param name="penaltyFactor">Multiplier on <see cref="BuildPenalty"/> (1, or the re-run factor).</param>
    /// <returns>Search-level build options.</returns>
    public BuildMoveOptions ToMoveOptions(int carriedBlocks, double penaltyFactor = 1) => new()
    {
        Enabled = AllowBuild,
        BlocksAvailable = !RequireBlocksInInventory || carriedBlocks > 0,
        BuildPenalty = BuildPenalty * penaltyFactor,
        MaxBridgeLength = MaxBridge,
        MaxPillarHeight = MaxPillarHeight,
    };
}

/// <summary>How a route relates to the block budget.</summary>
public enum BuildBudgetStatus : byte
{
    /// <summary>The planned part places no blocks.</summary>
    NoBuild = 0,

    /// <summary>The planned part places between 1 and <see cref="BuildPolicy.MaxBlocks"/> blocks.</summary>
    WithinBudget,

    /// <summary>
    /// The planned part needs more than <see cref="BuildPolicy.MaxBlocks"/> blocks even after the re-run. The route
    /// is still returned and the HUD shows the exact count (<see cref="BudgetedRoute.BlocksNeeded"/>).
    /// </summary>
    OverBudget,
}

/// <summary>A fine route with the block-budget verdict.</summary>
/// <param name="Route">The chosen route (the re-run's, when one was made and it found a route).</param>
/// <param name="Budget">Budget verdict for <paramref name="Route"/>.</param>
/// <param name="BlocksNeeded">Exact blocks <paramref name="Route"/> places (the HUD's "+N blocks").</param>
/// <param name="Reran">Whether the first route was over budget and the search was re-run with the higher penalty.</param>
/// <param name="CarriedBlocks">Placeable blocks the player carried when planning.</param>
public sealed record BudgetedRoute(FineRoute Route, BuildBudgetStatus Budget, int BlocksNeeded, bool Reran, int CarriedBlocks)
{
    /// <summary>True when over budget.</summary>
    public bool OverBudget => Budget == BuildBudgetStatus.OverBudget;

    /// <summary>Blocks still to gather: <see cref="BlocksNeeded"/> beyond <see cref="CarriedBlocks"/>, never negative.</summary>
    public int BlocksShort => Math.Max(0, BlocksNeeded - CarriedBlocks);
}

/// <summary>
/// Runs <see cref="FineAStar"/> under a <see cref="BuildPolicy"/> (WF-14): the carry-blocks rule decides whether
/// build moves exist at all, <c>buildPenalty</c> prices them inside the search, and <c>maxBlocks</c> is applied after
/// it with one re-run at a higher penalty.
/// </summary>
/// <remarks>
/// <para>The re-run only changes the penalty, so it can trade blocks for walking but never loses reachability. Its
/// route is kept when it is complete (or, for partial routes, when it has a route at all); otherwise the first
/// route stands. The budget counts the planned part only: blocks beyond an Unknown tail are not known yet.</para>
/// </remarks>
public static class BuildPlanner
{
    /// <summary>Plans a fine route under <paramref name="policy"/>.</summary>
    /// <param name="grid">Loaded terrain.</param>
    /// <param name="start">Start feet position.</param>
    /// <param name="goal">Goal position.</param>
    /// <param name="policy">Building settings.</param>
    /// <param name="carriedBlocks">Placeable blocks carried (hotbar and backpack). ≥ 0.</param>
    /// <param name="search">Search options; their <see cref="FineSearchOptions.Build"/> is replaced. Defaults if null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The route with its budget verdict.</returns>
    public static BudgetedRoute FindPath(IBlockGrid grid, GridPos start, GridPos goal, BuildPolicy policy, int carriedBlocks, FineSearchOptions? search = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(carriedBlocks);
        policy.Validate();
        search ??= FineSearchOptions.Default;

        FineRoute first = FineAStar.FindPath(grid, start, goal, search with { Build = policy.ToMoveOptions(carriedBlocks) }, cancellationToken);
        if (first.BlocksNeeded <= policy.MaxBlocks)
        {
            return Verdict(first, policy, reran: false, carriedBlocks);
        }

        FineRoute rerun = FineAStar.FindPath(grid, start, goal, search with { Build = policy.ToMoveOptions(carriedBlocks, policy.RerunPenaltyFactor) }, cancellationToken);
        bool keepRerun = first.Found ? rerun.Found : rerun.HasRoute;
        return Verdict(keepRerun ? rerun : first, policy, reran: true, carriedBlocks);
    }

    private static BudgetedRoute Verdict(FineRoute route, BuildPolicy policy, bool reran, int carriedBlocks)
    {
        int blocks = route.BlocksNeeded;
        BuildBudgetStatus status = blocks == 0 ? BuildBudgetStatus.NoBuild
            : blocks <= policy.MaxBlocks ? BuildBudgetStatus.WithinBudget
            : BuildBudgetStatus.OverBudget;
        return new BudgetedRoute(route, status, blocks, reran, carriedBlocks);
    }
}
