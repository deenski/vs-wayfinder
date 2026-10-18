using System;
using System.Linq.Expressions;
using System.Reflection;
using Vintagestory.API.Common;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Binds <c>GetBlockIdUnsafe(int index, int layer)</c> once per runtime chunk type.
/// </summary>
/// <remarks>
/// <c>IChunkBlocks.GetBlockIdUnsafe(int)</c> reads the solid layer only. The layer overload is public
/// on the 1.22.7 runtime chunk-data type and is not on the interface, so the adapter binds it instead
/// of referencing <c>VintagestoryLib</c>. The adapter reads
/// <c>BlockLayersAccess.Fluid</c> and <c>BlockLayersAccess.Solid</c>. The most-solid layer is not
/// used: its implementation looks up <c>Blocks[id].SideSolid</c> and throws when that slot is null.
/// A missing method leaves the cell <see cref="CellClass.Unknown"/> and is logged once per type.
/// </remarks>
internal static class LayeredBlockIds
{
    private static readonly TypeDelegateCache<global::System.Func<IChunkBlocks, int, int, int>> cache = new();

    public static bool TryRead(IChunkBlocks data, int index, int layer, Action<string>? logMissing, out int id)
    {
        if (!cache.TryGet(data.GetType(), Bind, logMissing == null ? null : type => logMissing(MissingMessage(type)), out global::System.Func<IChunkBlocks, int, int, int>? read)
            || read == null)
        {
            id = 0;
            return false;
        }

        id = read(data, index, layer);
        return true;
    }

    private static string MissingMessage(Type type) =>
        "GetBlockIdUnsafe(int, int) is missing on " + type.FullName + "; those cells stay Unknown.";

    private static global::System.Func<IChunkBlocks, int, int, int>? Bind(Type type)
    {
        MethodInfo? method = type.GetMethod(
            "GetBlockIdUnsafe",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(int), typeof(int)],
            modifiers: null);
        if (method == null)
        {
            return null;
        }

        ParameterExpression block = Expression.Parameter(typeof(IChunkBlocks), "data");
        ParameterExpression index = Expression.Parameter(typeof(int), "index");
        ParameterExpression layer = Expression.Parameter(typeof(int), "layer");
        MethodCallExpression call = Expression.Call(Expression.Convert(block, type), method, index, layer);
        return Expression.Lambda<global::System.Func<IChunkBlocks, int, int, int>>(call, block, index, layer).Compile();
    }
}
