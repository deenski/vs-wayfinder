using System;
using System.Linq.Expressions;
using System.Reflection;
using Vintagestory.API.Common;

namespace Wayfinding.Snapshot;

/// <summary>
/// Binds <c>GetBlockIdUnsafe(int index, int layer)</c> once.
/// </summary>
/// <remarks>
/// <c>IChunkBlocks.GetBlockIdUnsafe(int)</c> reads the solid layer only. The layer overload is public
/// on the 1.22.7 runtime chunk-data type and is not on the interface, so the adapter binds it instead
/// of referencing <c>VintagestoryLib</c>. The adapter reads
/// <c>BlockLayersAccess.Fluid</c> and <c>BlockLayersAccess.Solid</c>. The most-solid layer is not
/// used: its implementation looks up <c>Blocks[id].SideSolid</c> and throws when that slot is null.
/// </remarks>
internal static class LayeredBlockIds
{
    private static global::System.Func<IChunkBlocks, int, int, int>? reader;

    public static int Read(IChunkBlocks data, int index, int layer)
    {
        global::System.Func<IChunkBlocks, int, int, int> read = reader ??= Bind(data);
        return read(data, index, layer);
    }

    private static global::System.Func<IChunkBlocks, int, int, int> Bind(IChunkBlocks data)
    {
        MethodInfo? method = data.GetType().GetMethod(
            "GetBlockIdUnsafe",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(int), typeof(int)],
            modifiers: null);
        if (method == null)
        {
            throw new MissingMethodException(data.GetType().FullName, "GetBlockIdUnsafe(int, int)");
        }

        ParameterExpression block = Expression.Parameter(typeof(IChunkBlocks), "data");
        ParameterExpression index = Expression.Parameter(typeof(int), "index");
        ParameterExpression layer = Expression.Parameter(typeof(int), "layer");
        MethodCallExpression call = Expression.Call(Expression.Convert(block, data.GetType()), method, index, layer);
        return Expression.Lambda<global::System.Func<IChunkBlocks, int, int, int>>(call, block, index, layer).Compile();
    }
}
