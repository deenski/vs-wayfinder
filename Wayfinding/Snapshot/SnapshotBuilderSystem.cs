using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Main-thread, time-sliced snapshot builder. One tick costs at most about
/// <see cref="TimeSlicedSnapshotFill.DefaultBudgetMilliseconds"/> ms, and that cost is reported
/// through <see cref="Stats"/> for <c>.nav stats</c>.
/// </summary>
/// <remarks>
/// This system does not register a chat command. <c>.nav</c> belongs to whoever calls
/// <c>GetOrCreate("nav")</c>; this class only exposes <see cref="LastSnapshotMilliseconds"/>.
/// </remarks>
public sealed class SnapshotBuilderSystem : ModSystem
{
    /// <summary>Game-tick interval. The listener runs on the main thread.</summary>
    public const int TickIntervalMs = 20;

    private readonly SnapshotStatsSink stats = new();
    private ICoreAPI? api;
    private MainThreadGuard? guard;
    private long listenerId;
    private bool listening;
    private Job? job;
    private bool smokeStarted;
    private int smokeTicks;

    /// <summary>Per-frame snapshot cost. Forward this to <c>PlanMetrics.RecordSnapshotMs</c> when KAN-18 is on main.</summary>
    public SnapshotStatsSink Stats => stats;

    /// <summary>Milliseconds of the latest slice, for <c>.nav stats</c>. Null until the first slice.</summary>
    public double? LastSnapshotMilliseconds => stats.LastMilliseconds;

    /// <summary>The snapshot produced by the last finished <see cref="Begin"/>, or null.</summary>
    public BlockGridSnapshot? Latest { get; private set; }

    /// <summary>True while a build still has chunks left.</summary>
    public bool IsBuilding => job != null;

    /// <inheritdoc />
    public override void Start(ICoreAPI api)
    {
        this.api = api;
        guard = new MainThreadGuard();
        listenerId = api.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
        listening = true;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (listening && api != null)
        {
            api.Event.UnregisterGameTickListener(listenerId);
            listening = false;
        }

        job?.Terrain.CloseChunk();
        job = null;
        base.Dispose();
    }

    /// <summary>
    /// Starts a snapshot of an in-dimension box. Replaces a build that is already running.
    /// Call this on the main thread.
    /// </summary>
    /// <param name="origin">Minimum corner. Y is in-dimension.</param>
    /// <param name="sizeX">Cells along X.</param>
    /// <param name="sizeY">Cells along Y.</param>
    /// <param name="sizeZ">Cells along Z.</param>
    /// <param name="dimension">Game dimension id.</param>
    public void Begin(GridPos origin, int sizeX, int sizeY, int sizeZ, int dimension)
    {
        if (api?.World == null || guard == null)
        {
            throw new InvalidOperationException("Snapshot builder is not started.");
        }

        guard.AssertMainThread();
        job?.Terrain.CloseChunk();
        int chunkSize = GlobalConstants.ChunkSize;
        if (chunkSize <= 0)
        {
            chunkSize = 32;
        }

        var builder = new BlockGridSnapshotBuilder(origin, sizeX, sizeY, sizeZ, dimension);
        var terrain = new GameTerrainSlice(api.World, dimension, chunkSize, guard);
        job = new Job(builder, new TimeSlicedSnapshotFill(builder, chunkSize), terrain);
        Latest = null;
    }

    private void OnTick(float dt)
    {
        MaybeSmoke();
        if (job == null || guard == null)
        {
            return;
        }

        guard.AssertMainThread();
        SnapshotSlice slice = job.Fill.Advance(job.Terrain, TimeSlicedSnapshotFill.DefaultBudget, stats);
        if (!slice.Completed)
        {
            return;
        }

        Latest = job.Builder.Build();
        job = null;
    }

    private void MaybeSmoke()
    {
        if (smokeStarted || job != null || api?.World?.BlockAccessor == null || guard == null)
        {
            return;
        }

        if (api.World.Blocks == null || api.World.Blocks.Count == 0 || api.World.DefaultSpawnPosition == null)
        {
            return;
        }

        smokeTicks++;
        if (smokeTicks < 5)
        {
            return;
        }

        smokeStarted = true;
        BlockPos spawn = api.World.DefaultSpawnPosition.AsBlockPos;
        Begin(new GridPos(spawn.X, spawn.Y, spawn.Z), 4, 4, 4, spawn.dimension);
    }

    private sealed class Job(BlockGridSnapshotBuilder builder, TimeSlicedSnapshotFill fill, GameTerrainSlice terrain)
    {
        public BlockGridSnapshotBuilder Builder { get; } = builder;

        public TimeSlicedSnapshotFill Fill { get; } = fill;

        public GameTerrainSlice Terrain { get; } = terrain;
    }
}
