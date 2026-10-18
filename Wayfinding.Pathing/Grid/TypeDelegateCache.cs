using System.Collections.Concurrent;

namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Binds one delegate per runtime type and keeps that binding.
/// </summary>
/// <remarks>
/// The game adapter uses this for <c>GetBlockIdUnsafe(int, int)</c>. In single-player the client and
/// server chunk types can differ, so a single static delegate is the wrong cache. A missing method is
/// stored as no delegate and reported once per type.
/// </remarks>
/// <typeparam name="TDelegate">The bound delegate type.</typeparam>
public sealed class TypeDelegateCache<TDelegate>
    where TDelegate : class
{
    private readonly ConcurrentDictionary<Type, Slot> slots = new();

    /// <summary>
    /// Returns the delegate bound for <paramref name="type"/>, binding it on first use.
    /// </summary>
    /// <param name="type">The runtime type.</param>
    /// <param name="bind">Produces the delegate, or null when the type has no such method.</param>
    /// <param name="onMissing">Called once for each type whose bind returned null. May be null.</param>
    /// <param name="bound">The delegate when this returns true.</param>
    /// <returns>False when the type has no delegate. The caller leaves the cell unknown.</returns>
    public bool TryGet(Type type, Func<Type, TDelegate?> bind, Action<Type>? onMissing, out TDelegate? bound)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(bind);
        Slot slot = slots.GetOrAdd(type, key => new Slot(bind(key)));
        bound = slot.Bound;
        if (bound != null)
        {
            return true;
        }

        if (onMissing != null && slot.TryMarkLogged())
        {
            onMissing(type);
        }

        return false;
    }

    private sealed class Slot(TDelegate? bound)
    {
        private int logged;

        public TDelegate? Bound { get; } = bound;

        public bool TryMarkLogged() => Interlocked.Exchange(ref logged, 1) == 0;
    }
}
