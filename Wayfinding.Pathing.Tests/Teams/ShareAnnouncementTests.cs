using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class ShareAnnouncementTests
{
    [Theory]
    [InlineData(true, "Maili", "[Wayfinding] Maili is sharing location")]
    [InlineData(false, "Maili", "[Wayfinding] Maili stopped sharing location")]
    public void Human_line_matches_the_group_chat_sentence(bool sharing, string name, string expected)
    {
        Assert.Equal(expected, ShareAnnouncement.FormatHumanLine(name, sharing));
    }

    [Fact]
    public void Payload_round_trips()
    {
        var on = ShareAnnouncement.FormatPayload(true, "uid-1");
        var off = ShareAnnouncement.FormatPayload(false, "uid-1");

        Assert.Equal("wayfinding:v1:share:on:uid-1", on);
        Assert.Equal("wayfinding:v1:share:off:uid-1", off);
        Assert.True(ShareAnnouncement.TryParsePayload(on, out var onNotice));
        Assert.True(onNotice.Sharing);
        Assert.Equal("uid-1", onNotice.PlayerUid);
        Assert.True(ShareAnnouncement.TryParsePayload(off, out var offNotice));
        Assert.False(offNotice.Sharing);
    }

    [Fact]
    public void Chat_message_keeps_the_human_line_and_the_payload()
    {
        var message = ShareAnnouncement.FormatChatMessage("Maili", false, "uid-1");

        Assert.StartsWith("[Wayfinding] Maili stopped sharing location ", message, StringComparison.Ordinal);
        Assert.Contains("wayfinding:v1:share:off:uid-1", message, StringComparison.Ordinal);
        Assert.True(ShareAnnouncement.TryParsePayload(message, out var notice));
        Assert.False(notice.Sharing);
        Assert.Equal("uid-1", notice.PlayerUid);
    }

    [Fact]
    public void Vanilla_server_wrapper_still_yields_the_payload_and_the_sender_entity()
    {
        var message = ShareAnnouncement.FormatChatMessage("Maili", true, "uid-1");
        var data = "from: 4821,withoutPrefix:" + message;

        Assert.True(ShareAnnouncement.TryReadSenderEntityId(data, out var entityId));
        Assert.Equal(4821, entityId);
        Assert.True(ShareAnnouncement.TryParseIncoming(message, data, out var parsed));
        Assert.True(parsed.Notice.Sharing);
        Assert.Equal("uid-1", parsed.Notice.PlayerUid);
        Assert.Equal(4821, parsed.SenderEntityId);
    }

    [Fact]
    public void Forwarded_data_payload_parses_without_a_sender_entity()
    {
        var data = ShareAnnouncement.FormatPayload(true, "uid-1");

        Assert.False(ShareAnnouncement.TryReadSenderEntityId(data, out _));
        Assert.True(ShareAnnouncement.TryParseIncoming("[Wayfinding] Maili is sharing location", data, out var parsed));
        Assert.Equal("uid-1", parsed.Notice.PlayerUid);
        Assert.Null(parsed.SenderEntityId);
    }

    [Fact]
    public void Disagreeing_payloads_are_rejected()
    {
        var message = ShareAnnouncement.FormatChatMessage("Maili", true, "uid-1");
        var data = ShareAnnouncement.FormatPayload(false, "uid-2");

        Assert.False(ShareAnnouncement.TryParseIncoming(message, data, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("wayfinding:v1:share:maybe:uid")]
    [InlineData("wayfinding:v1:share:on:")]
    [InlineData("wayfinding:v2:share:on:uid")]
    [InlineData("WAYFINDING:v1:share:on:uid")]
    [InlineData("from: 9,withoutPrefix:[Wayfinding] Maili is sharing location")]
    public void Malformed_or_foreign_text_does_not_parse(string? text)
    {
        Assert.False(ShareAnnouncement.TryParsePayload(text, out _));
        Assert.False(ShareAnnouncement.TryParseIncoming(text, text, out _));
    }
}
