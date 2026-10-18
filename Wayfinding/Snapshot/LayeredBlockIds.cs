using System;
using Vintagestory.API.Common;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Reads fluid and solid block ids through <see cref="ReflectiveBlockIdReader"/>.
/// </summary>
/// <remarks>
/// <c>IChunkBlocks.GetBlockIdUnsafe(int)</c> reads the solid layer only. The layer overload is public
/// on the 1.22.7 runtime chunk-data type and is not on the interface, so the reader binds it instead
/// of referencing <c>VintagestoryLib</c>. The adapter reads
/// <c>BlockLayersAccess.Fluid</c> and <c>BlockLayersAccess.Solid</c>. The most-solid layer is not
/// used: its implementation looks up <c>Blocks[id].SideSolid</c> and throws when that slot is null.
/// One reader is shared by client and server chunk types. Each concrete type gets its own delegate,
/// cast to the method's declaring type. A missing method leaves the cell <see cref="CellClass.Unknown"/>
/// and is logged once per type.
/// </remarks>
internal static class LayeredBlockIds
{
    private static readonly ReflectiveBlockIdReader reader = new();

    public static bool TryRead(IChunkBlocks data, int index, int layer, Action<string>? logMissing, out int id) =>
        reader.TryRead(data, index, layer, logMissing, out id);
}
