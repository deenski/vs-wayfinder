using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>How a <see cref="FineAStar"/> run ended.</summary>
public enum FineSearchStatus : byte
{
    /// <summary>A cheapest route to the goal was found.</summary>
    Found = 0,

    /// <summary>
    /// The goal is not reachable over known terrain, but the search reached Unknown terrain. The route runs to the
    /// best boundary position and continues as a straight <see cref="FineRoute.Unknown"/> segment to the goal.
    /// </summary>
    UnknownBoundary,

    /// <summary>Every reachable known position was expanded without reaching the goal and no Unknown was met.</summary>
    NoPath,

    /// <summary>
    /// The node budget ran out. The route runs to the frontier position nearest the goal (lowest h) and ends in
    /// an Unknown segment to the goal.
    /// </summary>
    BudgetExhausted,

    /// <summary>The time budget ran out. Partial route chosen as for <see cref="BudgetExhausted"/>.</summary>
    TimedOut,

    /// <summary>The cancellation token fired. No route.</summary>
    Cancelled,

    /// <summary>The start is not occupiable (<see cref="Standability"/>). No route.</summary>
    InvalidStart,

    /// <summary>The goal is known not to be occupiable (inside a block, mid-air, submerged, or dangerous). No route.</summary>
    InvalidGoal,
}

/// <summary>The kind of move that reached a route position.</summary>
public enum MoveKind : byte
{
    /// <summary>The first position of the route.</summary>
    Start = 0,

    /// <summary>An ordinary walking step (climb up to MaxClimb, drop up to MaxDrop).</summary>
    Walk,

    /// <summary>A step that ends with the feet in water, including an allowed drop into deep water.</summary>
    Swim,

    /// <summary>Part of a Step build move: 1 block, climb MaxClimb + 1 (climb 2 with the defaults).</summary>
    Step,

    /// <summary>Part of a Pillar build move: climb − MaxClimb blocks, 2 or more (climb − 1 with the defaults).</summary>
    Pillar,

    /// <summary>Part of a Bridge build move (one block under each gap cell).</summary>
    Bridge,
}

/// <summary>Segment styling classes the renderer uses (WF-18).</summary>
public enum SegmentKind : byte
{
    /// <summary>Walking on land.</summary>
    Walk = 0,

    /// <summary>Swimming or wading.</summary>
    Swim,

    /// <summary>Needs placed blocks (amber).</summary>
    Build,

    /// <summary>Straight line over terrain the planner could not see (grey).</summary>
    Unknown,
}

/// <summary>One position of a route and the move that reached it.</summary>
/// <param name="Pos">Feet position.</param>
/// <param name="Kind">Move that reached it (<see cref="MoveKind.Start"/> for the first).</param>
/// <param name="Cost">Cost of that move, build penalty included.</param>
/// <param name="Blocks">Blocks placed by that move.</param>
public readonly record struct RouteStep(GridPos Pos, MoveKind Kind, double Cost, int Blocks)
{
    /// <summary>The rendering class of the move.</summary>
    public SegmentKind Segment => Kind switch
    {
        MoveKind.Swim => SegmentKind.Swim,
        MoveKind.Step or MoveKind.Pillar or MoveKind.Bridge => SegmentKind.Build,
        _ => SegmentKind.Walk,
    };
}

/// <summary>Kinds of build site.</summary>
public enum BuildKind : byte
{
    /// <summary>1 block, climb MaxClimb + 1 (climb 2 with the defaults).</summary>
    Step = 0,

    /// <summary>climb − MaxClimb blocks, 2 or more (climb 3 or more with climb − 1 blocks with the defaults).</summary>
    Pillar,

    /// <summary>One block under each gap cell.</summary>
    Bridge,
}

/// <summary>One place where the route needs blocks (WF-19 callouts and ghost outlines).</summary>
/// <param name="Kind">The build move.</param>
/// <param name="From">Feet position where the move starts.</param>
/// <param name="To">Feet position where it ends.</param>
/// <param name="Blocks">Cells to place a block in, in placement order.</param>
public sealed record BuildSite(BuildKind Kind, GridPos From, GridPos To, IReadOnlyList<GridPos> Blocks)
{
    /// <summary>Number of blocks.</summary>
    public int Count => Blocks.Count;
}

/// <summary>
/// A straight segment over terrain the planner could not plan: the unplanned remainder from the last planned
/// position to the goal. Always drawn as <see cref="SegmentKind.Unknown"/>. It is not a walkability claim and,
/// after a budget or time-out, may cross known solid blocks (the search simply did not get that far).
/// </summary>
/// <param name="From">Last planned position.</param>
/// <param name="To">The goal.</param>
public readonly record struct UnknownSegment(GridPos From, GridPos To);

