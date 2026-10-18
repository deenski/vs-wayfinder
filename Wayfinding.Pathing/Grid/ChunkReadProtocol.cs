namespace Wayfinding.Pathing.Grid;

/// <summary>One step of the per-chunk read the game adapter must perform, in order.</summary>
public enum ChunkReadStep : byte
{
    /// <summary><c>IWorldChunk.Unpack_ReadOnly</c>. Not <c>Unpack</c>, which marks the chunk dirty and forces a later recompression.</summary>
    UnpackReadOnly = 1,

    /// <summary><c>AcquireBlockReadLock</c>, one lock for the whole chunk. Taken only after unpack left <c>Data</c> non-null.</summary>
    AcquireLock = 2,

    /// <summary>A cell read through <c>GetBlockIdUnsafe</c> while the lock is held.</summary>
    Read = 3,

    /// <summary><c>ReleaseBlockReadLock</c>, including when a read throws.</summary>
    ReleaseLock = 4,
}

/// <summary>Records <see cref="ChunkReadStep"/> values so a test can check the lock and unpack order.</summary>
public interface IChunkReadLog
{
    /// <summary>Appends one step.</summary>
    /// <param name="step">The step that is about to run, or that just ran for <see cref="ChunkReadStep.Read"/>.</param>
    void Record(ChunkReadStep step);
}

/// <summary>
/// The three chunk operations around a bulk read. The game adapter implements this with
/// <c>Unpack_ReadOnly</c>, <c>AcquireBlockReadLock</c>, and <c>ReleaseBlockReadLock</c>.
/// </summary>
public interface IChunkReader
{
    /// <summary>
    /// Unpacks the chunk for reading. Returns false when the chunk is still unreadable
    /// (disposed, or <c>Data</c> stayed null because the chunk is compressed).
    /// </summary>
    /// <returns>True when block data can be locked and read.</returns>
    bool UnpackReadOnly();

    /// <summary>Takes the chunk's bulk read lock. Call only after <see cref="UnpackReadOnly"/> returned true.</summary>
    void AcquireLock();

    /// <summary>Releases the bulk read lock.</summary>
    void ReleaseLock();
}

/// <summary>
/// The per-chunk read order: <see cref="ChunkReadStep.UnpackReadOnly"/>, then
/// <see cref="ChunkReadStep.AcquireLock"/>, then cell reads, then <see cref="ChunkReadStep.ReleaseLock"/>.
/// </summary>
/// <remarks>
/// <c>chunk.Data</c> is null while the chunk is compressed, and <c>AcquireBlockReadLock</c> locks
/// <c>Data</c>. Unpack first and skip the lock when unpack does not make the chunk readable.
/// Do not call <c>Unpack()</c>: it sets <c>PotentialBlockOrLightingChanges</c> and the game recompresses
/// the chunk later. This type is the test seam for that order. The game adapter calls it; tests drive
/// a fake <see cref="IChunkReader"/>.
/// </remarks>
public static class ChunkReadProtocol
{
    /// <summary>
    /// Unpacks, then locks. Returns false without locking when the chunk stays unreadable.
    /// </summary>
    /// <param name="reader">The chunk operations.</param>
    /// <param name="log">Optional order log. May be null.</param>
    /// <param name="confirmReadable">
    /// Optional check after a successful unpack and before the lock. The game adapter passes
    /// <c>GetBlockOrNull</c> here. The 1.22.7 read lock is a counter, so a nested read on the same
    /// thread does not deadlock; this check still runs before the lock because it is a one-time
    /// loaded check. False skips the lock and leaves the chunk unknown.
    /// </param>
    /// <returns>True when the caller holds the lock and must <see cref="Close"/> it.</returns>
    public static bool TryOpen(IChunkReader reader, IChunkReadLog? log, Func<bool>? confirmReadable = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        log?.Record(ChunkReadStep.UnpackReadOnly);
        if (!reader.UnpackReadOnly())
        {
            return false;
        }

        if (confirmReadable != null && !confirmReadable())
        {
            return false;
        }

        log?.Record(ChunkReadStep.AcquireLock);
        reader.AcquireLock();
        return true;
    }

    /// <summary>Records a cell read. The caller performs the read itself.</summary>
    /// <param name="log">Optional order log. May be null.</param>
    public static void Read(IChunkReadLog? log) => log?.Record(ChunkReadStep.Read);

    /// <summary>Releases the lock taken by <see cref="TryOpen"/>.</summary>
    /// <param name="reader">The chunk operations.</param>
    /// <param name="log">Optional order log. May be null.</param>
    public static void Close(IChunkReader reader, IChunkReadLog? log)
    {
        ArgumentNullException.ThrowIfNull(reader);
        log?.Record(ChunkReadStep.ReleaseLock);
        reader.ReleaseLock();
    }
}
