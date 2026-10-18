using System;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Wayfinding.Infra;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Reads one chunk at a time from the live game on the main thread and classifies cells with
/// <see cref="CellClassifier"/>.
/// </summary>
/// <remarks>
/// Per chunk, in order: chunk-loaded check, <c>Unpack_ReadOnly</c>, <c>GetBlockOrNull</c> on the
/// solid layer, one <c>AcquireBlockReadLock</c>, <c>GetBlockIdUnsafe(index, layer)</c> for the fluid
/// and solid layers, then <c>ReleaseBlockReadLock</c> in a finally.
/// <c>GetBlockOrNull</c> reads through the locking <c>Get</c>, which takes the same read lock, so it
/// runs after unpack and before the lock. It is not called again per cell.
/// <c>Unpack()</c>, <c>GetCachingBlockAccessor</c>, <c>WalkBlocks</c>, and the most-solid layer are
/// not used. Most-solid looks up <c>Blocks[id].SideSolid</c> and throws when that slot is null.
/// <c>Data</c> is null while the chunk is compressed; a chunk that stays null after
/// <c>Unpack_ReadOnly</c> is unknown and is not locked.
/// A null <c>GetBlockOrNull</c> is unknown, never air.
/// <c>GetBlockOrNull</c> itself returns null for every negative coordinate and returns air for a
/// disposed chunk that is still in the map, so the loaded check (null, <c>Disposed</c>, client
/// <c>LoadedFromServer</c>) is applied first. An in-dimension Y outside <c>[0, 32768)</c> is unknown.
/// Negative X or Z still trust the loaded check, because the game returns null there before it looks.
/// </remarks>
internal sealed class GameTerrainSlice : ITerrainSlice, IChunkReader
{
    private readonly IBlockAccessor blocks;
    private readonly IWorldAccessor world;
    private readonly IPlayer? player;
    private readonly int dimension;
    private readonly MainThreadGuard guard;
    private readonly IChunkReadLog? log;
    private readonly BlockPos pos;
    private Action<string>? missingMethodLog;
    private IWorldChunk? chunk;
    private bool open;
    private int openChunkX;
    private int openChunkY;
    private int openChunkZ;

