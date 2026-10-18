using System.Collections.Concurrent;
using Wayfinding.Pathing.Coarse;

namespace Wayfinding.Pathing.Cache;

/// <summary>Why <see cref="HeightCache.Changed"/> fired.</summary>
public enum HeightCacheChange : byte
{
    /// <summary>A chunk column was revealed for the first time.</summary>
    Added = 0,

    /// <summary>A chunk column was captured again and differs from the stored one.</summary>
    Updated,
}

/// <summary>Arguments of <see cref="HeightCache.Changed"/>.</summary>
/// <param name="ChunkX">Chunk X.</param>
/// <param name="ChunkZ">Chunk Z.</param>
/// <param name="Change">Added or Updated.</param>
public sealed record HeightCacheChangedEventArgs(int ChunkX, int ChunkZ, HeightCacheChange Change);

/// <summary>
/// The revealed-terrain cache (WF-16, design v2 §3): one <see cref="HeightChunk"/> per chunk column the player has
/// loaded, readable by the coarse planner as an <see cref="IHeightGrid"/>. Columns of chunk columns never
/// captured are Unknown.
/// </summary>
/// <remarks>
/// <para>Thread-safe: the game thread stores captures while planner threads read. Chunks are immutable and
/// replaced whole, so a reader sees either the old or the new chunk column, never a mix. <see cref="Reset"/>
/// swaps the whole map in one publication, so a reader never observes a half-cleared load.</para>
/// <para><see cref="Changed"/> fires on the storing thread, after the store, only when the data changed (new, or
/// different from what was stored). Re-capturing an identical chunk column fires nothing. Loading a file
/// (<see cref="Reset"/>) fires nothing.</para>
/// <para>Persistence is <see cref="HeightCacheFile"/>. Saves of one cache are serialized, and a slow save does not
/// hold the lock the game thread needs to store a capture. <see cref="IsDirty"/> tells the owner when to save.</para>
/// </remarks>
public sealed class HeightCache : IHeightGrid
{
    private ConcurrentDictionary<(int, int), HeightChunk> _chunks = new();
    private readonly object _gate = new();
    private long _version;
    private long _savedVersion;

    /// <summary>Serializes saves and <see cref="Reset"/> so an older snapshot cannot overwrite a newer file.</summary>
    internal object SaveSync { get; } = new();

    /// <summary>Fires after a chunk column was added or changed.</summary>
    public event EventHandler<HeightCacheChangedEventArgs>? Changed;

    /// <summary>Stored chunk columns.</summary>
    public int Count => Map.Count;

    /// <summary>Incremented on every change; lets planners tell whether the cache moved since they planned.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>True when there are changes since the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => Version != Interlocked.Read(ref _savedVersion);

    /// <summary>Stores a capture. Returns true (and fires <see cref="Changed"/>) when it is new or different.</summary>
    /// <param name="chunk">The capture.</param>
    /// <returns>Whether the cache changed.</returns>
    public bool Store(HeightChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var key = (chunk.ChunkX, chunk.ChunkZ);
        HeightCacheChange change;
        EventHandler<HeightCacheChangedEventArgs>? changed;
        lock (_gate)
        {
            ConcurrentDictionary<(int, int), HeightChunk> map = _chunks;
            if (map.TryGetValue(key, out HeightChunk? old))
            {
                if (old.Equals(chunk))
                {
                    return false;
                }

                map[key] = chunk;
                change = HeightCacheChange.Updated;
            }
            else
            {
                map[key] = chunk;
                change = HeightCacheChange.Added;
            }

            Interlocked.Increment(ref _version);
            changed = Changed;
        }

        changed?.Invoke(this, new HeightCacheChangedEventArgs(chunk.ChunkX, chunk.ChunkZ, change));
        return true;
    }

    /// <summary>The stored chunk column, or null.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    public HeightChunk? GetChunk(int chunkX, int chunkZ) =>
        Map.TryGetValue((chunkX, chunkZ), out HeightChunk? c) ? c : null;

    /// <summary>True when the chunk column containing block (x, z) is revealed.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="z">Block Z.</param>
    public bool IsRevealed(int x, int z) => Map.ContainsKey((HeightChunk.ChunkOf(x), HeightChunk.ChunkOf(z)));