/// <summary>Result of a <see cref="FineAStar"/> run. Immutable.</summary>
/// <param name="Status">How the search ended.</param>
/// <param name="Cost">Cost of the planned (known) part of <see cref="Steps"/>; +∞ when there is no route.</param>
/// <param name="Steps">Planned positions from the start, each with the move that reached it; empty when there is no route.</param>
/// <param name="BuildSites">Build sites along the planned part, in route order.</param>
/// <param name="Unknown">The trailing Unknown segment for partial routes; null when <see cref="Status"/> is Found.</param>
/// <param name="Expanded">Positions expanded.</param>
/// <param name="UnknownMoves">Moves rejected because they depended on Unknown terrain.</param>
/// <remarks>
/// <see cref="Path"/>, <see cref="BlocksNeeded"/> and <see cref="TotalClimb"/> are computed once, when
/// <see cref="Steps"/> / <see cref="BuildSites"/> are set (including through <c>with</c>), so reading them
/// never allocates. Equality compares the positional members only, as before.
/// </remarks>
public sealed record FineRoute(
    FineSearchStatus Status,
    double Cost,
    IReadOnlyList<RouteStep> Steps,
    IReadOnlyList<BuildSite> BuildSites,
    UnknownSegment? Unknown,
    int Expanded,
    int UnknownMoves)
{
    private readonly IReadOnlyList<RouteStep> _steps = Steps ?? throw new ArgumentNullException(nameof(Steps));
    private readonly IReadOnlyList<BuildSite> _buildSites = BuildSites ?? throw new ArgumentNullException(nameof(BuildSites));
    private readonly Derived _derived = Derive(Steps, BuildSites);

    /// <summary>Planned positions from the start, each with the move that reached it; empty when there is no route.</summary>
    public IReadOnlyList<RouteStep> Steps
    {
        get => _steps;
        init
        {
            _steps = value ?? throw new ArgumentNullException(nameof(value));
            _derived = Derive(_steps, _buildSites);
        }
    }

    /// <summary>Build sites along the planned part, in route order.</summary>
    public IReadOnlyList<BuildSite> BuildSites
    {
        get => _buildSites;
        init
        {
            _buildSites = value ?? throw new ArgumentNullException(nameof(value));
            _derived = Derive(_steps, _buildSites);
        }
    }

    /// <summary>True when a complete route to the goal was found.</summary>
    public bool Found => Status == FineSearchStatus.Found;

    /// <summary>True when there is a route to draw: complete, or partial with an Unknown tail.</summary>
    public bool HasRoute => Steps.Count > 0;

    /// <summary>The planned feet positions. Computed once; the same instance on every read.</summary>
    public IReadOnlyList<GridPos> Path => _derived.Path;

    /// <summary>Total blocks the route needs (the HUD's "+N blocks"). Computed once.</summary>
    public int BlocksNeeded => _derived.BlocksNeeded;

    /// <summary>Sum of upward steps along the planned part. Computed once.</summary>
    public int TotalClimb => _derived.TotalClimb;

    /// <inheritdoc />
    public bool Equals(FineRoute? other) =>
        other is not null
        && Status == other.Status
        && Cost.Equals(other.Cost)
        && Equals(Steps, other.Steps)
        && Equals(BuildSites, other.BuildSites)
        && Nullable.Equals(Unknown, other.Unknown)
        && Expanded == other.Expanded
        && UnknownMoves == other.UnknownMoves;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Status, Cost, Steps, BuildSites, Unknown, Expanded, UnknownMoves);

    internal static FineRoute Empty(FineSearchStatus status, int expanded = 0, int unknownMoves = 0) =>
        new(status, double.PositiveInfinity, [], [], null, expanded, unknownMoves);

    private static Derived Derive(IReadOnlyList<RouteStep> steps, IReadOnlyList<BuildSite> sites)
    {
        if (steps is null || sites is null)
        {
            return Derived.None; // the constructor throws for the null list right after
        }

        var path = new GridPos[steps.Count];
        int climb = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            path[i] = steps[i].Pos;
            if (i > 0)
            {
                climb += Math.Max(0, steps[i].Pos.Y - steps[i - 1].Pos.Y);
            }
        }

        int blocks = 0;
        foreach (BuildSite site in sites)
        {
            blocks += site.Count;
        }

        return new Derived(Array.AsReadOnly(path), blocks, climb);
    }

    private sealed record Derived(IReadOnlyList<GridPos> Path, int BlocksNeeded, int TotalClimb)
    {
        public static readonly Derived None = new(Array.Empty<GridPos>(), 0, 0);
    }
}
