using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class TeamVisibilityTests
{
    private const int PartyId = 1;
    private const int CampId = 2;

    [Fact]
    public void Non_teammate_is_never_visible()
    {
        var team = PartyOf("local");

        Assert.False(team.IsVisibleTeammate("stranger"));
        Assert.False(team.IsVisibleTeammate(null));
        Assert.False(team.IsVisibleTeammate(""));
        Assert.False(team.TryApplyChat(99, null, ShareAnnouncement.FormatPayload(true, "stranger"), senderUid: null));
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

        Assert.True(team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: "maili"));

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
    }

    [Fact]
    public void Announcement_with_share_off_hides_the_player()
    {
        var team = PartyOf("local");
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.True(team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(false, "maili"), senderUid: null));

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
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);
        team.TryApplyChat(CampId, null, ShareAnnouncement.FormatPayload(true, "oren"), senderUid: null);
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
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);
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
        team.TryApplyChat(CampId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);

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
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);
        team.TryApplyChat(CampId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);
        Assert.Equal(TeamRestrictResult.Restricted, team.TryRestrictTo("scouts", out _));
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.True(team.TryApplyChat(CampId, null, ShareAnnouncement.FormatPayload(false, "maili"), senderUid: null));

        Assert.False(team.IsVisibleTeammate("maili"));
        team.ClearRestriction();
        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Malformed_or_foreign_payloads_are_ignored()
    {
        var team = PartyOf("local");
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);

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
            "from: 12,withoutPrefix:[Wayfinding] Maili is sharing location",
        ];

        foreach (var payload in payloads)
        {
            Assert.False(team.TryApplyChat(PartyId, payload, payload, senderUid: null));
        }

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("other"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
    }

    [Fact]
    public void Payload_uid_that_does_not_match_the_sender_is_ignored()
    {
        var team = PartyOf("local");

        Assert.False(team.TryApplyChat(
            PartyId,
            null,
            ShareAnnouncement.FormatPayload(true, "maili"),
            senderUid: "someone-else"));
        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("someone-else"));

        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: "maili");
        Assert.False(team.TryApplyChat(
            PartyId,
            null,
            ShareAnnouncement.FormatPayload(false, "maili"),
            senderUid: "someone-else"));

        Assert.True(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Payload_is_accepted_when_the_chat_event_does_not_expose_a_sender()
    {
        var team = PartyOf("local");

        Assert.True(team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null));
        Assert.True(team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "oren"), senderUid: ""));

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.True(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Vanilla_wrapped_chat_line_updates_visibility()
    {
        var team = PartyOf("local");
        var message = ShareAnnouncement.FormatChatMessage("Maili", true, "maili");
        var data = "from: 4821,withoutPrefix:" + message;

        Assert.True(team.TryApplyChat(PartyId, message, data, senderUid: "maili"));
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.False(team.TryApplyChat(PartyId, message, data, senderUid: "other"));
        Assert.True(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Local_player_is_never_a_visible_teammate()
    {
        var team = PartyOf("local");
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "local"), senderUid: "local");

        Assert.False(team.IsVisibleTeammate("local"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Share_on_after_share_off_is_visible_again()
    {
        var team = PartyOf("local");
        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(false, "maili"), senderUid: null);
        Assert.False(team.IsVisibleTeammate("maili"));

        team.TryApplyChat(PartyId, null, ShareAnnouncement.FormatPayload(true, "maili"), senderUid: null);

        Assert.True(team.IsVisibleTeammate("maili"));
    }

    private static TeamModel PartyOf(string localUid)
    {
        var team = new TeamModel();
        team.SetLocalPlayer(localUid);
        team.SetMembership([new TeamGroup(PartyId, "scouts")]);
        return team;
    }
}
