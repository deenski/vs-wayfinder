using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Tests;

public class TypeDelegateCacheTests
{
    [Fact]
    public void BindsOncePerType_AndLogsAMissingMethodOnce()
    {
        var cache = new TypeDelegateCache<Func<int, int>>();
        var logged = new List<Type>();
        int binds = 0;

        Func<Type, Func<int, int>?> bind = type =>
        {
            binds++;
            return type == typeof(int) ? static value => value + 1 : null;
        };

        Assert.True(cache.TryGet(typeof(int), bind, logged.Add, out Func<int, int>? reader));
        Assert.NotNull(reader);
        Assert.Equal(4, reader(3));
        Assert.True(cache.TryGet(typeof(int), _ => throw new InvalidOperationException("rebind"), logged.Add, out Func<int, int>? again));
        Assert.Same(reader, again);

        Assert.False(cache.TryGet(typeof(string), bind, logged.Add, out _));
        Assert.False(cache.TryGet(typeof(string), _ => throw new InvalidOperationException("rebind"), logged.Add, out _));

        Assert.Equal(2, binds);
        Assert.Equal([typeof(string)], logged);
    }
}
