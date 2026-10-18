using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class TeamVisibilityTests
{
    private const int PartyId = 1;
    private const int CampId = 2;

    private static readonly OnlinePlayer Maili = new("Maili", "maili");
    private static readonly OnlinePlayer Oren = new("Oren", "oren");
    private static readonly OnlinePlayer Local = new("Local", "local");
    private static readonly OnlinePlayer Griefer = new("Griefer", "griefer");
    private static readonly OnlinePlayer[] Party = [Maili, Oren, Local, Griefer];

    [Fact]
    public void Non_teammate_is_never_visible()
    {
        var team = PartyOf("local");

        Assert.False(team.IsVisibleTeammate("stranger"));
        Assert.False(team.IsVisibleTeammate(null));
        Assert.False(team.IsVisibleTeammate(""));
        var stranger = new OnlinePlayer("Stranger", "stranger");
        Assert.Equal(
            ShareChatDisposition.NotInGroup,
            team.TryApplyChat(
                99,
                "<strong>Stranger:</strong> " + ShareAnnouncement.FormatChatMessage("Stranger", true, "stranger"),
                [stranger]));
        Assert.False(team.IsVisibleTeammate("stranger"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Player_in_the_group_without_an_announcement_is_not_visible()
    {
        var team = PartyOf("local");

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Announcement_with_share_on_makes_the_player_visible()
    {
        var team = PartyOf("local");

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Maili));

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
    }

    [Fact]
    public void Announcement_with_share_off_hides_the_player()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, false, Maili));

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Nav_team_restricts_visibility_to_that_group()
    {
        var team = PartyOf("local");
        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Announce(team, PartyId, true, Maili);
        Announce(team, CampId, true, Oren);
        Assert.Equal(new[] { "maili", "oren" }, team.GetVisibleTeammates().ToArray());

        var restricted = team.TryRestrictTo("scouts", out var canonical);

        Assert.Equal(TeamRestrictResult.Restricted, restricted);
        Assert.Equal("scouts", canonical);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("oren"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());

        team.ClearRestriction();

        Assert.Null(team.TeamRestriction);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.True(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Nav_team_rejects_a_group_the_player_is_not_in()
    {
        var team = PartyOf("local");

        var result = team.TryRestrictTo("camp", out _);

        Assert.Equal(TeamRestrictResult.NotAMember, result);
        Assert.Null(team.TeamRestriction);
    }

    [Fact]
    public void Leaving_the_group_removes_visibility_until_a_new_announcement()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        Assert.True(team.IsVisibleTeammate("maili"));

        team.SetMembership([]);

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());

        team.SetMembership([new TeamGroup(PartyId, "scouts")]);

        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Evidence_in_one_group_does_not_count_for_another()
    {
        var team = PartyOf("local");
        team.SetMembership([new TeamGroup(CampId, "camp")]);
        Announce(team, CampId, true, Maili);

        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Assert.Equal(TeamRestrictResult.Restricted, team.TryRestrictTo("scouts", out _));

        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Opt_out_on_any_member_group_hides_the_player_immediately()
    {
        var team = PartyOf("local");
        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Announce(team, PartyId, true, Maili);
        Announce(team, CampId, true, Maili);
        Assert.Equal(TeamRestrictResult.Restricted, team.TryRestrictTo("scouts", out _));
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, CampId, false, Maili));

        Assert.False(team.IsVisibleTeammate("maili"));
        team.ClearRestriction();
        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Malformed_or_foreign_payloads_are_ignored()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        var before = team.RecordedShares();

        string?[] payloads =
        [
            null,
            "",
            "hello",
            "wayfinding:v1:share:maybe:maili",
            "wayfinding:v1:share:on:",
            "wayfinding:v1:share:ON:maili",
            "wayfinding:v2:share:on:other",
            "other:v1:share:on:other",
            "xwayfinding:v1:share:on:oren",
            "wayfinding:v1:share:on:maili:extra",
            "<strong>Maili:</strong> xwayfinding:v1:share:off:maili",
            "<strong>Maili:</strong> wayfinding:v1:share:off:maili:extra",
        ];

        foreach (var payload in payloads)
        {
            Assert.Equal(ShareChatDisposition.Ignored, team.TryApplyChat(PartyId, payload, Party));
        }

        AssertUnchanged(team, before);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("other"));
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Unverified_sender_is_ignored()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(ShareChatDisposition.Unverified, Post(team, PartyId, false, Maili, "maili", prefix: false));
        Assert.Equal(ShareChatDisposition.Unverified, Post(team, PartyId, true, Oren, "oren", prefix: false));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Ghost", ShareAnnouncement.FormatPayload(false, "ghost")), Party));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Maili", ShareAnnouncement.FormatPayload(true, "oren")), Party));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
        Assert.False(team.IsVisibleTeammate("ghost"));
    }

    [Fact]
    public void Far_away_typed_spoof_in_server_format_is_ignored()
    {
        var team = VisibleMaili(out var before);
        var forgedOn = EscapedSpoof(
            Griefer.Name,
            "<strong>Oren:</strong> " + ShareAnnouncement.FormatPayload(true, "oren"));
        var forgedOff = EscapedSpoof(
            Griefer.Name,
            "<strong>Maili:</strong> " + ShareAnnouncement.FormatPayload(false, "maili"));

        Assert.Equal(ShareChatDisposition.Unverified, team.TryApplyChat(PartyId, forgedOn, Party));
        Assert.Equal(ShareChatDisposition.Unverified, team.TryApplyChat(PartyId, forgedOff, Party));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Griefer", ShareAnnouncement.FormatPayload(true, "oren")), Party));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Griefer", ShareAnnouncement.FormatPayload(false, "maili")), Party));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
        Assert.False(team.IsVisibleTeammate("griefer"));
    }

    [Fact]
    public void Server_prefix_name_resolves_sender_uid()
    {
        var team = PartyOf("local");
        var message = ServerLine("Maili", ShareAnnouncement.FormatChatMessage("Maili", true, "maili"));
        var colored = "<font color=\"#C4A35A\"><strong>Maili:</strong></font> "
            + ShareAnnouncement.FormatChatMessage("Maili", true, "maili");

        Assert.Equal(ShareChatDisposition.Applied, team.TryApplyChat(PartyId, message, [Maili]));
        Assert.True(team.IsVisibleTeammate("maili"));

        team.Reset();
        team.SetLocalPlayer("local");
        team.SetMembership([new TeamGroup(PartyId, "scouts")]);
        Assert.Equal(ShareChatDisposition.Applied, team.TryApplyChat(PartyId, colored, [Maili]));
        Assert.Equal(new[] { new ShareRecord(PartyId, "maili", true) }, team.RecordedShares());

        var off = ServerLine("Maili", ShareAnnouncement.FormatChatMessage("Maili", false, "maili"));
        Assert.Equal(ShareChatDisposition.Applied, team.TryApplyChat(PartyId, off, [Maili]));
        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Wrong_uid_does_not_change_state()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, false, Maili, "oren"));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, true, Oren, "maili"));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Unknown_name_does_not_change_state()
    {
        var team = VisibleMaili(out var before);
        var ghost = new OnlinePlayer("Ghost", "ghost");

        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Ghost", ShareAnnouncement.FormatPayload(false, "maili")), Party));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Ghost", ShareAnnouncement.FormatPayload(true, "ghost")), [Maili]));
        Assert.Equal(ShareChatDisposition.Unverified, Announce(team, PartyId, true, ghost));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("ghost"));
    }

    [Fact]
    public void Missing_prefix_does_not_change_state()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(ShareChatDisposition.Unverified, Post(team, PartyId, false, Maili, "maili", prefix: false));
        Assert.Equal(ShareChatDisposition.Unverified, Post(team, PartyId, true, Oren, "oren", prefix: false));
        Assert.Equal(
            ShareChatDisposition.Ignored,
            team.TryApplyChat(PartyId, "from: 4821,withoutPrefix:" + ShareAnnouncement.FormatPayload(false, "maili"), Party));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Payload_typed_in_a_group_the_player_is_not_in_does_not_change_state()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(ShareChatDisposition.NotInGroup, Post(team, 99, false, Maili, "maili"));
        Assert.Equal(ShareChatDisposition.NotInGroup, Post(team, 99, true, Oren, "oren"));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Duplicate_online_names_are_not_verified()
    {
        var team = VisibleMaili(out var before);
        OnlinePlayer[] ambiguous = [Maili, new OnlinePlayer("Maili", "other")];

        Assert.Equal(
            ShareChatDisposition.Unverified,
            team.TryApplyChat(PartyId, ServerLine("Maili", ShareAnnouncement.FormatPayload(false, "maili")), ambiguous));

        AssertUnchanged(team, before);
    }

    [Fact]
    public void Local_player_is_never_a_visible_teammate()
    {
        var team = PartyOf("local");
        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Local));

        Assert.False(team.IsVisibleTeammate("local"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Share_on_after_share_off_is_visible_again()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, false, Maili);
        Assert.False(team.IsVisibleTeammate("maili"));

        Announce(team, PartyId, true, Maili);

        Assert.True(team.IsVisibleTeammate("maili"));
    }

    private static TeamModel VisibleMaili(out IReadOnlyList<ShareRecord> recorded)
    {
        var team = PartyOf("local");
        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Maili));
        Assert.True(team.IsVisibleTeammate("maili"));
        recorded = team.RecordedShares();
        return team;
    }

    private static void AssertUnchanged(TeamModel team, IReadOnlyList<ShareRecord> recorded)
    {
        Assert.Equal(recorded, team.RecordedShares());
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
        Assert.True(team.IsVisibleTeammate("maili"));
    }

    private static ShareChatDisposition Announce(TeamModel team, int groupId, bool sharing, OnlinePlayer sender)
    {
        return Post(team, groupId, sharing, sender, sender.Uid);
    }

    private static ShareChatDisposition Post(
        TeamModel team,
        int groupId,
        bool sharing,
        OnlinePlayer sender,
        string payloadUid,
        bool prefix = true)
    {
        var payload = ShareAnnouncement.FormatChatMessage(sender.Name, sharing, payloadUid);
        var message = prefix ? ServerLine(sender.Name, payload) : payload;
        return team.TryApplyChat(groupId, message, Party);
    }

    private static string ServerLine(string name, string body)
    {
        return "<strong>" + name + ":</strong> " + body;
    }

    private static string EscapedSpoof(string attacker, string typed)
    {
        var escaped = typed.Replace(">", "&gt;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        return ServerLine(attacker, escaped);
    }

    private static TeamModel PartyOf(string localUid)
    {
        var team = new TeamModel();
        team.SetLocalPlayer(localUid);
        team.SetMembership([new TeamGroup(PartyId, "scouts")]);
        return team;
    }
}
