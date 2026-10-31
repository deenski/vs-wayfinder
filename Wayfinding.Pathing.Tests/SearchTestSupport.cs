using Wayfinding.Pathing.Cost;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Search;
using Wayfinding.Pathing.Testing;

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

    /// <summary>Every occupiable feet position of a heightmap built with <see cref="FakeGrid.FromHeightmap"/>.</summary>
    public static GridPos[] StandableCells(IBlockGrid grid, string[] rows)
    {
        var result = new List<GridPos>();
        for (int z = 0; z < rows.Length; z++)
        {
            for (int x = 0; x < rows[z].Length; x++)
            {
                if (FakeGrid.TryParseHeight(rows[z][x], out int h) || rows[z][x] == '~')
                {
                    var p = new GridPos(x, rows[z][x] == '~' ? 1 : h + 1, z);
                    if (Standability.Classify(grid, p).IsOccupiable())
                    {
                        result.Add(p);
                    }
                }
            }
        }

        return result.ToArray();
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
    /// Checks a route step by step. Walking steps are re-evaluated on the real grid and must be Ok at their
    /// recorded cost. Build legs (Step, Pillar, Bridge) are re-evaluated on the real grid plus that build site's
    /// own blocks only: horizontal legs through <see cref="ClimbCostModel.Evaluate"/>, and a pillar's vertical leg
    /// as an occupiable pillar top at <c>(UphillPenalty + buildPenalty) × blocks</c>. The step costs must add up to
    /// <see cref="FineRoute.Cost"/>, and build sites must match the build steps.
    /// </summary>
    /// <param name="grid">The real terrain.</param>
    /// <param name="route">The route.</param>
    /// <param name="model">Its cost model.</param>
    /// <param name="buildPenalty">Its per-block penalty.</param>
    /// <param name="cumulative">
    /// Check build legs against every block placed so far (all earlier sites plus this one) instead of the site's own
    /// blocks only. Use it for <see cref="BuildPlanner"/> routes, whose later sites may omit a cell an earlier site
    /// already filled (KAN-28 round 2). Pillar cells are checked cell by cell against the blocks placed so far, and the
    /// route must place each cell once. Bridge legs check the step's cost against its own block count only.
    /// </param>
    public static void AssertValid(IBlockGrid grid, FineRoute route, ClimbCostModel model, double buildPenalty = 24, bool cumulative = false)
    {
        var allPlaced = new List<GridPos>();
        Assert.True(route.HasRoute);
        Assert.Equal(MoveKind.Start, route.Steps[0].Kind);
        double sum = 0;
        int site = 0;
        IBlockGrid legGrid = grid;
        for (int i = 1; i < route.Steps.Count; i++)
        {
            RouteStep s = route.Steps[i];
            GridPos from = route.Steps[i - 1].Pos;
            sum += s.Cost;
            if (s.Kind is MoveKind.Walk or MoveKind.Swim)
            {
                MoveCost m = model.Evaluate(grid, from, s.Pos);
                Assert.True(m.IsOk, $"step {i} {from} -> {s.Pos} is {m.Verdict}/{m.Reason}");
                Assert.Equal(m.Cost, s.Cost, Eps);
                Assert.Equal(m.Swim, s.Kind == MoveKind.Swim);
                continue;
            }

            // A build leg. A new site starts at a non-build position or where the previous site ended.
            bool afterBuild = route.Steps[i - 1].Kind is MoveKind.Step or MoveKind.Pillar or MoveKind.Bridge;
            if (!afterBuild || (site > 0 && from == route.BuildSites[site - 1].To && site < route.BuildSites.Count && from == route.BuildSites[site].From))
            {
                Assert.True(site < route.BuildSites.Count, $"build step {i} has no build site");
                Assert.Equal(from, route.BuildSites[site].From);
                allPlaced.AddRange(route.BuildSites[site].Blocks);
                legGrid = new OverlayGrid(grid, cumulative ? allPlaced.ToArray() : route.BuildSites[site].Blocks);
                site++;
            }

            if (from.X == s.Pos.X && from.Z == s.Pos.Z)
            {
                int blocks = s.Pos.Y - from.Y;
                if (cumulative)
                {
                    // Every pillar cell is filled by this site or an earlier one, and the step pays for exactly the
                    // cells this site places (the others are repeats DedupePlacements dropped).
                    IReadOnlyList<GridPos> siteBlocks = route.BuildSites[site - 1].Blocks;
                    int own = 0;
                    for (int y = from.Y; y < s.Pos.Y; y++)
                    {
                        var cell = new GridPos(from.X, y, from.Z);
                        Assert.True(allPlaced.Contains(cell), $"pillar cell {cell} of step {i} is never placed");
                        own += siteBlocks.Contains(cell) ? 1 : 0;
                    }

                    Assert.Equal(own, s.Blocks);
                }
                else
                {
                    Assert.Equal(blocks, s.Blocks);
                }

                Assert.True(Standability.Classify(legGrid, s.Pos, model.Options.HeadroomCells).IsOccupiable(), $"pillar top {s.Pos} is not occupiable");
                Assert.Equal(model.Options.UphillPenalty * blocks + buildPenalty * s.Blocks, s.Cost, Eps);
            }
            else
            {
                MoveCost m = model.Evaluate(legGrid, from, s.Pos);
                Assert.True(m.IsOk, $"build leg {i} {from} -> {s.Pos} is {m.Verdict}/{m.Reason}");
                Assert.Equal(m.Cost + buildPenalty * s.Blocks, s.Cost, Eps);
            }
        }

        Assert.Equal(route.BuildSites.Count, site);
        Assert.Equal(route.Cost, sum, Eps);
        Assert.Equal(route.Steps.Sum(s => s.Blocks), route.BlocksNeeded);
        if (cumulative)
        {
            // Steps pay for each placed cell exactly once: no cell repeats across sites, and every site block is paid for.
            Assert.Equal(allPlaced.Count, allPlaced.Distinct().Count());
            Assert.Equal(allPlaced.Count, route.BlocksNeeded);
        }
        foreach (BuildSite b in route.BuildSites)
        {
            foreach (GridPos pos in b.Blocks)
            {
                BlockCell cell = grid.GetCell(pos);
                Assert.True(cell.Class is CellClass.Air or CellClass.Walkable or CellClass.Lava, $"block at {pos} goes into {cell.Class}");
                Assert.Equal(CellFlags.None, cell.Flags & CellFlags.BuildForbidden);
            }
        }
    }

    /// <summary>A grid with some hypothetical blocks placed (they read as Solid).</summary>
    public sealed class OverlayGrid(IBlockGrid inner, IReadOnlyCollection<GridPos> placed) : IBlockGrid
    {
        private readonly HashSet<GridPos> _placed = [.. placed];

        public int Dimension => inner.Dimension;

        public CellClass GetClass(int x, int y, int z) => _placed.Contains(new GridPos(x, y, z)) ? CellClass.Solid : inner.GetClass(x, y, z);

        public BlockCell GetCell(int x, int y, int z) => _placed.Contains(new GridPos(x, y, z)) ? new BlockCell(CellClass.Solid) : inner.GetCell(x, y, z);
    }

    /// <summary>A clock that moves forward by a fixed step every time it is read.</summary>
    public sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, step.Ticks);
    }

    /// <summary>
    /// A grid wrapper for cross-thread cancellation: after a number of reads it signals <see cref="Reached"/> and
    /// blocks the search thread until <paramref name="token"/> is cancelled (by another thread), then carries on.
    /// </summary>
    public sealed class GateGrid(IBlockGrid inner, long gateAfter, CancellationToken token) : IBlockGrid
    {
        private long _reads;

        public ManualResetEventSlim Reached { get; } = new();

        public int SearchThreadId { get; private set; }

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
            if (++_reads == gateAfter)
            {
                SearchThreadId = Environment.CurrentManagedThreadId;
                Reached.Set();
                Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)), "nobody cancelled the search");
            }
        }
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
