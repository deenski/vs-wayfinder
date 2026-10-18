using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class JourneyMapNoticeTests
{
    [Fact]
    public void Notice_fires_at_most_once_while_the_mod_is_loaded()
    {
        var notice = new JourneyMapNotice();

        Assert.True(notice.TryFire(modLoaded: true));
        Assert.False(notice.TryFire(modLoaded: true));
        Assert.False(notice.TryFire(modLoaded: true));
    }

    [Fact]
    public void Notice_does_not_fire_when_the_mod_is_absent()
    {
        var notice = new JourneyMapNotice();

        Assert.False(notice.TryFire(modLoaded: false));
        Assert.False(notice.TryFire(modLoaded: false));
        Assert.True(notice.TryFire(modLoaded: true));
    }
}
