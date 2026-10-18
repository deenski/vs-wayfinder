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
/// replaced whole, so a reader sees either the old or the new chunk column, never a mix.</para>
/// <para><see cref="Changed"/> fires on the storing thread, after the store, only when the data changed (new, or
/// different from what was stored). Re-capturing an identical chunk column fires nothing.</para>
/// <para>Persistence is <see cref="HeightCacheFile"/>; <see cref="IsDirty"/> tells the owner when to save.</para>
/// </remarks>
public sealed class HeightCache : IHeightGrid
{
    private readonly ConcurrentDictionary<(int, int), HeightChunk> _chunks = new();
    private long _version;
    private long _savedVersion;

    /// <summary>Fires after a chunk column was added or changed.</summary>
    public event EventHandler<HeightCacheChangedEventArgs>? Changed;

    /// <summary>Stored chunk columns.</summary>
    public int Count => _chunks.Count;

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
        while (true)
        {
            if (_chunks.TryGetValue(key, out HeightChunk? old))
            {
                if (old.Equals(chunk))
                {
                    return false;
                }

                if (_chunks.TryUpdate(key, chunk, old))
                {
                    change = HeightCacheChange.Updated;
                    break;
                }
            }
            else if (_chunks.TryAdd(key, chunk))
            {
                change = HeightCacheChange.Added;
                break;
            }
        }

        Interlocked.Increment(ref _version);
        Changed?.Invoke(this, new HeightCacheChangedEventArgs(chunk.ChunkX, chunk.ChunkZ, change));
        return true;
    }

    /// <summary>The stored chunk column, or null.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    public HeightChunk? GetChunk(int chunkX, int chunkZ) => _chunks.TryGetValue((chunkX, chunkZ), out HeightChunk? c) ? c : null;

    /// <summary>True when the chunk column containing block (x, z) is revealed.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="z">Block Z.</param>
    public bool IsRevealed(int x, int z) => _chunks.ContainsKey((HeightChunk.ChunkOf(x), HeightChunk.ChunkOf(z)));

    /// <inheritdoc />
    public HeightColumn GetColumn(int x, int z) =>
        _chunks.TryGetValue((HeightChunk.ChunkOf(x), HeightChunk.ChunkOf(z)), out HeightChunk? c)
            ? c.GetLocal(x & (HeightChunk.Size - 1), z & (HeightChunk.Size - 1))
            : HeightColumn.Unknown;

    /// <summary>All stored chunk columns, ordered by (ChunkX, ChunkZ): a consistent copy for saving.</summary>
    /// <returns>The chunks.</returns>
    public IReadOnlyList<HeightChunk> Snapshot() =>
        _chunks.Values.OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkZ).ToArray();

    /// <summary>Replaces the contents with <paramref name="chunks"/> (a loaded file). Fires no events and leaves the cache clean.</summary>
    /// <param name="chunks">The chunks.</param>
    public void Reset(IEnumerable<HeightChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        _chunks.Clear();
        foreach (HeightChunk c in chunks)
        {
            _chunks[(c.ChunkX, c.ChunkZ)] = c;
        }

        long v = Interlocked.Increment(ref _version);
        Interlocked.Exchange(ref _savedVersion, v);
    }

    /// <summary>Marks the cache saved as of <paramref name="version"/> (the <see cref="Version"/> read before taking the snapshot).</summary>
    /// <param name="version">Version the saved snapshot reflects.</param>
    public void MarkSaved(long version) => Interlocked.Exchange(ref _savedVersion, version);
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
