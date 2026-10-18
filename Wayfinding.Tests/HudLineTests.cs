using System;
using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class HudLineTests
{
    [Fact]
    public void Short_form_is_an_arrow_name_and_meters()
    {
        Assert.Equal("→ target 412 m", HudLine.Format("target", "412 m", "", ""));
    }

    [Fact]
    public void Mode_and_status_badges_follow_the_distance()
    {
        var line = HudLine.Format("Camp", "412 m", HudLine.ModeBadge, HudLine.Status(NavTargetKind.Waypoint));

        Assert.Equal("→ Camp 412 m  Local  waypoint", line);
    }

    [Fact]
    public void Named_targets_keep_their_name_and_blank_names_say_target()
    {
        Assert.Equal("Camp", HudLine.Label(NavTargetKind.Waypoint, " Camp "));
        Assert.Equal("target", HudLine.Label(NavTargetKind.Coordinate, null));
        Assert.Equal("target", HudLine.Label(NavTargetKind.Waypoint, "  "));
    }

    [Fact]
    public void A_stale_player_line_appends_the_map_age_and_greys_out()
    {
        var line = HudLine.Format("Maili", "412 m", HudLine.ModeBadge, HudLine.Status(NavTargetKind.Player));

        var stale = HudLine.WithTether(line, TeammateFixKind.Stale, TimeSpan.FromSeconds(30), out var staleGrey);

        Assert.Equal("→ Maili 412 m  Local  player 30s", stale);
        Assert.Equal("30s", TeammateAgeText.For(TeammateFixKind.Stale, TimeSpan.FromSeconds(30)));
        Assert.True(staleGrey);
        Assert.Equal(line, HudLine.WithTether(line, TeammateFixKind.Live, TimeSpan.Zero, out var liveGrey));
        Assert.False(liveGrey);
        Assert.False(HudLine.Show(mapAllowed: true, resolved: false));
    }

    [Fact]
    public void Nothing_shows_when_the_map_is_off()
    {
        Assert.False(HudLine.Show(mapAllowed: false, resolved: true));
        Assert.False(HudLine.Show(mapAllowed: true, resolved: false));
        Assert.True(HudLine.Show(mapAllowed: true, resolved: true));
    }
}