    /// <inheritdoc />
    public HeightColumn GetColumn(int x, int z) =>
        Map.TryGetValue((HeightChunk.ChunkOf(x), HeightChunk.ChunkOf(z)), out HeightChunk? c)
            ? c.GetLocal(x & (HeightChunk.Size - 1), z & (HeightChunk.Size - 1))
            : HeightColumn.Unknown;

    /// <summary>All stored chunk columns, ordered by (ChunkX, ChunkZ): a consistent copy for saving.</summary>
    /// <returns>The chunks.</returns>
    public IReadOnlyList<HeightChunk> Snapshot()
    {
        lock (_gate)
        {
            return CopyChunks();
        }
    }

    /// <summary>Copies the chunks and the version they reflect, or returns false when nothing needs saving.</summary>
    /// <param name="version">The version the copy reflects.</param>
    /// <param name="chunks">The copy, ordered by chunk coordinate.</param>
    /// <returns>True when the cache was dirty.</returns>
    internal bool TryCopyForSave(out long version, out IReadOnlyList<HeightChunk> chunks)
    {
        lock (_gate)
        {
            if (!IsDirty)
            {
                version = 0;
                chunks = [];
                return false;
            }

            version = Version;
            chunks = CopyChunks();
            return true;
        }
    }

    private HeightChunk[] CopyChunks() => _chunks.Values.OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkZ).ToArray();

    /// <summary>Replaces the contents with <paramref name="chunks"/> (a loaded file). Fires no events and leaves the cache clean.</summary>
    /// <param name="chunks">The chunks. A null entry is rejected. Duplicate coordinates keep the last chunk.</param>
    public void Reset(IEnumerable<HeightChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var next = new ConcurrentDictionary<(int, int), HeightChunk>();
        foreach (HeightChunk c in chunks)
        {
            ArgumentNullException.ThrowIfNull(c);
            next[(c.ChunkX, c.ChunkZ)] = c;
        }

        // SaveSync first, then the map gate: the same order as a save, so the two cannot deadlock.
        lock (SaveSync)
        {
            lock (_gate)
            {
                Volatile.Write(ref _chunks, next);
                long v = Interlocked.Increment(ref _version);
                Interlocked.Exchange(ref _savedVersion, v);
            }
        }
    }

    /// <summary>
    /// Marks the cache saved as of <paramref name="version"/> (the <see cref="Version"/> read before taking the snapshot).
    /// A version older than the current saved mark, or newer than <see cref="Version"/>, is ignored, so a slow save
    /// cannot roll the cache back to dirty after a newer save or a load.
    /// </summary>
    /// <param name="version">Version the saved snapshot reflects.</param>
    public void MarkSaved(long version)
    {
        lock (_gate)
        {
            long saved = Interlocked.Read(ref _savedVersion);
            long current = Interlocked.Read(ref _version);
            if (version < saved || version > current)
            {
                return;
            }

            Interlocked.Exchange(ref _savedVersion, version);
        }
    }

    private ConcurrentDictionary<(int, int), HeightChunk> Map => Volatile.Read(ref _chunks);
}

/// <summary>
/// Loaded terrain over the cache: <paramref name="primary"/>'s column unless it is Unknown, then
/// <paramref name="fallback"/>'s. How the coarse planner reads revealed-but-unloaded land (WF-16): loaded terrain
/// wins where both know a column, the cache fills in the rest, and columns neither knows stay Unknown.
/// </summary>
/// <param name="primary">Usually <see cref="HeightGrid.FromBlockGrid"/> over the loaded window.</param>
/// <param name="fallback">Usually the <see cref="HeightCache"/>.</param>
public sealed class LayeredHeightGrid(IHeightGrid primary, IHeightGrid fallback) : IHeightGrid
{
    private readonly IHeightGrid _primary = primary ?? throw new ArgumentNullException(nameof(primary));
    private readonly IHeightGrid _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

    /// <inheritdoc />
    public HeightColumn GetColumn(int x, int z)
    {
        HeightColumn c = _primary.GetColumn(x, z);
        return c.Kind != ColumnKind.Unknown ? c : _fallback.GetColumn(x, z);
    }
}
