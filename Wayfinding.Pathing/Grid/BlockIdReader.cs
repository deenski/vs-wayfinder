using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Reads one layered block id from a chunk-data object, without taking a dependency on game types.
/// </summary>
/// <remarks>
/// False means the caller leaves the cell <see cref="CellClass.Unknown"/>. Implementations do not throw:
/// a missing method, a failed bind, or a failing call all return false.
/// </remarks>
public interface IBlockIdReader
{
    /// <summary>
    /// Reads the id at <paramref name="index"/> on <paramref name="layer"/>.
    /// </summary>
    /// <param name="data">The runtime chunk data. Null returns false.</param>
    /// <param name="index">Chunk-local block index.</param>
    /// <param name="layer">Layer argument passed through to <c>GetBlockIdUnsafe</c>.</param>
    /// <param name="logWarning">Called once for each runtime type that cannot be read. May be null.</param>
    /// <param name="id">The id when this returns true. Zero when it returns false.</param>
    /// <returns>False when the cell must stay unknown.</returns>
    bool TryRead(object? data, int index, int layer, Action<string>? logWarning, out int id);
}

/// <summary>
/// Binds <c>GetBlockIdUnsafe(int index, int layer)</c> once per concrete runtime type.
/// </summary>
/// <remarks>
/// The delegate casts through the method's declaring type, not the first
/// runtime type that happened to bind. Client and server chunk types can therefore share a method
/// declared on a common base, and a type that declares its own method does not reuse the other
/// type's cast. A failed bind is stored as an unsupported marker for that type, so later reads
/// return false and the warning is logged once. Nothing on this path throws.
/// </remarks>
public sealed class ReflectiveBlockIdReader : IBlockIdReader
{
    private readonly ConcurrentDictionary<Type, Slot> slots = new();

    /// <summary>
    /// Reads one id. See <see cref="IBlockIdReader.TryRead"/>.
    /// </summary>
    /// <param name="data">The runtime chunk data. Null returns false.</param>
    /// <param name="index">Chunk-local block index.</param>
    /// <param name="layer">Layer argument passed through to <c>GetBlockIdUnsafe</c>.</param>
    /// <param name="logWarning">Called once for each runtime type that cannot be read. May be null.</param>
    /// <param name="id">The id when this returns true. Zero when it returns false.</param>
    /// <returns>False when the cell must stay unknown.</returns>
    public bool TryRead(object? data, int index, int layer, Action<string>? logWarning, out int id)
    {
        id = 0;
        if (data == null)
        {
            return false;
        }

        try
        {
            Slot slot = slots.GetOrAdd(data.GetType(), Bind);
            if (slot.Read == null)
            {
                WarnOnce(slot, data.GetType(), logWarning);
                return false;
            }

            id = slot.Read(data, index, layer);
            return true;
        }
        catch (Exception)
        {
            // A bad cast or a throwing target must not escape into the tick.
            id = 0;
            return false;
        }
    }

    private static void WarnOnce(Slot slot, Type type, Action<string>? logWarning)
    {
        if (logWarning == null || !slot.TryMarkLogged())
        {
            return;
        }

        logWarning("GetBlockIdUnsafe(int, int) is missing on " + type.FullName + "; those cells stay Unknown.");
    }

    private static Slot Bind(Type type)
    {
        try
        {
            MethodInfo? method = type.GetMethod(
                "GetBlockIdUnsafe",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(int), typeof(int)],
                modifiers: null);
            if (method == null || method.ReturnType != typeof(int))
            {
                return new Slot(null);
            }

            // Sibling types inherit one method. Cast to the type that declares it, not to
            // whichever concrete type bound first.
            Type target = method.DeclaringType ?? type;
            ParameterExpression block = Expression.Parameter(typeof(object), "data");
            ParameterExpression index = Expression.Parameter(typeof(int), "index");
            ParameterExpression layer = Expression.Parameter(typeof(int), "layer");
            MethodCallExpression call = Expression.Call(Expression.Convert(block, target), method, index, layer);
            var read = Expression.Lambda<Func<object, int, int, int>>(call, block, index, layer).Compile();
            return new Slot(read);
        }
        catch (Exception)
        {
            return new Slot(null);
        }
    }

    /// <summary>A null <see cref="Read"/> is the unsupported marker for one runtime type.</summary>
    private sealed class Slot
    {
        private int logged;

        public Slot(Func<object, int, int, int>? read) => Read = read;

        public Func<object, int, int, int>? Read { get; }

        public bool TryMarkLogged() => Interlocked.Exchange(ref logged, 1) == 0;
    }
}
