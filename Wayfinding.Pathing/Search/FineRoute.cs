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

    /// <summary>The node budget ran out. The route is the best partial route plus an Unknown segment.</summary>
    BudgetExhausted,

    /// <summary>The time budget ran out. The route is the best partial route plus an Unknown segment.</summary>
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

    /// <summary>A step that ends with the feet in water.</summary>
    Swim,

    /// <summary>Part of a Step build move (climb 2 with 1 block).</summary>
    Step,

    /// <summary>Part of a Pillar build move (climb 3 or more, climb − 1 blocks).</summary>
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
    /// <summary>Climb 2 with 1 block.</summary>
    Step = 0,

    /// <summary>Climb 3 or more with climb − 1 blocks.</summary>
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

/// <summary>A straight segment over terrain the planner could not plan.</summary>
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
public sealed record FineRoute(
    FineSearchStatus Status,
    double Cost,
    IReadOnlyList<RouteStep> Steps,
    IReadOnlyList<BuildSite> BuildSites,
    UnknownSegment? Unknown,
    int Expanded,
    int UnknownMoves)
{
    /// <summary>True when a complete route to the goal was found.</summary>
    public bool Found => Status == FineSearchStatus.Found;

    /// <summary>True when there is a route to draw: complete, or partial with an Unknown tail.</summary>
    public bool HasRoute => Steps.Count > 0;

    /// <summary>The planned feet positions.</summary>
    public IReadOnlyList<GridPos> Path => Steps.Select(s => s.Pos).ToArray();

    /// <summary>Total blocks the route needs (the HUD's "+N blocks").</summary>
    public int BlocksNeeded => BuildSites.Sum(b => b.Count);

    /// <summary>Sum of upward steps along the planned part.</summary>
    public int TotalClimb
    {
        get
        {
            int climb = 0;
            for (int i = 1; i < Steps.Count; i++)
            {
                climb += Math.Max(0, Steps[i].Pos.Y - Steps[i - 1].Pos.Y);
            }

            return climb;
        }
    }

    internal static FineRoute Empty(FineSearchStatus status, int expanded = 0, int unknownMoves = 0) =>
        new(status, double.PositiveInfinity, [], [], null, expanded, unknownMoves);
}
