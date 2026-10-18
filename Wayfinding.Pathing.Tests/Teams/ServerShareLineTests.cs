using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class ServerShareLineTests
{
    private static readonly OnlinePlayer Maili = new("Maili", "maili");
    private static readonly OnlinePlayer Oren = new("Oren", "oren");
    private static readonly OnlinePlayer Griefer = new("Griefer", "griefer");
    private static readonly OnlinePlayer[] Online = [Maili, Oren, Griefer];

    [Fact]
    public void Forged_typed_chat_is_unverified()
    {
        var typed = "<strong>Maili:</strong> " + ShareAnnouncement.FormatPayload(true, "maili");
        var escaped = typed.Replace(">", "&gt;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        var message = "<strong>Griefer:</strong> " + escaped;

        var resolution = ServerShareLine.Resolve(message, Online);

        Assert.Equal(ShareLineStatus.Unverified, resolution.Status);
        Assert.Equal("Griefer", resolution.SenderName);
        Assert.Equal("maili", resolution.Notice.PlayerUid);
        Assert.True(resolution.Notice.Sharing);
    }

    [Fact]
    public void Wrong_uid_is_unverified()
    {
        var on = Resolve("Maili", ShareAnnouncement.FormatPayload(true, "oren"));
        var off = Resolve("Oren", ShareAnnouncement.FormatPayload(false, "maili"));

        Assert.Equal(ShareLineStatus.Unverified, on.Status);
        Assert.Equal("oren", on.Notice.PlayerUid);
        Assert.Equal(ShareLineStatus.Unverified, off.Status);
        Assert.False(off.Notice.Sharing);
    }

    [Fact]
    public void Unknown_name_is_unverified()
    {
        var resolution = Resolve("Ghost", ShareAnnouncement.FormatPayload(true, "ghost"));

        Assert.Equal(ShareLineStatus.Unverified, resolution.Status);
        Assert.Equal("Ghost", resolution.SenderName);
        Assert.Equal("ghost", resolution.Notice.PlayerUid);
    }

    [Fact]
    public void Missing_prefix_is_unverified()
    {
        var on = ServerShareLine.Resolve(ShareAnnouncement.FormatPayload(true, "maili"), Online);
        var off = ServerShareLine.Resolve(ShareAnnouncement.FormatChatMessage("Maili", false, "maili"), Online);

        Assert.Equal(ShareLineStatus.Unverified, on.Status);
        Assert.Equal("", on.SenderName);
        Assert.True(on.Notice.Sharing);
        Assert.Equal(ShareLineStatus.Unverified, off.Status);
        Assert.False(off.Notice.Sharing);
    }

    [Fact]
    public void Server_prefix_name_resolves_sender_uid()
    {
        var plain = Resolve("Maili", ShareAnnouncement.FormatChatMessage("Maili", true, "maili"));
        var colored = ServerShareLine.Resolve(
            "<font color=\"#C4A35A\"><strong>Maili:</strong></font> "
            + ShareAnnouncement.FormatPayload(false, "maili"),
            Online);

        Assert.Equal(ShareLineStatus.Verified, plain.Status);
        Assert.Equal("maili", plain.Notice.PlayerUid);
        Assert.True(plain.Notice.Sharing);
        Assert.Equal(ShareLineStatus.Verified, colored.Status);
        Assert.False(colored.Notice.Sharing);
        Assert.Equal("Maili", colored.SenderName);
    }

    [Fact]
    public void Ordinary_chat_has_no_payload()
    {
        var resolution = ServerShareLine.Resolve("<strong>Maili:</strong> hello", Online);

        Assert.Equal(ShareLineStatus.None, resolution.Status);
    }

    [Fact]
    public void Word_boundary_blocks_a_glued_prefix()
    {
        var resolution = Resolve("Maili", "xwayfinding:v1:share:on:maili");

        Assert.Equal(ShareLineStatus.None, resolution.Status);
    }

    [Fact]
    public void Extra_colon_in_the_uid_is_not_a_payload()
    {
        var resolution = Resolve("Maili", "wayfinding:v1:share:on:maili:extra");

        Assert.Equal(ShareLineStatus.None, resolution.Status);
    }

    [Fact]
    public void Two_players_with_the_same_name_are_unverified()
    {
        OnlinePlayer[] ambiguous = [Maili, new OnlinePlayer("Maili", "other")];
        var message = "<strong>Maili:</strong> " + ShareAnnouncement.FormatPayload(true, "maili");

        Assert.Equal(ShareLineStatus.Unverified, ServerShareLine.Resolve(message, ambiguous).Status);
    }

    [Fact]
    public void Staff_prefix_does_not_match_the_player_name()
    {
        var message = "<font color=\"#FF0000\"><strong>Staff Maili:</strong></font> "
            + ShareAnnouncement.FormatPayload(true, "maili");

        var resolution = ServerShareLine.Resolve(message, Online);

        Assert.Equal(ShareLineStatus.Unverified, resolution.Status);
        Assert.Equal("Staff Maili", resolution.SenderName);
    }

    private static ShareLineResolution Resolve(string name, string body)
    {
        return ServerShareLine.Resolve("<strong>" + name + ":</strong> " + body, Online);
    }
}
