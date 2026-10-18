using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class ShareDeliveryTests
{
    private const int PartyId = 1;
    private const int CampId = 2;

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(900, 1000)]
    [InlineData(0, 900)]
    [InlineData(1500, 0)]
    public void Spacing_below_1500_still_waits_1500(int spacingMs, int rateLimitMs)
    {
        var clock = new ManualClock();
        var outbox = new ShareOutbox(clock, spacingMs, rateLimitMs);
        outbox.Enqueue(PartyId, "line");

        Assert.True(outbox.TryTake(out _, out _));
        clock.Milliseconds = 900;
        Assert.False(outbox.TryTake(out _, out _));
        clock.Milliseconds = 999;
        Assert.False(outbox.TryTake(out _, out _));
        clock.Milliseconds = 1499;
        Assert.False(outbox.TryTake(out _, out _));
        clock.Milliseconds = 1500;
        Assert.True(outbox.TryTake(out var groupId, out var text));
        Assert.Equal(PartyId, groupId);
        Assert.Equal("line", text);
    }

    [Fact]
    public void Dropped_send_is_retried_until_the_echo()
    {
        var clock = new ManualClock();
        var outbox = new ShareOutbox(clock);
        outbox.Enqueue(PartyId, "line");

        Assert.True(outbox.TryTake(out _, out var first));
        clock.Milliseconds = 1500;
        Assert.True(outbox.TryTake(out _, out var second));
        Assert.Equal(first, second);
        Assert.True(outbox.IsPending(PartyId));

        outbox.NoteOwnEcho(PartyId, "<strong>Local:</strong> " + second);
        clock.Milliseconds = 3000;
        Assert.False(outbox.TryTake(out _, out _));
        Assert.False(outbox.IsPending(PartyId));
    }

    [Fact]
    public void Own_echo_acks_only_that_group()
    {
        var clock = new ManualClock();
        var outbox = new ShareOutbox(clock);
        outbox.Enqueue(PartyId, "same");
        outbox.Enqueue(CampId, "same");

        Assert.True(outbox.TryTake(out var groupId, out var text));
        Assert.Equal(PartyId, groupId);
        outbox.NoteOwnEcho(PartyId, "<font color=\"#C4A35A\"><strong>Local:</strong></font> " + text);

        Assert.False(outbox.IsPending(PartyId));
        Assert.Equal("same", outbox.PendingText(CampId));
    }

    [Fact]
    public void Typed_own_chat_resets_spacing_without_acking()
    {
        var clock = new ManualClock();
        var outbox = new ShareOutbox(clock);
        outbox.Enqueue(PartyId, "announce");
        Assert.True(outbox.TryTake(out _, out _));

        clock.Milliseconds = 1400;
        outbox.NoteOwnEcho(PartyId, "<strong>Local:</strong> hello");

        Assert.True(outbox.IsPending(PartyId));
        clock.Milliseconds = 1500;
        Assert.False(outbox.TryTake(out _, out _));
        clock.Milliseconds = 1400 + 1500;
        Assert.True(outbox.TryTake(out _, out var text));
        Assert.Equal("announce", text);
    }

    [Fact]
    public void Join_announces_the_current_state_to_every_group()
    {
        var world = NewWorld(sharing: true);
        world.Session.ReplaceMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);

        Assert.Equal(2, world.Session.Outbox.PendingCount);
        Assert.Contains("wayfinding:v1:share:on:local", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);
        Assert.Contains("wayfinding:v1:share:on:local", world.Session.Outbox.PendingText(CampId), StringComparison.Ordinal);
        Assert.Contains("is sharing location", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);
    }

    [Fact]
    public void Group_change_reannounces_the_current_state()
    {
        var world = NewWorld(sharing: false);
        world.Session.ReplaceMembership([new TeamGroup(PartyId, "scouts")]);
        Ack(world, PartyId);

        world.Session.ReplaceMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);

        Assert.Contains("wayfinding:v1:share:off:local", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);
        Assert.Contains("wayfinding:v1:share:off:local", world.Session.Outbox.PendingText(CampId), StringComparison.Ordinal);
        Assert.Contains("stopped sharing location", world.Session.Outbox.PendingText(CampId), StringComparison.Ordinal);
    }

    [Fact]
    public void Share_off_is_still_announced()
    {
        var world = NewWorld(sharing: false);
        world.Session.AnnounceToMembership();

        var pending = world.Session.Outbox.PendingText(PartyId);
        Assert.NotNull(pending);
        Assert.Contains("wayfinding:v1:share:off:local", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("wayfinding:v1:share:on:", pending, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaving_a_group_drops_its_queued_announcement()
    {
        var world = NewWorld(sharing: true);
        world.Session.ReplaceMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);

        world.Session.ReplaceMembership([new TeamGroup(PartyId, "scouts")]);

        Assert.False(world.Session.Outbox.IsPending(CampId));
        Assert.Contains("wayfinding:v1:share:on:local", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);
    }

    [Fact]
    public void First_announcement_from_a_new_uid_queues_exactly_one_reply()
    {
        var world = NewWorld(sharing: true);
        var players = new[] { new OnlinePlayer("Maili", "maili"), new OnlinePlayer("Local", "local") };

        var first = world.Session.Receive(ShareChatKind.Other, PartyId, Line("Maili", "maili", true), players);

        Assert.Equal(ShareChatDisposition.Applied, first.Disposition);
        Assert.True(first.QueuedReply);
        Assert.Equal(1, world.Session.Outbox.PendingCount);
        Assert.Equal(world.Session.CurrentAnnouncement(), world.Session.Outbox.PendingText(PartyId));
        Assert.Contains("wayfinding:v1:share:on:local", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);

        var repeat = world.Session.Receive(ShareChatKind.Other, PartyId, Line("Maili", "maili", true), players);

        Assert.False(repeat.QueuedReply);
        Assert.Equal(1, world.Session.Outbox.PendingCount);

        Ack(world, PartyId);
        var again = world.Session.Receive(ShareChatKind.Other, PartyId, Line("Maili", "maili", false), players);

        Assert.Equal(ShareChatDisposition.Applied, again.Disposition);
        Assert.False(again.QueuedReply);
        Assert.False(world.Session.Outbox.IsPending(PartyId));
        Assert.False(world.Session.Model.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Reply_merges_into_the_pending_announcement_for_that_group()
    {
        var world = NewWorld(sharing: false);
        world.Session.AnnounceToMembership();
        var players = new[] { new OnlinePlayer("Maili", "maili") };

        var receipt = world.Session.Receive(ShareChatKind.Other, PartyId, Line("Maili", "maili", true), players);

        Assert.True(receipt.QueuedReply);
        Assert.Equal(1, world.Session.Outbox.PendingCount);
        Assert.Contains("wayfinding:v1:share:off:local", world.Session.Outbox.PendingText(PartyId), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_chat_does_not_apply_or_reset_spacing()
    {
        var world = NewWorld(sharing: true);
        world.Session.AnnounceToMembership();
        Assert.True(world.Session.Outbox.TryTake(out _, out var pending));
        var players = new[] { new OnlinePlayer("Maili", "maili") };

        world.Clock.Milliseconds = 100;
        var ignored = world.Session.Receive(ShareChatKind.Ignored, PartyId, Line("Maili", "maili", true), players);
        world.Session.Receive(ShareChatKind.Ignored, PartyId, "<strong>Local:</strong> " + pending, players);

        Assert.Equal(ShareChatDisposition.Ignored, ignored.Disposition);
        Assert.False(ignored.QueuedReply);
        Assert.False(world.Session.Model.IsVisibleTeammate("maili"));
        Assert.True(world.Session.Outbox.IsPending(PartyId));
        world.Clock.Milliseconds = 1500;
        Assert.True(world.Session.Outbox.TryTake(out _, out var text));
        Assert.Equal(pending, text);
    }

    [Fact]
    public void Own_chat_does_not_make_the_local_player_visible_or_reply()
    {
        var world = NewWorld(sharing: true);
        var players = new[] { new OnlinePlayer("Local", "local") };

        var receipt = world.Session.Receive(ShareChatKind.Own, PartyId, Line("Local", "local", true), players);

        Assert.False(receipt.QueuedReply);
        Assert.False(world.Session.Model.IsVisibleTeammate("local"));
        Assert.False(world.Session.Outbox.IsPending(PartyId));
    }

    [Fact]
    public void Own_typed_chat_through_the_session_resets_spacing()
    {
        var world = NewWorld(sharing: true);
        world.Session.AnnounceToMembership();
        Assert.True(world.Session.Outbox.TryTake(out var groupId, out var pending));

        world.Clock.Milliseconds = 1400;
        world.Session.Receive(ShareChatKind.Own, groupId, "<strong>Local:</strong> hello", []);

        Assert.Equal(pending, world.Session.Outbox.PendingText(groupId));
        world.Clock.Milliseconds = 1500;
        Assert.False(world.Session.Outbox.TryTake(out _, out _));
        world.Clock.Milliseconds = 1400 + 1500;
        Assert.True(world.Session.Outbox.TryTake(out _, out var text));
        Assert.Equal(pending, text);
    }

    private static void Ack(DeliveryWorld world, int groupId)
    {
        Assert.True(world.Session.Outbox.TryTake(out var taken, out var text));
        Assert.Equal(groupId, taken);
        world.Session.Receive(ShareChatKind.Own, groupId, "<strong>Local:</strong> " + text, []);
        Assert.False(world.Session.Outbox.IsPending(groupId));
    }

    private static string Line(string name, string uid, bool sharing)
    {
        return "<strong>" + name + ":</strong> " + ShareAnnouncement.FormatChatMessage(name, sharing, uid);
    }

    private static DeliveryWorld NewWorld(bool sharing)
    {
        return new DeliveryWorld(sharing);
    }

    private sealed class DeliveryWorld
    {
        public DeliveryWorld(bool sharing)
        {
            var model = new TeamModel();
            model.SetLocalPlayer("local");
            model.SetMembership([new TeamGroup(PartyId, "scouts")]);
            Session = new ShareSession(model, new ShareOutbox(Clock))
            {
                LocalName = "Local",
                Sharing = sharing,
            };
        }

        public ManualClock Clock { get; } = new();

        public ShareSession Session { get; }
    }

    private sealed class ManualClock : ITeamClock
    {
        public long Milliseconds { get; set; }
    }
}
