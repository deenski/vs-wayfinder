using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>A corner of a simplified route.</summary>
/// <param name="Pos">Feet position.</param>
/// <param name="Segment">Kind of the segment that ends here (the first point carries the kind of the first segment).</param>
public readonly record struct RoutePoint(GridPos Pos, SegmentKind Segment);

/// <summary>
/// Grid-aware Douglas–Peucker simplification of a <see cref="FineRoute"/> for drawing (WF-12). A shortcut is
/// only taken where a player could actually walk it, so the drawn line never crosses blocks, lava or hazards.
/// </summary>
/// <remarks>
/// <para><b>What is simplified.</b> Only Known runs (<see cref="SegmentKind.Walk"/> and
/// <see cref="SegmentKind.Swim"/>). Points are never merged across a change of <see cref="SegmentKind"/>. Every
/// point of a <see cref="SegmentKind.Build"/> run is kept, so every build site keeps its exact cells. The
/// trailing <see cref="SegmentKind.Unknown"/> segment stays one straight line to the goal.</para>
/// <para><b>Climbs and drops.</b> Every point where Y changes is kept, along with its neighbour on the other
/// side of the change. Each 1-block climb or 3-block drop stays an explicit segment, and shortcuts only
/// happen at a constant Y.</para>
/// <para><b>Walkability.</b> A shortcut from kept point <c>a</c> to <c>b</c> (same Y) is accepted only if both of
/// these hold. Every dropped point lies within the tolerance of the chord. And every column the player's body
/// touches while moving along the chord is occupiable at that Y with the run's footing. The body is a square of
/// half-width <see cref="PlayerHalfWidth"/> around the block centre, swept along the chord. Footing is
/// <see cref="StandKind.Ground"/> for walk runs and Ground or <see cref="StandKind.Swim"/> for swim runs; the
/// check uses <see cref="Standability.Classify"/> with the model's headroom. That rejects solid, lava, hazard,
/// Unknown, missing support and low ceilings. The endpoints' own columns are exempt because they are route
/// positions already. Otherwise the run is split at the worst point, as in plain Douglas–Peucker. Neighbouring
/// route points are always accepted, because they are moves the search validated.</para>
/// <para>Reads the grid only and keeps no state, so it is safe to call from planner threads.</para>
/// </remarks>
public static class RouteSimplifier
{
    /// <summary>Half the player's width in blocks (the Vintage Story player is 0.6 blocks wide).</summary>
    public const double PlayerHalfWidth = 0.3;

