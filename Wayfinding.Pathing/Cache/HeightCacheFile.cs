using System.Buffers.Binary;

namespace Wayfinding.Pathing.Cache;

/// <summary>Outcome of <see cref="HeightCacheFile.Load"/>.</summary>
public enum HeightCacheLoadStatus : byte
{
    /// <summary>The file was read; every chunk column in it is in the result.</summary>
    Loaded = 0,

    /// <summary>No file: a new world, or the cache was never saved. Start empty.</summary>
    Missing,

    /// <summary>Not a height cache file (wrong magic) or a damaged header.</summary>
    BadHeader,

    /// <summary>A height cache from another format version. Start empty and rebuild.</summary>
    WrongVersion,

    /// <summary>The file is shorter or longer than its header says (an interrupted write, a partial copy).</summary>
    Truncated,

    /// <summary>The payload does not match its checksum, or records are duplicated or out of order.</summary>
    Corrupt,

    /// <summary>The file belongs to another dimension.</summary>
    WrongDimension,

    /// <summary>The file could not be read (I/O error, permissions).</summary>
    Unreadable,
}

/// <summary>Result of <see cref="HeightCacheFile.Load"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Chunks">The chunk columns read (empty unless Loaded).</param>
/// <param name="QuarantinedTo">Where a damaged file was moved (for the log), or null.</param>
public sealed record HeightCacheLoadResult(HeightCacheLoadStatus Status, IReadOnlyList<HeightChunk> Chunks, string? QuarantinedTo)
{
    /// <summary>True when the cache must be rebuilt from scratch (anything but Loaded or Missing).</summary>
    public bool Rebuilt => Status is not (HeightCacheLoadStatus.Loaded or HeightCacheLoadStatus.Missing);
}

/// <summary>
/// The on-disk height cache, one file per world and dimension (WF-16). Versioned binary with checksums, written
/// atomically; any damage means "start empty and rebuild as chunks load", never an exception or a partial cache.
/// </summary>
/// <remarks>
/// <para><b>Format v1</b> (little-endian):</para>
/// <code>
///  0  4  magic "WFHC"
///  4  2  format version (1)
///  6  2  header size (24)
///  8  4  dimension
/// 12  4  chunk column count N
/// 16  4  CRC-32 of the payload (bytes 24 to the end)
/// 20  4  CRC-32 of header bytes 0..19
/// 24     N records, sorted by (chunk X, chunk Z), no duplicates:
///          4 chunk X, 4 chunk Z, 1 024 × 2 packed columns (<see cref="HeightChunk"/>)  = 2 056 bytes
/// </code>
/// <para>The file is exactly 24 + 2 056 N bytes, so it grows linearly with the explored area (~2 KB per chunk
/// column). <see cref="Save"/> writes a sibling temp file, flushes it to disk and renames it over the old file,
/// so a crash leaves either the old or the new cache. <see cref="Load"/> moves a damaged file aside to
/// <c>&lt;path&gt;.corrupt</c> so the next save starts clean.</para>
/// </remarks>
public static class HeightCacheFile
{
    /// <summary>Current format version.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>Header size in bytes.</summary>
    public const int HeaderSize = 24;

    /// <summary>Bytes per chunk column record.</summary>
    public const int RecordSize = 8 + HeightChunk.Columns * 2;

    /// <summary>Most chunk columns a file may claim (≈ 2 GB); larger counts are treated as a damaged header.</summary>
    public const int MaxChunks = 1_000_000;

    private static ReadOnlySpan<byte> Magic => "WFHC"u8;

    /// <summary>The file length for <paramref name="chunks"/> chunk columns.</summary>
    /// <param name="chunks">Chunk column count.</param>
    public static long FileSize(int chunks) => HeaderSize + (long)RecordSize * chunks;

