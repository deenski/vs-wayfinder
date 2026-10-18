using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;

namespace Wayfinding.Pathing.Tests;

/// <summary>Grid generators and route checks shared by the KAN-26/27 search tests.</summary>
internal static class SearchTestSupport
{
    public const double Eps = 1e-6;

    /// <summary>Highest occupiable feet position in a column.</summary>
    public static GridPos Feet(IBlockGrid grid, int x, int z, int yTop = 64, int yBottom = -8)
    {
        for (int y = yTop; y >= yBottom; y--)
        {
            if (Standability.Classify(grid, new GridPos(x, y, z)).IsOccupiable())
            {
                return new GridPos(x, y, z);
            }
        }

        throw new InvalidOperationException($"No occupiable cell in column ({x}, {z}).");
    }

    /// <summary>
    /// Deterministic random heightmap: gentle hills (0-4), scattered water, lava and wall columns. With
    /// <paramref name="closed"/>, a '#' wall rings the map so nothing outside it (Unknown) is reachable.
    /// </summary>
    public static string[] RandomHeightmap(int seed, int sizeX, int sizeZ, bool closed = true, double features = 0.08)
    {
        var rng = new Random(seed);
        var h = new int[sizeX, sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int left = x > 0 ? h[x - 1, z] : 1;
                int up = z > 0 ? h[x, z - 1] : 1;
                int v = (left + up) / 2 + rng.Next(-1, 2);
                h[x, z] = Math.Clamp(v, 0, 4);
            }
        }

        var rows = new string[sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            var row = new char[sizeX];
            for (int x = 0; x < sizeX; x++)
            {
                bool edge = x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
                double r = rng.NextDouble();
                row[x] = closed && edge ? '#'
                    : r < features * 0.4 ? '#'
                    : r < features * 0.7 ? '~'
                    : r < features * 0.8 ? 'L'
                    : (char)('0' + h[x, z]);
            }

            rows[z] = new string(row);
        }

        return rows;
    }

    /// <summary>
    /// Checks a route step by step: walking steps are re-evaluated on the real grid and must be Ok at their recorded
    /// cost; the step costs add up to <see cref="FineRoute.Cost"/>; build sites match the build steps.
    /// </summary>
    public static void AssertValid(IBlockGrid grid, FineRoute route, ClimbCostModel model)
    {
        Assert.True(route.HasRoute);
        Assert.Equal(MoveKind.Start, route.Steps[0].Kind);
        double sum = 0;
        for (int i = 1; i < route.Steps.Count; i++)
        {
            RouteStep s = route.Steps[i];
            sum += s.Cost;
            if (s.Kind is MoveKind.Walk or MoveKind.Swim)
            {
                MoveCost m = model.Evaluate(grid, route.Steps[i - 1].Pos, s.Pos);
                Assert.True(m.IsOk, $"step {i} {route.Steps[i - 1].Pos} -> {s.Pos} is {m.Verdict}/{m.Reason}");
                Assert.Equal(m.Cost, s.Cost, Eps);
                Assert.Equal(m.Swim, s.Kind == MoveKind.Swim);
            }
        }

        Assert.Equal(route.Cost, sum, Eps);
        Assert.Equal(route.Steps.Sum(s => s.Blocks), route.BlocksNeeded);
        foreach (BuildSite site in route.BuildSites)
        {
            foreach (GridPos b in site.Blocks)
            {
                BlockCell cell = grid.GetCell(b);
                Assert.True(cell.Class is CellClass.Air or CellClass.Walkable or CellClass.Lava, $"block at {b} goes into {cell.Class}");
                Assert.Equal(CellFlags.None, cell.Flags & CellFlags.BuildForbidden);
            }
        }
    }

    /// <summary>A clock that moves forward by a fixed step every time it is read.</summary>
    public sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, step.Ticks);
    }

    /// <summary>A grid wrapper that runs an action after a number of reads (to cancel mid-search).</summary>
    public sealed class CountingGrid(IBlockGrid inner, long triggerAfter, Action trigger) : IBlockGrid
    {
        private long _reads;

        public long Reads => _reads;

        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z)
        {
            Tick();
            return inner.GetClass(x, y, z);
        }

        public BlockCell GetCell(int x, int y, int z)
        {
            Tick();
            return inner.GetCell(x, y, z);
        }

        private void Tick()
        {
            if (++_reads == triggerAfter)
            {
                trigger();
            }
        }
    }
}
