using Wayfinding.Pathing.Input;

namespace Wayfinding.Pathing.Tests;

public class NavHotkeyTests
{
    [Fact]
    public void Holding_a_hotkey_fires_once_until_the_window_passes()
    {
        var guard = new HotkeyRepeatGuard();
        long now = 1_000;
        Assert.True(guard.Accept(now));
        for (int i = 0; i < 40; i++)
        {
            now += 30;
            Assert.False(guard.Accept(now));
        }

        Assert.True(guard.Accept(now + HotkeyRepeatGuard.WindowMs));
    }

    [Fact]
    public void Cycle_walks_pinned_waypoints_and_wraps()
    {
        Guid home = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid camp = Guid.Parse("10000000-0000-0000-0000-000000000002");
        PinnedStep[] entries =
        [
            new("not-a-guid", "Broken", pinned: true),
            new(home.ToString("D"), "Home", pinned: true),
            new(Guid.NewGuid().ToString("D"), "Loose", pinned: false),
            new(camp.ToString("D"), "Camp", pinned: true),
        ];

        Assert.True(PinnedCycle.TryNext(entries, null, out Guid first, out string firstName));
        Assert.Equal(home, first);
        Assert.Equal("Home", firstName);

        Assert.True(PinnedCycle.TryNext(entries, home, out Guid second, out string secondName));
        Assert.Equal(camp, second);
        Assert.Equal("Camp", secondName);

        Assert.True(PinnedCycle.TryNext(entries, camp, out Guid wrapped, out _));
        Assert.Equal(home, wrapped);

        Assert.False(PinnedCycle.TryNext([], null, out _, out _));
        Assert.False(PinnedCycle.TryNext([new(null, "Nope", pinned: false)], null, out _, out _));
    }
}