    /// <summary>Serializes <paramref name="chunks"/> (any order; duplicates keep the last).</summary>
    /// <param name="chunks">Chunk columns.</param>
    /// <param name="dimension">The dimension they belong to.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Serialize(IEnumerable<HeightChunk> chunks, int dimension = 0)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        HeightChunk[] sorted = chunks
            .GroupBy(c => (c.ChunkX, c.ChunkZ)).Select(g => g.Last())
            .OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkZ).ToArray();
        if (sorted.Length > MaxChunks)
        {
            throw new ArgumentException($"At most {MaxChunks} chunk columns per file.", nameof(chunks));
        }

        var bytes = new byte[FileSize(sorted.Length)];
        Span<byte> span = bytes;
        int at = HeaderSize;
        foreach (HeightChunk c in sorted)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], c.ChunkX);
            BinaryPrimitives.WriteInt32LittleEndian(span[(at + 4)..], c.ChunkZ);
            at += 8;
            foreach (ushort v in c.Packed)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[at..], v);
                at += 2;
            }
        }

        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], dimension);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], sorted.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], Crc32.Compute(span[HeaderSize..]));
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], Crc32.Compute(span[..20]));
        return bytes;
    }

    /// <summary>Parses file bytes. Never throws for bad data.</summary>
    /// <param name="bytes">The file contents.</param>
    /// <param name="dimension">The dimension expected.</param>
    /// <param name="chunks">The chunk columns when the result is Loaded, else empty.</param>
    /// <returns>Loaded, or why the data was rejected.</returns>
    public static HeightCacheLoadStatus Deserialize(ReadOnlySpan<byte> bytes, int dimension, out IReadOnlyList<HeightChunk> chunks)
    {
        chunks = [];
        if (bytes.Length < 8)
        {
            // An empty or cut-off file: truncated if what is there is the start of a header.
            return Magic.StartsWith(bytes[..Math.Min(bytes.Length, 4)]) ? HeightCacheLoadStatus.Truncated : HeightCacheLoadStatus.BadHeader;
        }

        if (!bytes[..4].SequenceEqual(Magic))
        {
            return HeightCacheLoadStatus.BadHeader;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != FormatVersion)
        {
            return HeightCacheLoadStatus.WrongVersion;
        }

        if (bytes.Length < HeaderSize)
        {
            return HeightCacheLoadStatus.Truncated;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) != Crc32.Compute(bytes[..20]))
        {
            return HeightCacheLoadStatus.BadHeader;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        if (count < 0 || count > MaxChunks)
        {
            return HeightCacheLoadStatus.BadHeader;
        }

        if (bytes.Length != FileSize(count))
        {
            return HeightCacheLoadStatus.Truncated;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != dimension)
        {
            return HeightCacheLoadStatus.WrongDimension;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]) != Crc32.Compute(bytes[HeaderSize..]))
        {
            return HeightCacheLoadStatus.Corrupt;
        }

        var list = new HeightChunk[count];
        var packed = new ushort[HeightChunk.Columns];
        int at = HeaderSize;
        (int X, int Z)? previous = null;
        for (int k = 0; k < count; k++)
        {
            int cx = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
            int cz = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
            if (previous is { } p && (cx < p.X || (cx == p.X && cz <= p.Z)))
            {
                return HeightCacheLoadStatus.Corrupt; // out of order or duplicated
            }

            previous = (cx, cz);
            at += 8;
            for (int i = 0; i < packed.Length; i++, at += 2)
            {
                packed[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);
            }

            list[k] = HeightChunk.FromPacked(cx, cz, packed);
        }

        chunks = list;
        return HeightCacheLoadStatus.Loaded;
    }

    /// <summary>Writes <paramref name="chunks"/> to <paramref name="path"/> atomically (temp file, flush, rename).</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="chunks">Chunk columns.</param>
    /// <param name="dimension">Dimension.</param>
    public static void Save(string path, IEnumerable<HeightChunk> chunks, int dimension = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] bytes = Serialize(chunks, dimension);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Saves <paramref name="cache"/> if it has unsaved changes. Returns whether it wrote.</summary>
    /// <param name="cache">The cache.</param>
    /// <param name="path">Destination file.</param>
    /// <param name="dimension">Dimension.</param>
    /// <returns>True when a file was written.</returns>
    public static bool SaveIfDirty(HeightCache cache, string path, int dimension = 0)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (!cache.IsDirty)
        {
            return false;
        }

        long version = cache.Version;
        Save(path, cache.Snapshot(), dimension);
        cache.MarkSaved(version);
        return true;
    }

    /// <summary>
    /// Reads <paramref name="path"/>. A missing file is Missing; a damaged, truncated, other-version or
    /// other-dimension file is moved aside to <c>&lt;path&gt;.corrupt</c> and reported, with no chunks. Never throws
    /// for file contents.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="dimension">The dimension expected.</param>
    /// <returns>The result.</returns>
    public static HeightCacheLoadResult Load(string path, int dimension = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        TryDelete(path + ".tmp"); // a leftover from a crash mid-save; the real file is intact
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return new HeightCacheLoadResult(HeightCacheLoadStatus.Missing, [], null);
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HeightCacheLoadResult(HeightCacheLoadStatus.Unreadable, [], null);
        }

        HeightCacheLoadStatus status = Deserialize(bytes, dimension, out IReadOnlyList<HeightChunk> chunks);
        if (status == HeightCacheLoadStatus.Loaded)
        {
            return new HeightCacheLoadResult(status, chunks, null);
        }

        string? aside = path + ".corrupt";
        try
        {
            File.Move(path, aside, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            aside = null;
        }

        return new HeightCacheLoadResult(status, [], aside);
    }

    /// <summary>Loads <paramref name="path"/> into <paramref name="cache"/> (replacing its contents; empty unless Loaded).</summary>
    /// <param name="cache">The cache.</param>
    /// <param name="path">The file.</param>
    /// <param name="dimension">Dimension.</param>
    /// <returns>The load result.</returns>
    public static HeightCacheLoadResult LoadInto(HeightCache cache, string path, int dimension = 0)
    {
        ArgumentNullException.ThrowIfNull(cache);
        HeightCacheLoadResult r = Load(path, dimension);
        cache.Reset(r.Chunks);
        return r;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320), as in zip and PNG.</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                t[i] = c;
            }

            return t;
        }
    }
}