    /// <summary>Simplifies the route's polyline against the grid it was planned on.</summary>
    /// <param name="route">The route.</param>
    /// <param name="grid">The terrain the route was planned on (the same snapshot).</param>
    /// <param name="model">The cost model the route was planned with (for headroom); default model if null.</param>
    /// <param name="tolerance">Largest allowed 3D distance, in blocks, of a dropped point from the kept line. ≥ 0.</param>
    /// <returns>Corners from start to goal (the goal last when the route has an Unknown tail); empty for no route.</returns>
    public static IReadOnlyList<RoutePoint> Simplify(FineRoute route, IBlockGrid grid, ClimbCostModel? model = null, double tolerance = 0.5)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(grid);
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "tolerance must be a finite number ≥ 0.");
        }

        int headroom = (model ?? new ClimbCostModel()).Options.HeadroomCells;
        IReadOnlyList<RouteStep> steps = route.Steps;
        if (steps.Count == 0)
        {
            return [];
        }

        var points = new List<GridPos>(steps.Count + 1);
        var kinds = new List<SegmentKind>(steps.Count + 1);
        foreach (RouteStep s in steps)
        {
            points.Add(s.Pos);
            kinds.Add(s.Segment);
        }

        if (route.Unknown is { } u)
        {
            points.Add(u.To);
            kinds.Add(SegmentKind.Unknown);
        }

        if (points.Count == 1)
        {
            return [new RoutePoint(points[0], SegmentKind.Walk)];
        }

        kinds[0] = kinds[1];
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        int runStart = 0;
        for (int i = 1; i < points.Count; i++)
        {
            bool runEnds = i == points.Count - 1 || kinds[i + 1] != kinds[i];
            if (!runEnds)
            {
                continue;
            }

            keep[runStart] = keep[i] = true;
            switch (kinds[i])
            {
                case SegmentKind.Walk or SegmentKind.Swim:
                    SimplifyKnownRun(grid, headroom, points, runStart, i, kinds[i], tolerance, keep);
                    break;
                default:
                    // Build: every point (exact build cells). Unknown: the straight tail, already only its two ends.
                    for (int k = runStart; k <= i; k++)
                    {
                        keep[k] = true;
                    }

                    break;
            }

            runStart = i;
        }

        var result = new List<RoutePoint>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(new RoutePoint(points[i], kinds[i]));
            }
        }

        return result;
    }

    /// <summary>
    /// True when the player can walk the straight line from <paramref name="a"/> to <paramref name="b"/> at one Y:
    /// every column the swept body touches (other than the endpoints' own) is occupiable with the given footing.
    /// Public so tests and debug overlays apply exactly the simplifier's rule.
    /// </summary>
    /// <param name="grid">The terrain.</param>
    /// <param name="a">One end (feet position).</param>
    /// <param name="b">Other end; must have the same Y as <paramref name="a"/>, otherwise the result is false.</param>
    /// <param name="segment">The footing: <see cref="SegmentKind.Walk"/> (Ground only) or <see cref="SegmentKind.Swim"/> (Ground or Swim).</param>
    /// <param name="headroomCells">Body height in cells.</param>
    /// <returns>Whether the chord is walkable.</returns>
    public static bool IsChordWalkable(IBlockGrid grid, GridPos a, GridPos b, SegmentKind segment, int headroomCells = Standability.DefaultHeadroomCells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (a.Y != b.Y || segment is not (SegmentKind.Walk or SegmentKind.Swim))
        {
            return false;
        }

        double ax = a.X + 0.5, az = a.Z + 0.5, bx = b.X + 0.5, bz = b.Z + 0.5;
        const double w = PlayerHalfWidth;
        int x0 = (int)Math.Floor(Math.Min(ax, bx) - w), x1 = (int)Math.Floor(Math.Max(ax, bx) + w);
        int z0 = (int)Math.Floor(Math.Min(az, bz) - w), z1 = (int)Math.Floor(Math.Max(az, bz) + w);
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                if ((x == a.X && z == a.Z) || (x == b.X && z == b.Z))
                {
                    continue;
                }

                // The body square (half-width w) touches cell [x, x+1] x [z, z+1] iff the chord meets the cell grown by w.
                if (!SegmentTouchesBox(ax, az, bx, bz, x - w, z - w, x + 1 + w, z + 1 + w))
                {
                    continue;
                }

                StandKind stand = Standability.Classify(grid, new GridPos(x, a.Y, z), headroomCells);
                bool ok = stand == StandKind.Ground || (segment == SegmentKind.Swim && stand == StandKind.Swim);
                if (!ok)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Splits a Known run at every Y change, then simplifies each constant-Y piece.</summary>
    private static void SimplifyKnownRun(IBlockGrid grid, int headroom, List<GridPos> p, int first, int last, SegmentKind kind, double tolerance, bool[] keep)
    {
        int pieceStart = first;
        for (int i = first; i < last; i++)
        {
            if (p[i].Y != p[i + 1].Y)
            {
                keep[i] = keep[i + 1] = true; // both ends of a climb or drop
                Mark(grid, headroom, p, pieceStart, i, kind, tolerance, keep);
                pieceStart = i + 1;
            }
        }

        Mark(grid, headroom, p, pieceStart, last, kind, tolerance, keep);
    }

    private static void Mark(IBlockGrid grid, int headroom, List<GridPos> p, int first, int last, SegmentKind kind, double tolerance, bool[] keep)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((first, last));
        while (stack.Count > 0)
        {
            (int a, int b) = stack.Pop();
            if (b - a < 2)
            {
                continue; // neighbouring route points: a move the search validated
            }

            double worst = -1;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = Distance(p[i], p[a], p[b]);
                if (d > worst)
                {
                    worst = d;
                    at = i;
                }
            }

            if (worst > tolerance || !IsChordWalkable(grid, p[a], p[b], kind, headroom))
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
    }

    /// <summary>Slab test: does the segment (ax, az)–(bx, bz) meet the closed box [minX, maxX] × [minZ, maxZ]?</summary>
    private static bool SegmentTouchesBox(double ax, double az, double bx, double bz, double minX, double minZ, double maxX, double maxZ)
    {
        double t0 = 0, t1 = 1;
        return Clip(bx - ax, ax, minX, maxX, ref t0, ref t1) && Clip(bz - az, az, minZ, maxZ, ref t0, ref t1);
    }

    private static bool Clip(double d, double start, double min, double max, ref double t0, ref double t1)
    {
        if (d == 0)
        {
            return start >= min && start <= max;
        }

        double ta = (min - start) / d, tb = (max - start) / d;
        if (ta > tb)
        {
            (ta, tb) = (tb, ta);
        }

        t0 = Math.Max(t0, ta);
        t1 = Math.Min(t1, tb);
        return t0 <= t1;
    }

    /// <summary>3D distance from <paramref name="p"/> to the segment a–b.</summary>
    private static double Distance(GridPos p, GridPos a, GridPos b)
    {
        double abx = (double)b.X - a.X, aby = (double)b.Y - a.Y, abz = (double)b.Z - a.Z;
        double apx = (double)p.X - a.X, apy = (double)p.Y - a.Y, apz = (double)p.Z - a.Z;
        double len2 = abx * abx + aby * aby + abz * abz;
        double t = len2 == 0 ? 0 : Math.Clamp((apx * abx + apy * aby + apz * abz) / len2, 0, 1);
        double dx = apx - t * abx, dy = apy - t * aby, dz = apz - t * abz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