    public GameTerrainSlice(IWorldAccessor world, int dimension, int chunkSize, MainThreadGuard guard, IChunkReadLog? log = null)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        blocks = world.BlockAccessor ?? throw new ArgumentException("The world has no block accessor.", nameof(world));
        this.dimension = dimension;
        ChunkSize = chunkSize;
        this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
        this.log = log;
        pos = new BlockPos(dimension);
        player = (world.Api as ICoreClientAPI)?.World?.Player;
        if (player?.Entity == null)
        {
            player = null;
        }
    }

    public int ChunkSize { get; }

    public bool TryOpenChunk(int chunkX, int chunkY, int chunkZ)
    {
        guard.AssertMainThread();
        if (open)
        {
            CloseChunk();
        }

        // A negative chunk Y, or one at Boundary/ChunkSize and above, is the neighbouring dimension.
        // Dim 2 chunkY -1 would be game chunk 2047, which is dim 1's top chunk. Leave it unknown.
        if (!DimensionY.ContainsChunkY(chunkY, ChunkSize))
        {
            return false;
        }

        int gameChunkY = chunkY + dimension * (DimensionY.Boundary / ChunkSize);
        guard.AssertMainThread();
        IWorldChunk? found = blocks.GetChunk(chunkX, gameChunkY, chunkZ);
        if (!IsLoaded(found))
        {
            return false;
        }

        chunk = found;
        openChunkX = chunkX;
        openChunkY = chunkY;
        openChunkZ = chunkZ;
        if (!ChunkReadProtocol.TryOpen(this, log, () => ConfirmWithGetBlockOrNull(chunkX, chunkY, chunkZ)))
        {
            chunk = null;
            return false;
        }

        open = true;
        return true;
    }

    public void CloseChunk()
    {
        if (!open)
        {
            return;
        }

        open = false;
        ChunkReadProtocol.Close(this, log);
        chunk = null;
    }

    public bool UnpackReadOnly()
    {
        guard.AssertMainThread();
        IWorldChunk? current = chunk;
        if (current == null || current.Disposed)
        {
            return false;
        }

        // Data is null while compressed. Unpack() would set PotentialBlockOrLightingChanges.
        current.Unpack_ReadOnly();
        guard.AssertMainThread();
        return current.Data != null && !current.Disposed;
    }

    public void AcquireLock()
    {
        guard.AssertMainThread();
        chunk!.AcquireBlockReadLock();
    }

    public void ReleaseLock()
    {
        guard.AssertMainThread();
        chunk?.ReleaseBlockReadLock();
    }

    public bool TryReadCell(int x, int y, int z, out BlockCell cell)
    {
        guard.AssertMainThread();
        cell = default;
        if (!DimensionY.ContainsInDimensionY(y))
        {
            return false;
        }

        ChunkReadProtocol.Read(log);
        IWorldChunk? current = chunk;
        if (!open || current == null || current.Disposed || current.Data == null)
        {
            return false;
        }

        if (!DimensionY.TryToInternal(dimension, y, out _))
        {
            return false;
        }

        int localX = x - openChunkX * ChunkSize;
        int localY = y - openChunkY * ChunkSize;
        int localZ = z - openChunkZ * ChunkSize;
        if ((uint)localX >= (uint)ChunkSize || (uint)localY >= (uint)ChunkSize || (uint)localZ >= (uint)ChunkSize)
        {
            return false;
        }

        int index = (localY * ChunkSize + localZ) * ChunkSize + localX;
        guard.AssertMainThread();
        if (!LayeredBlockIds.TryRead(current.Data, index, BlockLayersAccess.Fluid, MissingMethodLog, out int fluidId)
            || !LayeredBlockIds.TryRead(current.Data, index, BlockLayersAccess.Solid, MissingMethodLog, out int solidId))
        {
            return false;
        }

        Place(x, y, z);
        // Ice sits in the fluid layer and is not a liquid. A solid-layer id wins when both are set.
        if (!TryReadSample(fluidId, solidId != 0 ? solidId : fluidId, out CellSample sample))
        {
            return false;
        }

        if (!CellClassifier.TryClassify(sample, out cell))
        {
            return false;
        }

        ApplyClaim(ref cell);
        return true;
    }

    public bool IsLoaded(IWorldChunk? candidate)
    {
        guard.AssertMainThread();
        if (candidate == null || candidate.Disposed)
        {
            return false;
        }

        if (candidate is IClientChunk client && !client.LoadedFromServer)
        {
            return false;
        }

        return true;
    }

    private bool ConfirmWithGetBlockOrNull(int chunkX, int chunkY, int chunkZ)
    {
        // (uint)chunkY >= Boundary/ChunkSize, including negatives, is the neighbouring dimension.
        if (!DimensionY.ContainsChunkY(chunkY, ChunkSize))
        {
            return false;
        }

        int x = chunkX * ChunkSize;
        int y = chunkY * ChunkSize;
        int z = chunkZ * ChunkSize;
        if (!DimensionY.ContainsInDimensionY(y) || !DimensionY.TryToInternal(dimension, y, out int internalY))
        {
            return false;
        }

        // GetBlockOrNull returns null for every negative X or Z, before it looks at the chunk.
        // Y is already inside [0, Boundary), so a negative internal Y here is a negative dimension.
        if ((x | internalY | z) < 0)
        {
            return true;
        }

        guard.AssertMainThread();
        return blocks.GetBlockOrNull(x, internalY, z, BlockLayersAccess.Solid) != null;
    }

    private Action<string> MissingMethodLog => missingMethodLog ??= message =>
    {
        world.Api.ModLoader.GetModSystem<WayfindingModSystem>().Log.Log(LogLevel.Error, message);
    };

    private bool TryReadSample(int fluidId, int blockId, out CellSample sample)
    {
        Block? fluid = BlockAt(fluidId);
        if (fluidId != 0 && fluid == null)
        {
            sample = default;
            return false;
        }

        if (fluid != null && fluid.Id != 0 && fluid.IsLiquid())
        {
            string code = fluid.LiquidCode ?? string.Empty;
            ObservedFluid kind = code.Contains("lava", StringComparison.OrdinalIgnoreCase)
                ? ObservedFluid.Lava
                : ObservedFluid.Water;
            guard.AssertMainThread();
            float cost = fluid.GetTraversalCost(pos, EnumAICreatureType.Humanoid);
            sample = new CellSample(true, kind, fluid.Id, cost, HasCollision: false);
            return true;
        }

        Block? block = BlockAt(blockId);
        if (blockId != 0 && block == null)
        {
            sample = default;
            return false;
        }

        if (block == null || block.Id == 0)
        {
            sample = new CellSample(true, ObservedFluid.None, 0, 0, HasCollision: false);
            return true;
        }

        guard.AssertMainThread();
        float traversal = block.GetTraversalCost(pos, EnumAICreatureType.Humanoid);
        guard.AssertMainThread();
        bool collision = TryMaxCollisionY2(block.GetCollisionBoxes(blocks, pos), out float maxY2);
        sample = new CellSample(
            true,
            ObservedFluid.None,
            block.Id,
            traversal,
            collision,
            collision ? maxY2 : null);
        return true;
    }

    private void ApplyClaim(ref BlockCell cell)
    {
        if (player == null || world.Claims == null)
        {
            return;
        }

        // The vsapi comment on TestAccess says the client always returns Granted. That comment
        // is stale in 1.22.7: the client evaluates land claims. Protections other mods add
        // through the server-only access events still read as Granted here.
        guard.AssertMainThread();
        EnumWorldAccessResponse access = world.Claims.TestAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak);
        if (access != EnumWorldAccessResponse.Granted)
        {
            cell = cell with { Flags = cell.Flags | CellFlags.BuildForbidden };
        }
    }

    private Block? BlockAt(int id)
    {
        guard.AssertMainThread();
        if (id == 0)
        {
            return null;
        }

        var list = world.Blocks;
        if (list == null || (uint)id >= (uint)list.Count)
        {
            return null;
        }

        return list[id];
    }

    private void Place(int x, int y, int z)
    {
        pos.SetDimension(dimension);
        pos.Set(x, y, z);
#if DEBUG
        if (DimensionY.TryToInternal(dimension, y, out int expected))
        {
            Debug.Assert(pos.InternalY == expected, "BlockPos.InternalY drifted from dim * 32768.");
        }
#endif
    }

    private static bool TryMaxCollisionY2(Cuboidf[]? boxes, out float maxY2)
    {
        maxY2 = 0;
        if (boxes == null || boxes.Length == 0)
        {
            return false;
        }

        bool any = false;
        float max = float.NegativeInfinity;
        foreach (Cuboidf box in boxes)
        {
            if (box == null)
            {
                continue;
            }

            any = true;
            if (box.Y2 > max)
            {
                max = box.Y2;
            }
        }

        if (!any)
        {
            return false;
        }

        maxY2 = max;
        return true;
    }
}
