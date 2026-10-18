using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Search;

/// <summary>A corner of a simplified route.</summary>
/// <param name="Pos">Feet position.</param>
/// <param name="Segment">Kind of the segment that ends here (the first point carries the kind of the first segment).</param>
public readonly record struct RoutePoint(GridPos Pos, SegmentKind Segment);

/// <summary>
/// Douglas–Peucker simplification of a <see cref="FineRoute"/> for drawing (WF-12). Never merges across a change
/// of <see cref="SegmentKind"/>, never removes a point of a build segment (so every build site keeps its exact
/// cells), and keeps the trailing Unknown segment as one straight line.
/// </summary>
public static class RouteSimplifier
{
    /// <summary>Simplifies the route's polyline.</summary>
    /// <param name="route">The route.</param>
    /// <param name="tolerance">Largest allowed 3D distance, in blocks, of a dropped point from the kept line. ≥ 0.</param>
    /// <returns>Corners from start to goal (the goal last when the route has an Unknown tail); empty for no route.</returns>
    public static IReadOnlyList<RoutePoint> Simplify(FineRoute route, double tolerance = 0.5)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "tolerance must be a finite number ≥ 0.");
        }

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
            if (kinds[i] == SegmentKind.Build)
            {
                for (int k = runStart; k <= i; k++)
                {
                    keep[k] = true;
                }
            }
            else
            {
                Mark(points, runStart, i, tolerance, keep);
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

    private static void Mark(List<GridPos> p, int first, int last, double tolerance, bool[] keep)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((first, last));
        while (stack.Count > 0)
        {
            (int a, int b) = stack.Pop();
            if (b - a < 2)
            {
                continue;
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

            if (worst > tolerance)
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
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
